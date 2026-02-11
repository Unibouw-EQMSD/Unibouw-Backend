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
        },
                Attachments = attachments
            };

            var sendMailBody = new SendMailPostRequestBody
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
        public async Task<bool> SendReminderEmailAsync(Guid subcontractorId, string recipientEmail, string subcontractorName, Guid rfqId, string emailBody)
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
                throw new ArgumentException("Recipient email invalid");

            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
          FROM Rfq r
          INNER JOIN Projects prj ON r.ProjectID = prj.ProjectID
          LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
          WHERE r.RfqID = @RfqID",
                new { RfqID = rfqId }
            );
            string personName = personDetails?.PersonName ?? "Project Assignee";

            string htmlBody = $@"
<p>Dear {WebUtility.HtmlEncode(subcontractorName)},</p>
<p>{emailBody}</p>
<p>Regards,<br/>
<strong>{personName}</strong><br/>
Project - Unibouw testing
</p>";

            await SendGraphEmailAsync(recipientEmail, "Reminder: Upload Your Quote - Unibouw", htmlBody);
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