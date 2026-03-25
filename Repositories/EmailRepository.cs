using Azure.Identity;
using Dapper;
using iText.Kernel.Pdf.Canvas.Parser.ClipperLib;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using Microsoft.Graph.Users.Item.SendMail;

namespace UnibouwAPI.Repositories
{
    public class EmailRepository : IEmail
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public EmailRepository(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;

            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection")
                ?? throw new InvalidOperationException("Connection string not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        // 🔹 Get logged-in user's email safely
        private string GetSenderEmail()
        {
            var email =
                _httpContextAccessor.HttpContext?.User?
                    .FindFirst(ClaimTypes.Email)?.Value
                ??
                _httpContextAccessor.HttpContext?.User?
                    .FindFirst("email")?.Value;

            // fallback to configured email
            return email ?? _configuration["GraphEmail:SenderUser"];
        }

        // 🔹 Get logged-in user's name safely
        private string GetSenderName()
        {
            return
                _httpContextAccessor.HttpContext?.User?
                    .FindFirst(ClaimTypes.Name)?.Value
                ?? _configuration["GraphEmail:DisplayName"] ?? "Unibouw Communications";
        }
        private async Task SendGraphEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var tenantId = _configuration["GraphEmail:TenantId"];
            var clientId = _configuration["GraphEmail:ClientId"];
            var clientSecret = _configuration["GraphEmail:ClientSecret"];

            var user = _httpContextAccessor.HttpContext?.User;

            var senderUser = _configuration["GraphEmail:SenderUser"];
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlBody
                },
                ToRecipients = new List<Recipient>
        {
            new Recipient { EmailAddress = new EmailAddress { Address = toEmail } }
        }
            };

