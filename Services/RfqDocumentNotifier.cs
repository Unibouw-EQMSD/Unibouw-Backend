using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Net;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Services
{
    public class RfqDocumentNotifier
    {
        private readonly RfqDocumentNotificationRepository _repo;
        private readonly IEmail _email;
        private readonly IRFQConversationMessage _convo;
        private readonly string _connectionString;
        private readonly ILogger<RfqDocumentNotifier> _logger;

        public RfqDocumentNotifier(
            IConfiguration configuration,
            RfqDocumentNotificationRepository repo,
            IEmail email,
            IRFQConversationMessage convo,
            ILogger<RfqDocumentNotifier> logger)
        {
            _repo = repo;
            _email = email;
            _convo = convo;
            _connectionString = configuration.GetConnectionString("UnibouwDbConnection")
                ?? throw new InvalidOperationException("Connection string 'UnibouwDbConnection' is not configured.");
            _logger = logger;
        }

        // Helper to fetch details
        private async Task<(string ProjectName, string ProjectNumber, string RfqNumber, string WorkItemsText)> GetRfqDetailsAsync(Guid rfqId)
        {
            const string sql = @"
SELECT 
    p.Name   AS ProjectName,
    p.Number AS ProjectNumber,
    r.RfqNumber AS RfqNumber
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

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

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


        public async Task NotifyAsync(
       Guid projectId,
       Guid currentRfqId,
       Guid projectDocumentId,
       string docName,
       string userEmail,
       string? language = "en")
        {
            // Fetch details from DB for every call, ensures correct data for each doc
            var (projectName, projectNumber, rfqNumber, workItemsText) = await GetRfqDetailsAsync(currentRfqId);
            var subs = (await _repo.GetEligibleSubcontractorsForRfqAsync(currentRfqId)).ToList();
            _logger.LogInformation("NOTIFY: {Count} eligible subs for RFQ {RFQ} / Doc {Doc}", subs.Count, currentRfqId, docName);
            if (!subs.Any()) return;

            foreach (var sub in subs)
            {
                // ❌ Remove all "already had doc" checks!
                // ✅ Only check if already notified
                var alreadyNotified = await _repo.WasNotificationSentAsync(currentRfqId, projectDocumentId, sub.SubcontractorID);
                if (alreadyNotified)
                {
                    _logger.LogInformation("NOTIFY: SKIP sub {Sub}, already notified for this RFQ/doc", sub.SubcontractorID);
                    continue;
                }

                var subject = "RFQ Update – New Document Added for Review";
                var htmlBody = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectNumber)} - {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item(s):</strong> {WebUtility.HtmlEncode(workItemsText)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(docName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                _logger.LogInformation("NOTIFY: Sending notification to {Email} for doc {Doc}", sub.Email, docName);
                try
                {
                    await _email.SendSimpleEmailAsync(sub.Email!, subject, htmlBody);
                    await _repo.LogNotificationAsync(currentRfqId, projectDocumentId, sub.SubcontractorID);
                    _logger.LogInformation("NOTIFY: Notification logged for RFQ {RFQ} Doc {Doc} Sub {Sub}", currentRfqId, projectDocumentId, sub.SubcontractorID);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NOTIFY: Failed to send notification to {Email} for doc {Doc}", sub.Email, docName);
                }
            }
        }

        public async Task NotifyForEditRfqDocs(
    Guid projectId,
    Guid rfqId,
    IEnumerable<dynamic> linkedDocs,
    string userEmail,
    string? language = "en")
        {
            var (projectName, projectNumber, rfqNumber, workItemsText) = await GetRfqDetailsAsync(rfqId);
            _logger.LogInformation("NotifyForEditRfqDocs: projectId={ProjectId}, rfqId={RfqId}, docsCount={DocsCount}, user={User}",
                projectId, rfqId, linkedDocs.Count(), userEmail);

            foreach (var doc in linkedDocs)
            {
                var subs = (await _repo.GetEligibleSubcontractorsForRfqAsync(rfqId)).ToList();
                foreach (var sub in subs)
                {
                    // ✅ Only check notification log for idempotency
                    var alreadyNotified = await _repo.WasNotificationSentAsync(rfqId, doc.ProjectDocumentID, sub.SubcontractorID);
                    if (alreadyNotified)
                    {
                        _logger.LogInformation("NOTIFY: SKIP sub {Sub}, already notified for this RFQ/doc", sub.SubcontractorID);
                        continue;
                    }

                    var subject = "RFQ Update – New Document Added for Review";
                    var htmlBody = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectNumber)} - {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item(s):</strong> {WebUtility.HtmlEncode(workItemsText)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(doc.Filename)}
</p>
<p>Best regards<br/>QMS Team</p>";

                    try
                    {
                        await _email.SendSimpleEmailAsync(sub.Email!, subject, htmlBody);
                        await _repo.LogNotificationAsync(rfqId, doc.ProjectDocumentID, sub.SubcontractorID);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }
    }
}