using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;
using UnibouwAPI.Repositories.Interfaces;

public class RfqDocUpdateNotifier
{
    private readonly string _cs;
    private readonly IEmail _email;

    public RfqDocUpdateNotifier(IConfiguration cfg, IEmail email)
    {
        _cs = cfg.GetConnectionString("UnibouwDbConnection")!;
        _email = email;
    }

    private IDbConnection Con => new SqlConnection(_cs);

    private async Task<(string ProjectName, string ProjectNumber, string RfqNumber)> GetRfqHeaderAsync(
        IDbConnection con, Guid rfqId)
    {
        const string sql = @"
SELECT 
    p.Name   AS ProjectName,
    p.Number AS ProjectNumber,
    r.RfqNumber AS RfqNumber
FROM dbo.Rfq r
JOIN dbo.Projects p ON p.ProjectID = r.ProjectID
WHERE r.RfqID = @RfqID;";

        var head = await con.QuerySingleAsync<dynamic>(sql, new { RfqID = rfqId });
        return ((string)head.ProjectName, (string)head.ProjectNumber, (string)head.RfqNumber);
    }

    private async Task<Dictionary<Guid, string>> GetWorkItemTextMapAsync(
        IDbConnection con, IEnumerable<Guid> workItemIds)
    {
        const string sql = @"
SELECT WorkItemID, Number, Name
FROM dbo.WorkItems
WHERE WorkItemID IN @Ids AND IsDeleted = 0;";

        var rows = await con.QueryAsync<dynamic>(sql, new { Ids = workItemIds.ToArray() });

        return rows.ToDictionary(
            r => (Guid)r.WorkItemID,
            r => $"{r.Number} - {r.Name}"
        );
    }

    public async Task NotifyForNewProjectDocAsync(Guid projectId, Guid newProjectDocumentId, string docName)
    {
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync();

        // 1) All "Sent" RFQs in this project
        const string rfqSql = @"
SELECT r.RfqID
FROM dbo.Rfq r
WHERE r.ProjectID = @ProjectID
  AND r.IsDeleted = 0
  AND r.Status = 'Sent';";

        var rfqIds = (await con.QueryAsync<Guid>(rfqSql, new { ProjectID = projectId })).ToList();
        if (!rfqIds.Any()) return;

        foreach (var rfqId in rfqIds)
        {
            // 2) Skip RFQs that already have this doc linked
            const string rfqHasDocSql = @"
SELECT COUNT(1)
FROM dbo.RfqDocuments rd
WHERE rd.RfqID = @RfqID
  AND rd.ProjectDocumentID = @DocId
  AND ISNULL(rd.IsDeleted, 0) = 0;";

            var rfqAlreadyHasDoc = await con.ExecuteScalarAsync<int>(
                rfqHasDocSql, new { RfqID = rfqId, DocId = newProjectDocumentId });

            if (rfqAlreadyHasDoc > 0) continue;

            // 3) Identify which workitems this doc is relevant to (critical for your requirement)
            const string docWiSql = @"
SELECT WorkItemID
FROM dbo.RfqDocumentWorkItemMapping
WHERE RfqID = @RfqID
  AND ProjectDocumentID = @DocId;";

            var docWorkItemIds = (await con.QueryAsync<Guid>(
                docWiSql, new { RfqID = rfqId, DocId = newProjectDocumentId })).Distinct().ToList();

            // If doc isn't mapped to any workitem, don't notify anyone (prevents wrong notifications)
            if (!docWorkItemIds.Any()) continue;

            // 4) Eligible subcontractors ONLY for those workitems and ONLY if not declined
            // Assumes RfqSubcontractorResponse has WorkItemID and Status per workitem.
            const string eligibleSql = @"
SELECT SubcontractorID, WorkItemID
FROM dbo.RfqSubcontractorResponse
WHERE RfqID = @RfqID
  AND WorkItemID IN @WorkItemIds
  AND Status IN ('May Respond Later','Interested','Submitted Quote');";

            var eligiblePairs = (await con.QueryAsync<(Guid SubcontractorID, Guid WorkItemID)>(
                eligibleSql, new { RfqID = rfqId, WorkItemIds = docWorkItemIds }))
                .ToList();

            if (!eligiblePairs.Any()) continue;

            // 5) Build RFQ header once
            var (projectName, projectNumber, rfqNumber) = await GetRfqHeaderAsync(con, rfqId);

            // 6) Prepare workitem display text only for relevant workitems
            var workItemTextMap = await GetWorkItemTextMapAsync(con, docWorkItemIds);

            // group by subcontractor => each subcontractor sees ONLY relevant workitems
            var bySub = eligiblePairs
                .GroupBy(x => x.SubcontractorID)
                .ToList();

            foreach (var g in bySub)
            {
                var subId = g.Key;

                // Idempotency: don’t send again for same RFQ/doc/sub
                const string alreadySql = @"
SELECT COUNT(1)
FROM dbo.RfqDocumentNotificationLog
WHERE RfqID=@RfqID AND ProjectDocumentID=@DocId AND SubcontractorID=@SubId;";

                var already = await con.ExecuteScalarAsync<int>(
                    alreadySql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });

                if (already > 0) continue;

                var relevantWorkItemsText = string.Join(", ",
                    g.Select(x => x.WorkItemID)
                     .Distinct()
                     .Where(id => workItemTextMap.ContainsKey(id))
                     .Select(id => workItemTextMap[id])
                );

                if (string.IsNullOrWhiteSpace(relevantWorkItemsText))
                    continue;

                var commentHtml = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the same RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectNumber)} - {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item(s):</strong> {WebUtility.HtmlEncode(relevantWorkItemsText)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(docName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                // Send in same thread (your existing logic)
                await _email.ReplyRfqThreadAsync(rfqId, subId, commentHtml);

                // Log notification
                const string logSql = @"
INSERT INTO dbo.RfqDocumentNotificationLog
(LogID, RfqID, ProjectDocumentID, SubcontractorID, SentOn)
VALUES (NEWID(), @RfqID, @DocId, @SubId, SYSUTCDATETIME());";

                await con.ExecuteAsync(logSql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });
            }
        }
    }
}