            var sendMailBody = new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            };

            await graphClient.Users[senderUser].SendMail.PostAsync(sendMailBody);
        }

        private async Task SendGraphEmailAsyncWithAttachments(
      string toEmail,
      string subject,
      string htmlBody,
      List<Microsoft.Graph.Models.Attachment>? attachments = null)
        {
            var tenantId = _configuration["GraphEmail:TenantId"];
            var clientId = _configuration["GraphEmail:ClientId"];
            var clientSecret = _configuration["GraphEmail:ClientSecret"];
            var senderUser = _configuration["GraphEmail:SenderUser"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            var message = new Microsoft.Graph.Models.Message
            {
                Subject = subject,
                Body = new Microsoft.Graph.Models.ItemBody
                {
                    ContentType = Microsoft.Graph.Models.BodyType.Html,
                    Content = htmlBody
                },
                ToRecipients = new List<Microsoft.Graph.Models.Recipient>
        {
            new Microsoft.Graph.Models.Recipient
            {
                EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = toEmail }
            }
        },
                Attachments = attachments ?? new List<Microsoft.Graph.Models.Attachment>() // ✅ LIST
            };

            var sendMailBody = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            };

            await graphClient.Users[senderUser].SendMail.PostAsync(sendMailBody);
        }
        // ================= RFQ EMAIL =================


        string MaskId(Guid id)
        {
            return id.ToString().Substring(0, 8).ToUpper();
        }


        public async Task<List<EmailRequest>> SendRfqEmailAsync(EmailRequest request)
        {
            var sentEmails = new List<EmailRequest>();

            if (request?.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                return sentEmails;

            string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/') ?? "";

            // Language (default English). Does not change structure; only swaps labels/text.
            var lang = (request.Language ?? "en").ToLowerInvariant();

            string T(string key) => (lang, key) switch
            {
                ("nl", "Dear") => "Geachte",
                ("nl", "IntroFallback") => "U wordt uitgenodigd om een offerte in te dienen voor de volgende details:",
                ("nl", "Project") => "Project",
                ("nl", "RfqNo") => "RFQ nr.",
                ("nl", "DueDate") => "Vervaldatum",
                ("nl", "WorkItems") => "Werkonderdelen",
                ("nl", "ViewSummary") => "Projectoverzicht bekijken",
                ("nl", "Regards") => "Met vriendelijke groet",
                ("nl", "Signature") => "Project - Unibouw",

                // default: English
                (_, "Dear") => "Dear",
                (_, "IntroFallback") => "You are invited to submit a quotation for the following details:",
                (_, "Project") => "Project",
                (_, "RfqNo") => "RFQ No",
                (_, "DueDate") => "Due Date",
                (_, "WorkItems") => "Work Items",
                (_, "ViewSummary") => "View Project Summary",
                (_, "Regards") => "Regards",
                (_, "Signature") => "Project - Unibouw",
            };

            // Load RFQ + Project
            var rfq = await _connection.QuerySingleAsync<dynamic>(
                @"SELECT RfqID, RfqNumber, ProjectID
          FROM Rfq
          WHERE RfqID = @id",
                new { id = request.RfqID });

            Guid rfqId = (Guid)rfq.RfqID;
            Guid projectId = (Guid)rfq.ProjectID;

            var project = await _connection.QuerySingleAsync<dynamic>(
                @"SELECT Name, Number
          FROM Projects
          WHERE ProjectID = @id",
                new { id = projectId });

            string projectName = project.Name;
            string projectCode = project.Number;

            // Project manager / assignee name (for signature)
            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
          FROM Projects prj
          LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
          WHERE prj.ProjectID = @Id",
                new { Id = projectId });

            string personName = personDetails?.PersonName ?? "Project Assignee";

            // Work items
            var workItems = (await _connection.QueryAsync<dynamic>(
                @"SELECT WorkItemID, Name
          FROM WorkItems
          WHERE WorkItemID IN @ids",
                new { ids = request.WorkItems })).ToList();

            // Subcontractors
            var subcontractors = (await _connection.QueryAsync<dynamic>(
                @"SELECT SubcontractorID, Email, ISNULL(Name,'Subcontractor') Name
          FROM Subcontractors
          WHERE SubcontractorID IN @ids
            AND Email IS NOT NULL",
                new { ids = request.SubcontractorIDs })).ToList();

            // Sender mailbox used by Graph
            var senderUser = _configuration["GraphEmail:SenderUser"];

            // Build Graph client once
            var tenantId = _configuration["GraphEmail:TenantId"];
            var clientId = _configuration["GraphEmail:ClientId"];
            var clientSecret = _configuration["GraphEmail:ClientSecret"];
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            foreach (var sub in subcontractors)
            {
                Guid subcontractorId = (Guid)sub.SubcontractorID;
                string toEmail = (string)sub.Email;
                string subName = (string)sub.Name;

                // Skip if already emailed
                bool alreadyEmailed = await _connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
              FROM RfqSubcontractorMapping
              WHERE RfqID = @RfqID
                AND SubcontractorID = @SubId
                AND CreatedOn IS NOT NULL",
                    new { RfqID = rfqId, SubId = subcontractorId }) > 0;

                if (alreadyEmailed)
                    continue;

                // Due date
                DateTime dueDate = await _connection.QuerySingleAsync<DateTime>(
                    @"SELECT DueDate
              FROM RfqSubcontractorMapping
              WHERE RfqID = @RfqID
                AND SubcontractorID = @SubId",
                    new { RfqID = rfqId, SubId = subcontractorId });

                // HTML fragments
                string workItemListHtml = string.Join("",
                    workItems.Select(w => $"<li><strong>{WebUtility.HtmlEncode((string)w.Name)}</strong></li>"));

                string workItemIdsCsv = string.Join(",", workItems.Select(w => w.WorkItemID));

                string summaryUrl =
                    $"{baseUrl}/project-summary" +
                    $"?rfqId={rfqId}" +
                    $"&subId={subcontractorId}" +
                    $"&workItemIds={workItemIdsCsv}";

                // ✅ Token in BODY (real comment, not HTML-encoded)
                string token = $"<!-- UBW:project={projectId};sub={subcontractorId};rfq={rfqId} -->";

                // Keep structure: only swap translated labels and default intro text
                var intro = string.IsNullOrWhiteSpace(request.Body) ? T("IntroFallback") : request.Body;

                string body = token + $@"
<p>{T("Dear")} {WebUtility.HtmlEncode(subName)},</p>
<p>{intro}</p>
<ul>
  <li><strong>{T("Project")}:</strong> {WebUtility.HtmlEncode(projectCode)} - {WebUtility.HtmlEncode(projectName)}</li>
  <li><strong>{T("RfqNo")}:</strong> {WebUtility.HtmlEncode((string)rfq.RfqNumber)}</li>
  <li><strong>{T("DueDate")}:</strong> {dueDate:dd-MMM-yyyy}</li>
</ul>
<p><strong>{T("WorkItems")}:</strong></p>
<ul>{workItemListHtml}</ul>
<br/>
<p>
  <a href='{summaryUrl}'
     style='padding:12px 20px;background:#f97316;color:#fff;text-decoration:none;border-radius:5px;'>
     {T("ViewSummary")}
  </a>
</p>
<br/>
<p>
{T("Regards")},<br/>
<strong>{WebUtility.HtmlEncode(personName)}</strong><br/>
{T("Signature")}
</p>";

                // ✅ Subject token with FULL GUIDs so poller ParseToken(subject) works
                var subject =
                    $"RFQ – {projectCode} - {projectName} [UBW:P={projectId};S={subcontractorId};R={rfqId}]";

                // Send mail (Graph)
                var message = new Microsoft.Graph.Models.Message
                {
                    Subject = subject,
                    Body = new ItemBody
                    {
                        ContentType = BodyType.Html,
                        Content = body
                    },
                    ToRecipients = new List<Recipient>
            {
                new Recipient { EmailAddress = new EmailAddress { Address = toEmail } }
            }
                };

                var sendMailBody = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                };

                await graphClient.Users[senderUser].SendMail.PostAsync(sendMailBody);

                // Capture thread anchor (best-effort)
                try
                {
                    var sent = await graphClient.Users[senderUser].MailFolders["SentItems"].Messages.GetAsync(r =>
                    {
                        r.QueryParameters.Top = 10;
                        r.QueryParameters.Orderby = new[] { "sentDateTime desc" };
                        r.QueryParameters.Select = new[] { "id", "conversationId", "internetMessageId", "bodyPreview", "sentDateTime" };
                    });

                    var tokenNeedle = $"UBW:project={projectId};sub={subcontractorId}";
                    var sentMsg = sent?.Value?.FirstOrDefault(m =>
                        (m.BodyPreview ?? "").Contains(tokenNeedle, StringComparison.OrdinalIgnoreCase));

                    await _connection.ExecuteAsync(@"
INSERT INTO dbo.OutboundRfqEmailAnchor
(AnchorId, ProjectId, SubcontractorId, RfqId, PmMailbox, ConversationId, InternetMessageId, GraphMessageId, SentUtc, Subject, IsActive)
VALUES
(@AnchorId, @ProjectId, @SubId, @RfqId, @PmMailbox, @ConversationId, @InternetMessageId, @GraphMessageId, SYSUTCDATETIME(), @Subject, 1);",
                        new
                        {
                            AnchorId = Guid.NewGuid(),
                            ProjectId = projectId,
                            SubId = subcontractorId,
                            RfqId = rfqId,
                            PmMailbox = senderUser,
                            ConversationId = sentMsg?.ConversationId,
                            InternetMessageId = sentMsg?.InternetMessageId,
                            GraphMessageId = sentMsg?.Id,
                            Subject = subject
                        });
                }
                catch
                {
                    // Fallback: anchor without conversationId (audit-only)
                    await _connection.ExecuteAsync(@"
INSERT INTO dbo.OutboundRfqEmailAnchor
(AnchorId, ProjectId, SubcontractorId, RfqId, PmMailbox, ConversationId, InternetMessageId, GraphMessageId, SentUtc, Subject, IsActive)
VALUES
(@AnchorId, @ProjectId, @SubId, @RfqId, @PmMailbox, NULL, NULL, NULL, SYSUTCDATETIME(), @Subject, 1);",
                        new
                        {
                            AnchorId = Guid.NewGuid(),
                            ProjectId = projectId,
                            SubId = subcontractorId,
                            RfqId = rfqId,
                            PmMailbox = senderUser,
                            Subject = subject
                        });
                }

                // Mark as emailed (ONCE)
                await _connection.ExecuteAsync(
                    @"UPDATE RfqSubcontractorMapping
              SET CreatedOn = GETDATE()
              WHERE RfqID = @RfqID AND SubcontractorID = @SubId",
                    new { RfqID = rfqId, SubId = subcontractorId });

                sentEmails.Add(new EmailRequest
                {
                    RfqID = rfqId,
                    SubcontractorIDs = new List<Guid> { subcontractorId },
                    ToEmail = toEmail,
                    Subject = subject,
                    WorkItems = request.WorkItems,
                    Body = body,
                    Language = request.Language
                });
            }

            return sentEmails;
        }
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        // ================= REMINDER EMAIL =================
        public async Task<bool> SendReminderEmailAsync(
      Guid subcontractorId,
      string recipientEmail,
      string subcontractorName,
      Guid rfqId,
      string emailBody)
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
                throw new ArgumentException("Recipient email invalid");
            string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/') ?? "";

            // Get reminder type
            var reminderType = await _connection.QuerySingleOrDefaultAsync<string>(
                @"SELECT ReminderType 
          FROM dbo.RfqReminder
          WHERE RfqID = @RfqID 
            AND SubcontractorID = @SubcontractorID",
                new { RfqID = rfqId, SubcontractorID = subcontractorId });

            // Get all pending RFQs for this subcontractor (custom: only that RFQ)
            IEnumerable<dynamic> rfqs;
            if (reminderType == "custom")
            {
                rfqs = await _connection.QueryAsync<dynamic>(
                    @"SELECT 
                r.RfqID,
                r.RfqNumber,
                r.ProjectID,
                p.Name AS ProjectName,
                p.Number AS ProjectNumber,
                rsm.DueDate
              FROM dbo.RfqSubcontractorMapping rsm
              INNER JOIN dbo.Rfq r ON r.RfqID = rsm.RfqID
              INNER JOIN dbo.Projects p ON p.ProjectID = r.ProjectID
              WHERE rsm.SubcontractorID = @SubId
                AND r.RfqID = @RfqID",
                    new { SubId = subcontractorId, RfqID = rfqId });
            }
            else
            {
                rfqs = await _connection.QueryAsync<dynamic>(
                    @"SELECT 
                r.RfqID,
                r.RfqNumber,
                r.ProjectID,
                p.Name AS ProjectName,
                p.Number AS ProjectNumber,
                rsm.DueDate
            FROM dbo.RfqSubcontractorMapping rsm
            INNER JOIN dbo.Rfq r ON r.RfqID = rsm.RfqID
            INNER JOIN dbo.Projects p ON p.ProjectID = r.ProjectID
            WHERE rsm.SubcontractorID = @SubId
              AND rsm.DueDate > SYSDATETIME()
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.RfqResponseDocuments d
                    WHERE d.RfqID = rsm.RfqID
                      AND d.SubcontractorID = rsm.SubcontractorID
                      AND d.IsDeleted = 0
                )
              AND NOT EXISTS (
                    SELECT 1
                    FROM dbo.RfqReminder rr
                    WHERE rr.RfqID = rsm.RfqID
                      AND rr.SubcontractorID = rsm.SubcontractorID
                      AND rr.ReminderType = 'custom'
                )",
                    new { SubId = subcontractorId });
            }

            if (!rfqs.Any())
                return false;

            // Send one email per RFQ, exactly like SendRfqEmailAsync
            foreach (var r in rfqs)
            {
                // Get project manager / assignee name (for signature)
                var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                    @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
              FROM Projects prj
              LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
              LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
              WHERE prj.ProjectID = @Id",
                    new { Id = r.ProjectID });

                string personName = personDetails?.PersonName ?? "Project Assignee";
                string projectName = r.ProjectName;
                string projectCode = r.ProjectNumber;
                Guid projectId = r.ProjectID;
                Guid rfqGuid = r.RfqID;
                string rfqNumber = r.RfqNumber;
                DateTime dueDate = r.DueDate;

                // Same subject line as SendRfqEmailAsync
                var subject = $"RFQ – {projectCode} - {projectName} [UBW:P={projectId};S={subcontractorId};R={rfqGuid}]";

                // Same tracking token as SendRfqEmailAsync
                string token = $"<span style='display:none'>UBW:project={projectId};sub={subcontractorId};rfq={rfqGuid}</span>";

                string workItemIdsCsv = ""; // (if you want to fetch related workitems, you can query them here)
                string summaryUrl = $"{baseUrl}/project-summary?rfqId={rfqGuid}&subId={subcontractorId}";

                string body = $@"
