using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Net;
using UnibouwAPI.Models;
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
            // STEP 1: Get ONLY previous SENT RFQs (important rule)
            var previousRfqs = await _repo.GetPreviousSentRfqsMissingDocumentAsync(
                projectId,
                currentRfqId,
                projectDocumentId
            );

            if (previousRfqs == null || !previousRfqs.Any())
            {
                _logger.LogInformation(
                    "NOTIFY: No eligible previous RFQs found. No notification triggered."
                );
                return;
            }

            foreach (var rfqId in previousRfqs)
            {
                // STEP 2: Load RFQ details
                var (projectName, projectNumber, rfqNumber, _) =
                    await GetRfqDetailsAsync(rfqId);

                // STEP 3: Get eligible subcontractors (already filtered in SQL)
                var rows = (await _repo.GetEligibleSubcontractorsForRfqAsync(rfqId)).ToList();

                if (!rows.Any())
                {
                    _logger.LogInformation(
                        "NOTIFY: No eligible subcontractors for RFQ {RfqId}",
                        rfqId
                    );
                    continue;
                }

                // STEP 4: Group by subcontractor
                var grouped = rows.GroupBy(x => new { x.SubcontractorID, x.Email });

                foreach (var group in grouped)
                {
                    var subId = group.Key.SubcontractorID;
                    var email = group.Key.Email;

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        _logger.LogWarning("NOTIFY: Missing email for subcontractor {Sub}", subId);
                        continue;
                    }

                    // STEP 5: Prevent duplicate notification
                    var alreadyNotified = await _repo.WasNotificationSentAsync(
                        rfqId,
                        projectDocumentId,
                        subId
                    );

                    if (alreadyNotified)
                    {
                        _logger.LogInformation("NOTIFY: SKIP already sent sub {Sub}", subId);
                        continue;
                    }

                    // STEP 6: Work item list
                    var allowedStatuses = new HashSet<string>
{
    "Interested",
    "Maybe Later",
    "Viewed",
    "Not Responded"
};

                    var workItemsText = string.Join(", ",
                        group
                            .Where(w =>
                                !string.IsNullOrWhiteSpace(w.RfqResponseStatusName) &&
                                allowedStatuses.Contains(w.RfqResponseStatusName.Trim())
                            )
                            .GroupBy(w => w.WorkItemID)
                            .Select(g => g.First())
                            .Select(w => $"{w.WorkItemNumber} - {w.WorkItemName}")
                    );

                    // STEP 7: Email body (STRICT AC TEMPLATE)
                    var subject = "Re: RFQ Update – New Document Added for Review";

                    var htmlBody = $@"
<p>Dear Subcontractor,</p>

<p>A new document has been added to the project and is now relevant to the RFQ previously shared with you.</p>

<p>Please review the newly added document using the same RFQ link shared earlier and consider it while preparing or updating your quotation.</p>

<p>
<strong>Project:</strong> {WebUtility.HtmlEncode(projectNumber)} - {WebUtility.HtmlEncode(projectName)}<br/>
<strong>RFQ Reference:</strong> {WebUtility.HtmlEncode(rfqNumber)}<br/>
<strong>Work Item(s):</strong> {WebUtility.HtmlEncode(workItemsText)}<br/>
<strong>New Document:</strong> {WebUtility.HtmlEncode(docName)}
</p>

<p>Kindly review the document and proceed accordingly.</p>

<p>Best regards<br/>QMS Team</p>";

                    try
                    {
                        // STEP 8: Send email
                        await _email.SendSimpleEmailAsync(email, subject, htmlBody);

                        // STEP 9: Log notification (CRITICAL FOR STOPPING SECOND RFQ DUPES)
                        await _repo.LogNotificationAsync(
                            rfqId,
                            projectDocumentId,
                            subId
                        );

                        _logger.LogInformation(
                            "NOTIFY: Sent successfully to {Email} for RFQ {RfqId}",
                            email,
                            rfqId
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "NOTIFY FAILED for {Email} RFQ {RfqId}",
                            email,
                            rfqId
                        );
                    }
                }
            }
        }

        public async Task NotifyForEditRfqDocs(
            Guid projectId,
            Guid rfqId,
            IEnumerable<ProjectDocumentDto> linkedDocs,
            string userEmail,
            string? language = "en")

        {
            var (projectName, projectNumber, rfqNumber, workItemsText) =
                await GetRfqDetailsAsync(rfqId);

            _logger.LogInformation(
                "NotifyForEditRfqDocs: projectId={ProjectId}, rfqId={RfqId}, docsCount={DocsCount}, user={User}",
                projectId, rfqId, linkedDocs.Count(), userEmail);

            foreach (var doc in linkedDocs)
            {
                var subs = (await _repo.GetEligibleSubcontractorsForRfqAsync(rfqId)).ToList();

                foreach (var sub in subs)
                {
                    var alreadyNotified = await _repo.WasNotificationSentAsync(
                        rfqId,
                        doc.ProjectDocumentID,
                        sub.SubcontractorID
                    );

                    if (alreadyNotified)
                    {
                        _logger.LogInformation(
                            "NOTIFY: SKIP sub {Sub}, already notified for this RFQ/doc",
                            sub.SubcontractorID);
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
<strong>New Document(s) Added:</strong> {WebUtility.HtmlEncode(doc.FileName)}  <!-- ✅ FIX -->
</p>
<p>Best regards<br/>QMS Team</p>";

                    try
                    {
                        await _email.SendSimpleEmailAsync(sub.Email!, subject, htmlBody);
                        await _repo.LogNotificationAsync(
                            rfqId,
                            doc.ProjectDocumentID,
                            sub.SubcontractorID
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to send document notification. RFQ={RfqId}, Doc={DocId}, Sub={SubId}",
                            rfqId, doc.ProjectDocumentID, sub.SubcontractorID);
                    }
                }
            }
        }
    }
}