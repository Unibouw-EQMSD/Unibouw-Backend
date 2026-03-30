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


        private async Task<string?> GetSubcontractorEmailById(Guid subcontractorId)
        {
            const string sql = "SELECT TOP 1 Email FROM Subcontractors WHERE SubcontractorID = @SubID";
            using var con = new SqlConnection(_connectionString);
            return await con.ExecuteScalarAsync<string?>(sql, new { SubID = subcontractorId });
        }
        public async Task NotifyForEditRfqDocs(
     Guid projectId,
     Guid rfqId,
     IEnumerable<dynamic> linkedDocs,
     string projectName,
     string rfqNumber,
     string workItemName,
     string userEmail,
     string? language = "en")
        {
            foreach (var doc in linkedDocs)
            {
                var subs = (await _repo.GetEligibleSubcontractorsForRfqAsync(rfqId)).ToList();
                foreach (var sub in subs)
                {
                    const string sql = @"
SELECT COUNT(1)
FROM RfqDocumentLink l
JOIN RfqSubcontractorMapping m ON l.RfqID = m.RfqID
JOIN Rfq r ON m.RfqID = r.RfqID
WHERE l.ProjectDocumentID = @DocID
  AND m.SubcontractorID = @SubID
  AND r.ProjectID = @ProjectID
  AND l.RfqID <> @CurrentRfqID;"; // <<--- exclude current RFQ

                    using var con = new SqlConnection(_connectionString);
                    var alreadyHadDoc = await con.ExecuteScalarAsync<int>(sql, new
                    {
                        DocID = doc.ProjectDocumentID,
                        SubID = sub.SubcontractorID,
                        ProjectID = projectId,
                        CurrentRfqID = rfqId
                    });

                    if (alreadyHadDoc > 0)
                        continue; // Sub already had this doc in a previous RFQ for this project

                    var alreadyNotified = await _repo.WasNotificationSentAsync(rfqId, doc.ProjectDocumentID, sub.SubcontractorID);
                    if (alreadyNotified)
                        continue;

                    var subject = "RFQ Update – New Document Added for Review";
                    var htmlBody = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item:</strong> {WebUtility.HtmlEncode(workItemName)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(doc.FileName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                    await _email.SendSimpleEmailAsync(sub.Email!, subject, htmlBody);
                    await _repo.LogNotificationAsync(rfqId, doc.ProjectDocumentID, sub.SubcontractorID);
                }
            }
        }

        public async Task NotifyAsync(
            Guid projectId,
            Guid currentRfqId,
            Guid projectDocumentId,
            string projectName,
            string rfqNumber,
            string workItemName,
            string docName,
            string userEmail,
            string? language = "en")
        {
            // Get all eligible subs for the current RFQ (edit or create)
            var subs = (await _repo.GetEligibleSubcontractorsForRfqAsync(currentRfqId)).ToList();
            _logger.LogInformation("NOTIFY: {Count} eligible subs for RFQ {RFQ} / Doc {Doc}", subs.Count, currentRfqId, docName);

            if (!subs.Any()) return;

            foreach (var sub in subs)
            {
                // Check if this doc was ever sent to this sub in this project
                const string sql = @"
SELECT COUNT(1)
FROM RfqDocumentLink l
JOIN RfqSubcontractorMapping m ON l.RfqID = m.RfqID
JOIN Rfq r ON m.RfqID = r.RfqID
WHERE l.ProjectDocumentID = @DocID
  AND m.SubcontractorID = @SubID
  AND r.ProjectID = @ProjectID;";

                using var con = new SqlConnection(_connectionString);
                var alreadyHadDoc = await con.ExecuteScalarAsync<int>(sql, new
                {
                    DocID = projectDocumentId,
                    SubID = sub.SubcontractorID,
                    ProjectID = projectId
                });

                _logger.LogInformation("NOTIFY: Sub {Sub} alreadyHadDoc={HadDoc}", sub.SubcontractorID, alreadyHadDoc);

                if (alreadyHadDoc > 0)
                {
                    _logger.LogInformation("NOTIFY: SKIP sub {Sub}, already had doc {Doc}", sub.SubcontractorID, docName);
                    continue; // This sub already got this doc in some RFQ for this project
                }

                // Optionally, prevent duplicate notifications if needed
                var alreadyNotified = await _repo.WasNotificationSentAsync(currentRfqId, projectDocumentId, sub.SubcontractorID);
                if (alreadyNotified)
                {
                    _logger.LogInformation("NOTIFY: SKIP sub {Sub}, already notified for this RFQ/doc", sub.SubcontractorID);
                    continue;
                }

                // Compose notification email
                var subject = "RFQ Update – New Document Added for Review";
                var htmlBody = $@"
<p>Dear Subcontractor,</p>
<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>
<p>Please review the newly added document using the RFQ link shared earlier and consider it while preparing or updating your quotation.</p>
<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item:</strong> {WebUtility.HtmlEncode(workItemName)}<br/>
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(docName)}
</p>
<p>Best regards<br/>QMS Team</p>";

                _logger.LogInformation("NOTIFY: Sending notification to {Email} for doc {Doc}", sub.Email, docName);

                await _email.SendSimpleEmailAsync(sub.Email!, subject, htmlBody);

                // Log notification for this RFQ-Doc-Sub to prevent future duplicates
                await _repo.LogNotificationAsync(currentRfqId, projectDocumentId, sub.SubcontractorID);
                _logger.LogInformation("NOTIFY: Notification logged for RFQ {RFQ} Doc {Doc} Sub {Sub}", currentRfqId, projectDocumentId, sub.SubcontractorID);
            }
        }
    }
}