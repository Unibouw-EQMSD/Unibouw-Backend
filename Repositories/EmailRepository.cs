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
        public async Task<List<EmailRequest>> SendRfqEmailAsync(EmailRequest request)
        {
            var sentEmails = new List<EmailRequest>();
            if (request?.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                return sentEmails;

            string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');

            var rfq = await _connection.QuerySingleAsync<dynamic>(
                @"SELECT RfqID, RfqNumber, ProjectID FROM Rfq WHERE RfqID = @id",
                new { id = request.RfqID });

            string projectName = await _connection.QuerySingleAsync<string>(
                @"SELECT Name FROM Projects WHERE ProjectID = @id",
                new { id = rfq.ProjectID });

            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
          FROM Projects prj
          LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
          WHERE prj.ProjectID = @Id", new { id = rfq.ProjectID });
            string personName = personDetails?.PersonName ?? "Project Assignee";

            var workItems = (await _connection.QueryAsync<dynamic>(
                @"SELECT WorkItemID, Name FROM WorkItems WHERE WorkItemID IN @ids",
                new { ids = request.WorkItems })).ToList();

            var subcontractors = (await _connection.QueryAsync<dynamic>(
                @"SELECT SubcontractorID, Email, ISNULL(Name,'Subcontractor') Name
          FROM Subcontractors
          WHERE SubcontractorID IN @ids
          AND Email IS NOT NULL",
                new { ids = request.SubcontractorIDs })).ToList();

            foreach (var sub in subcontractors)
            {
                // Already emailed check
                bool alreadyEmailed = await _connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1)
              FROM RfqSubcontractorMapping
              WHERE RfqID = @RfqID
                AND SubcontractorID = @SubId
                AND CreatedOn IS NOT NULL",
                    new { RfqID = rfq.RfqID, SubId = sub.SubcontractorID }
                ) > 0;
                if (alreadyEmailed) continue;

                DateTime dueDate = await _connection.QuerySingleAsync<DateTime>(
                    @"SELECT DueDate FROM RfqSubcontractorMapping
              WHERE RfqID = @RfqID AND SubcontractorID = @SubId",
                    new { RfqID = rfq.RfqID, SubId = sub.SubcontractorID });

                string workItemListHtml = string.Join("",
                    workItems.Select(w => $"<li><strong>{w.Name}</strong></li>"));
                string workItemIdsCsv = string.Join(",", workItems.Select(w => w.WorkItemID));
                string summaryUrl =
                    $"{baseUrl}/project-summary" +
                    $"?rfqId={rfq.RfqID}" +
                    $"&subId={sub.SubcontractorID}" +
                    $"&workItemIds={workItemIdsCsv}";
                string body = $@"
<p>Dear {sub.Name},</p>
<p>{(string.IsNullOrWhiteSpace(request.Body) ? "You are invited to submit a quotation for the following details:" : request.Body)}</p>
<ul>
<li><strong>Project:</strong> {projectName}</li>
<li><strong>RFQ No:</strong> {rfq.RfqNumber}</li>
<li><strong>Due Date:</strong> {dueDate:dd-MMM-yyyy}</li>
</ul>
<p><strong>Work Items:</strong></p>
<ul>{workItemListHtml}</ul>
<br/>
<p>
<a href='{summaryUrl}'
style='padding:12px 20px;background:#f97316;color:#fff;text-decoration:none;border-radius:5px;'>
View Project Summary
</a>
</p>
<br/>
<p>
Regards,<br/>
<strong>{personName}</strong><br/>
Project - Unibouw
</p>";

                // Send email via Graph
                await SendGraphEmailAsync(sub.Email, $"RFQ – {projectName}", body);

                // Mark as emailed (ONCE)
                await _connection.ExecuteAsync(
                    @"UPDATE RfqSubcontractorMapping
              SET CreatedOn = GETDATE()
              WHERE RfqID = @RfqID AND SubcontractorID = @SubId",
                    new { RfqID = rfq.RfqID, SubId = sub.SubcontractorID }
                );

                sentEmails.Add(new EmailRequest
                {
                    RfqID = rfq.RfqID,
                    SubcontractorIDs = new List<Guid> { sub.SubcontractorID },
                    ToEmail = sub.Email,
                    Subject = request.Subject,
                    WorkItems = request.WorkItems,
                    Body = body
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

            string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');

            // 🔹 Get reminder type
            var reminderType = await _connection.QuerySingleOrDefaultAsync<string>(
                @"SELECT ReminderType 
          FROM dbo.RfqReminder
          WHERE RfqID = @RfqID 
            AND SubcontractorID = @SubcontractorID",
                new { RfqID = rfqId, SubcontractorID = subcontractorId });

            IEnumerable<dynamic> rfqs;

            if (reminderType == "custom")
            {
                // 🔹 CUSTOM → only that RFQ
                rfqs = await _connection.QueryAsync<dynamic>(
                    @"SELECT 
                r.RfqID,
                r.RfqNumber,
                r.ProjectID,
                p.Name AS ProjectName,
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
                // 🔹 GLOBAL → all RFQs for subcontractor (not uploaded)
                rfqs = await _connection.QueryAsync<dynamic>(
@"SELECT 
    r.RfqID,
    r.RfqNumber,
    r.ProjectID,
    p.Name AS ProjectName,
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

            // 🔹 Get project assignee name (from first RFQ)
            var firstRfq = rfqs.First();

            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT p.Name AS PersonName
          FROM dbo.Rfq r
          INNER JOIN dbo.Projects prj ON r.ProjectID = prj.ProjectID
          LEFT JOIN dbo.PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN dbo.Persons p ON ppm.PersonID = p.PersonID
          WHERE r.RfqID = @RfqID",
                new { RfqID = firstRfq.RfqID });

            string personName = personDetails?.PersonName ?? "Project Assignee";

            // 🔹 Build RFQ list HTML
            string rfqListHtml = string.Join("",
                rfqs.Select(r =>
                {
                    string summaryUrl =
                        $"{baseUrl}/project-summary" +
                        $"?rfqId={r.RfqID}" +
                        $"&subId={subcontractorId}";

                    return $@"
<li style='margin-bottom:28px;'>
    <div style='margin-bottom:10px;'>
        <strong>{r.ProjectName}</strong>
    </div>

    <div style='margin-bottom:8px;'>
        RFQ No: {r.RfqNumber}
    </div>

    <div style='margin-bottom:14px;'>
        Due Date: {((DateTime)r.DueDate):dd-MMM-yyyy}
    </div>

    <div style='margin-top:6px;'>
        <a href='{summaryUrl}'
           style='display:inline-block;
                  padding:10px 16px;
                  background-color:#f97316;
                  color:#ffffff;
                  text-decoration:none;
                  border-radius:4px;
                  font-size:14px;'>
            View Project Summary
        </a>
    </div>
</li>";

                }));

            string htmlBody = $@"
<p style='margin-bottom:18px;'>Dear {WebUtility.HtmlEncode(subcontractorName)},</p>

<p style='margin-bottom:18px;'>{emailBody}</p>

<p style='margin-bottom:12px;'><strong>Your Pending RFQs:</strong></p>

<ul style='padding-left:18px; margin-bottom:20px;'>
{rfqListHtml}
</ul>

<p style='margin-top:30px;'>
Regards,<br/><br/>
<strong>{personName}</strong><br/>
Project - Unibouw
</p>";


            await SendGraphEmailAsync(
                recipientEmail,
                "Reminder: Upload Your Quote - Unibouw",
                htmlBody);

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

    }
}