using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

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
            return email ?? _configuration["SmtpSettings:FromEmail"];
        }

        // 🔹 Get logged-in user's name safely
        private string GetSenderName()
        {
            return
                _httpContextAccessor.HttpContext?.User?
                    .FindFirst(ClaimTypes.Name)?.Value
                ?? _configuration["SmtpSettings:DisplayName"];
        }

        // ================= RFQ EMAIL =================
        public async Task<List<EmailRequest>> SendRfqEmailAsync(EmailRequest request)
        {
            var sentEmails = new List<EmailRequest>();

            if (request?.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                throw new ArgumentException("Invalid request.");

            var smtp = _configuration.GetSection("SmtpSettings");

            using var client = new SmtpClient(smtp["Host"], int.Parse(smtp["Port"]))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    smtp["Username"],
                    smtp["Password"]
                )
            };

            string fromEmail = GetSenderEmail();
            string displayName = GetSenderName();
            string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');

            var rfq = await _connection.QuerySingleAsync<dynamic>(
                @"SELECT RfqID, RfqNumber, ProjectID FROM Rfq WHERE RfqID = @id",
                new { id = request.RfqID });

            string projectName = await _connection.QuerySingleAsync<string>(
                @"SELECT Name FROM Projects WHERE ProjectID = @id",
                new { id = rfq.ProjectID });

            var projectManager = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT ISNULL(pm.ProjectManagerName, 'Project Manager') AS Name
                  FROM Projects p
                  LEFT JOIN ProjectManagers pm ON pm.ProjectManagerID = p.ProjectManagerID
                  WHERE p.ProjectID = @id",
                new { id = rfq.ProjectID });

            string projectManagerName = projectManager?.Name ?? "Project Manager";

            var workItems = (await _connection.QueryAsync<dynamic>(
                @"SELECT WorkItemID, Name FROM WorkItems WHERE WorkItemID IN @ids",
                new { ids = request.WorkItems })).ToList();

            var subcontractors = (await _connection.QueryAsync<dynamic>(
                @"SELECT SubcontractorID, EmailID, ISNULL(Name,'Subcontractor') Name
                  FROM Subcontractors
                  WHERE SubcontractorID IN @ids
                  AND EmailID IS NOT NULL",
                new { ids = request.SubcontractorIDs })).ToList();

            foreach (var sub in subcontractors)
            {
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

<p>{request.Body}</p>

<ul>
<li><strong>Project:</strong> {projectName}</li>
<li><strong>RFQ No:</strong> {rfq.RfqNumber}</li>
<li><strong>Due Date:</strong> {dueDate:dd-MMM-yyyy}</li>
</ul>

<p><strong>Work Items:</strong></p>
<ul>{workItemListHtml}</ul>

<p>
<a href='{summaryUrl}'
style='padding:12px 20px;background:#f97316;color:#fff;text-decoration:none;border-radius:5px;' >
View Project Summary
</a>
</p>

<p>
Regards,<br/>
<strong>{projectManagerName}</strong><br/>
(Project Manager)<br/>
Unibouw Team
</p>";

                var mail = new MailMessage
                {
                    From = new MailAddress(fromEmail, displayName),
                    Subject = $"RFQ – {projectName}",
                    Body = body,
                    IsBodyHtml = true
                };

                mail.To.Add(sub.EmailID);

                await client.SendMailAsync(mail);

                sentEmails.Add(new EmailRequest
                {
                    RfqID = rfq.RfqID,
                    SubcontractorIDs = new List<Guid> { sub.SubcontractorID },
                    ToEmail = sub.EmailID,
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

            var smtp = _configuration.GetSection("SmtpSettings");

            using var client = new SmtpClient(smtp["Host"], int.Parse(smtp["Port"]))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    smtp["Username"],
                    smtp["Password"]
                )
            };

            // 🔑 SAME AS RFQ
            string fromEmail = smtp["Username"];        // SMTP mailbox
            string fromName = GetSenderName();         // Logged-in user
            string replyTo = GetSenderEmail();        // Logged-in user's email

            var projectManager = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT ISNULL(pm.ProjectManagerName, 'Project Manager') AS Name
          FROM Rfq r
          INNER JOIN Projects p ON p.ProjectID = r.ProjectID
          LEFT JOIN ProjectManagers pm ON pm.ProjectManagerID = p.ProjectManagerID
          WHERE r.RfqID = @rfqId",
                new { rfqId });

            string pmName = projectManager?.Name ?? "Project Manager";

            string htmlBody = $@"
<p>Dear {WebUtility.HtmlEncode(subcontractorName)},</p>
<p>{emailBody}</p>
<p>
Regards,<br/>
<strong>{pmName}</strong><br/>
(Project Manager)<br/>
Unibouw Team
</p>";

            var mail = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "Reminder: Upload Your Quote - Unibouw",
                Body = htmlBody,
                IsBodyHtml = true
            };

            mail.To.Add(recipientEmail);

            // ⭐ THIS IS THE KEY
            mail.ReplyToList.Add(new MailAddress(replyTo, fromName));

            await client.SendMailAsync(mail);
            return true;
        }
        // ================= GENERIC EMAIL =================
        public async Task<bool> SendMailAsync(
            string toEmail,
            string subject,
            string body,
            string name,
            List<string>? attachmentFilePaths = null)
        {
            var smtp = _configuration.GetSection("SmtpSettings");

            using var client = new SmtpClient(smtp["Host"], int.Parse(smtp["Port"]))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    smtp["Username"],
                    smtp["Password"]
                )
            };

            string htmlBody = $@"
<p>Dear {WebUtility.HtmlEncode(name)},</p>
<p>{body}</p>
<p>Thank you,<br/>
<strong>Unibouw Team</strong></p>";

            using var mail = new MailMessage
            {
                From = new MailAddress(GetSenderEmail(), GetSenderName()),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            mail.To.Add(toEmail);

            if (attachmentFilePaths?.Any() == true)
            {
                foreach (var path in attachmentFilePaths.Where(File.Exists))
                {
                    mail.Attachments.Add(new Attachment(path));
                }
            }

            await client.SendMailAsync(mail);
            return true;
        }
    }
}