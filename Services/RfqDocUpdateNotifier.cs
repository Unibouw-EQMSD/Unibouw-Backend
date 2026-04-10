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
    private async Task<(string ProjectName, string RfqNumber, string WorkItemNames)> GetRfqDetailsAsync(Guid rfqId)
    {
        const string sql = @"
            SELECT 
                p.Name AS ProjectName,
                r.RfqNumber,
                wi.Name AS WorkItemName
            FROM Rfq r
            INNER JOIN Projects p ON r.ProjectID = p.ProjectID
            LEFT JOIN RfqWorkItems rwi ON r.RfqID = rwi.RfqID
            LEFT JOIN WorkItems wi ON rwi.WorkItemID = wi.WorkItemID
            WHERE r.RfqID = @RfqID
        ";
        using var con = new SqlConnection(_cs);

        var results = (await con.QueryAsync(sql, new { RfqID = rfqId })).ToList();

        var projectName = results.FirstOrDefault()?.ProjectName ?? "(Unknown Project)";
        var rfqNumber = results.FirstOrDefault()?.RfqNumber ?? "(Unknown RFQ)";
        var workItemNames = results
            .Select(r => (string)r.WorkItemName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();
        var workItemName = workItemNames.Any() ? string.Join(", ", workItemNames) : "-";

        return (projectName, rfqNumber, workItemName);
    }

    public async Task NotifyForNewProjectDocAsync(Guid projectId, Guid newProjectDocumentId, string docName)
    {
        using var con = Con;
        // 1) RFQs already sent in same project missing the doc
        const string rfqSql = @"
SELECT r.RfqID
FROM dbo.Rfq r
WHERE r.ProjectID = @ProjectID
  AND r.IsDeleted = 0
  AND r.Status = 'Sent'
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RfqDocumentsMapping m
      WHERE m.RfqID = r.RfqID AND m.ProjectDocumentID = @DocId
  );";
        var rfqIds = (await con.QueryAsync<Guid>(rfqSql, new { ProjectID = projectId, DocId = newProjectDocumentId })).ToList();
        if (!rfqIds.Any()) return;

        foreach (var rfqId in rfqIds)
        {
            var (projectName, rfqNumber, workItemName) = await GetRfqDetailsAsync(rfqId);

            // 2) eligible subcontractors
            const string subsSql = @"
SELECT SubcontractorID
FROM dbo.RfqSubcontractorResponse
WHERE RfqID = @RfqID
  AND Status IN ('May Respond Later','Interested','Submitted Quote');";
            var subs = (await con.QueryAsync<Guid>(subsSql, new { RfqID = rfqId })).ToList();

            foreach (var subId in subs)
            {
                // 3) idempotency check
                const string alreadySql = @"
SELECT COUNT(1)
FROM dbo.RfqDocumentNotificationLog
WHERE RfqID=@RfqID AND ProjectDocumentID=@DocId AND SubcontractorID=@SubId;";
                var already = await con.ExecuteScalarAsync<int>(alreadySql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });
                if (already > 0) continue;

                // 4) Email body with all details
                var commentHtml = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the same RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item:</strong> {WebUtility.HtmlEncode(workItemName)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(docName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                await _email.ReplyRfqThreadAsync(rfqId, subId, commentHtml);

                // 5) log it
                const string logSql = @"
INSERT INTO dbo.RfqDocumentNotificationLog (LogID, RfqID, ProjectDocumentID, SubcontractorID, SentOn)
VALUES (NEWID(), @RfqID, @DocId, @SubId, SYSUTCDATETIME());";
                await con.ExecuteAsync(logSql, new { RfqID = rfqId, DocId = newProjectDocumentId, SubId = subId });
            }
        }
    }
}