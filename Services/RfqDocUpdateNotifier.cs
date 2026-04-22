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

    // Helper to fetch details
    private async Task<(string ProjectName, string ProjectNumber, string RfqNumber, string WorkItemsText)> GetRfqDetailsAsync(Guid rfqId)
    {
        const string sql = @"
SELECT 
    p.Name        AS ProjectName,
    p.Number      AS ProjectNumber,
    r.RfqNumber   AS RfqNumber
FROM dbo.Rfq r
JOIN dbo.Projects p ON p.ProjectID = r.ProjectID
WHERE r.RfqID = @RfqID;

SELECT 
    wi.Number AS WorkItemNumber,
    wi.Name   AS WorkItemName
FROM dbo.RfqWorkItemMapping rwm
JOIN dbo.WorkItems wi ON wi.WorkItemID = rwm.WorkItemID
WHERE rwm.RfqID = @RfqID
  AND wi.IsDeleted = 0
ORDER BY wi.Number;
";

        using var con = Con; // or _connection
        using var multi = await con.QueryMultipleAsync(sql, new { RfqID = rfqId });

        var head = await multi.ReadSingleAsync<dynamic>();
        var workItems = (await multi.ReadAsync<dynamic>()).ToList();

        string workItemsText =
            workItems.Any()
                ? string.Join(", ", workItems.Select(w => $"{w.WorkItemNumber} - {w.WorkItemName}"))
                : "-";

        return (
            (string)head.ProjectName,
            (string)head.ProjectNumber,
            (string)head.RfqNumber,
            workItemsText
        );
    }

    public async Task NotifyForNewProjectDocAsync(Guid projectId, Guid newProjectDocumentId, string docName)
    {
        using var con = Con;

        // 1) All RFQs in this project with status 'Sent'
        const string rfqSql = @"
SELECT r.RfqID
FROM dbo.Rfq r
WHERE r.ProjectID = @ProjectID
  AND r.IsDeleted = 0
  AND r.Status = 'Sent'";
        var rfqIds = (await con.QueryAsync<Guid>(rfqSql, new { ProjectID = projectId })).ToList();
        if (!rfqIds.Any()) return;

        foreach (var rfqId in rfqIds)
        {
            var (projectName, projectNumber, rfqNumber, workItemsText) = await GetRfqDetailsAsync(rfqId);
            // 2) Eligible subcontractors
            const string subsSql = @"
SELECT SubcontractorID
FROM dbo.RfqSubcontractorResponse
WHERE RfqID = @RfqID
  AND Status IN ('May Respond Later','Interested','Submitted Quote');";
            var subs = (await con.QueryAsync<Guid>(subsSql, new { RfqID = rfqId })).ToList();

            foreach (var subId in subs)
            {
                // 3) Idempotency check
                const string alreadySql = @"
SELECT COUNT(1)
FROM dbo.RfqDocumentNotificationLog
WHERE RfqID=@RfqID AND ProjectDocumentID=@DocId AND SubcontractorID=@SubId;";
                var already = await con.ExecuteScalarAsync<int>(alreadySql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });
                if (already > 0) continue;

                // 4) Email body with that doc's name
                var commentHtml = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the same RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:<strong>Project:</strong> {WebUtility.HtmlEncode(projectNumber)} - {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item(s):</strong> {WebUtility.HtmlEncode(workItemsText)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(docName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                await _email.ReplyRfqThreadAsync(rfqId, subId, commentHtml);

                // 5) Log the notification
                const string logSql = @"
INSERT INTO dbo.RfqDocumentNotificationLog (LogID, RfqID, ProjectDocumentID, SubcontractorID, SentOn)
VALUES (NEWID(), @RfqID, @DocId, @SubId, SYSUTCDATETIME());";
                await con.ExecuteAsync(logSql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });
            }
        }
    }
}