<p>Dear {WebUtility.HtmlEncode(subcontractorName)},</p>
<p>{emailBody}</p>
<ul>
  <li><strong>Project:</strong> {WebUtility.HtmlEncode(projectCode)} - {WebUtility.HtmlEncode(projectName)}</li>
  <li><strong>RFQ No:</strong> {WebUtility.HtmlEncode(rfqNumber)}</li>
  <li><strong>Due Date:</strong> {dueDate:dd-MMM-yyyy}</li>
</ul>
<p>
  <a href='{summaryUrl}'
     style='padding:12px 20px;background:#f97316;color:#fff;text-decoration:none;border-radius:5px;'>
     View Project Summary
  </a>
</p>
<br/>
<p>
Regards,<br/>
<strong>{WebUtility.HtmlEncode(personName)}</strong><br/>
Project - Unibouw
</p>";

                await SendGraphEmailAsync(
                    recipientEmail,
                    subject,
                    body
                );

                try
                {
                    var senderUser = _configuration["GraphEmail:SenderUser"];

                    // Insert the anchor (even if ConversationId is not available, store as NULL)
                    await _connection.ExecuteAsync(@"
        INSERT INTO dbo.OutboundRfqEmailAnchor
        (AnchorId, ProjectId, SubcontractorId, RfqId, PmMailbox, ConversationId, InternetMessageId, GraphMessageId, SentUtc, Subject, IsActive)
        VALUES (@AnchorId, @ProjectId, @SubId, @RfqId, @PmMailbox, NULL, NULL, NULL, SYSUTCDATETIME(), @Subject, 1);",
                        new
                        {
                            AnchorId = Guid.NewGuid(),
                            ProjectId = projectId,
                            SubId = subcontractorId,
                            RfqId = rfqGuid,
                            PmMailbox = senderUser,
                            Subject = subject
                        });
                }
                catch (Exception ex)
                {
                    // Optionally log or handle duplicate anchor insert (ignore if anchor already exists)
                }
            }

            return true;
        }
        public async Task<bool> SendMailAsync(
            string toEmail,
            string subject,
            string body,
            string name,
            Guid? projectId,
            List<string>? attachmentFilePaths = null)
        {
            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
          FROM Projects prj
          LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
          WHERE prj.ProjectID = @Id", new { id = projectId.Value });
            string personName = personDetails?.PersonName ?? "Project Assignee";

            string formattedBody = body
                .Replace("\r\n", "<br/>")
                .Replace("\n", "<br/>");

            string htmlBody = $@"
<p>Dear {WebUtility.HtmlEncode(name)},</p>
<p>{formattedBody}</p>
<p>Regards,<br/>
<strong>{personName}</strong><br/>
Project - Unibouw
</p>";

            // Attachment support (Graph API)
            List<Microsoft.Graph.Models.Attachment>? attachments = null;
            if (attachmentFilePaths?.Any() == true)
            {
                attachments = new List<Microsoft.Graph.Models.Attachment>();
                foreach (var path in attachmentFilePaths.Where(File.Exists))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    attachments.Add(new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = Path.GetFileName(path),
                        ContentBytes = bytes,
                        ContentType = "application/octet-stream"
                    });
                }
            }

            await SendGraphEmailAsyncWithAttachments(toEmail, subject, htmlBody, attachments);
            return true;
        }


        private GraphServiceClient CreateGraphClient()
        {
            var tenantId = _configuration["GraphEmail:TenantId"];
            var clientId = _configuration["GraphEmail:ClientId"];
            var clientSecret = _configuration["GraphEmail:ClientSecret"];
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential);
        }

        private async Task<(string? GraphId, string? ConversationId, string? InternetMessageId)> FindSentByTokenAsync(
            GraphServiceClient graphClient, string senderUser, string tokenNeedle)
        {
            var sent = await graphClient.Users[senderUser].MailFolders["SentItems"].Messages.GetAsync(r =>
            {
                r.QueryParameters.Top = 10;
                r.QueryParameters.Orderby = new[] { "sentDateTime desc" };
                r.QueryParameters.Select = new[] { "id", "conversationId", "internetMessageId", "bodyPreview" };
            });

            var msg = sent?.Value?.FirstOrDefault(m =>
                (m.BodyPreview ?? "").Contains(tokenNeedle, StringComparison.OrdinalIgnoreCase));

            return (msg?.Id, msg?.ConversationId, msg?.InternetMessageId);
        }

    }
}