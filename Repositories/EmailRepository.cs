using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;
using System.Net.Mail;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class EmailRepository : IEmail
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public EmailRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<bool> SendRfqEmailAsync(EmailRequest request)
        {
            try
            {
                if (request == null || request.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                    throw new ArgumentException("Invalid request: subcontractors are required.");

                var smtpSettings = _configuration.GetSection("SmtpSettings");
                string host = smtpSettings["Host"];
                int port = int.Parse(smtpSettings["Port"]);
                bool enableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"];
                string fromEmail = smtpSettings["FromEmail"];
                string displayName = smtpSettings["DisplayName"];

                string webBaseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');
                if (string.IsNullOrEmpty(webBaseUrl))
                    throw new Exception("Missing WebSettings:BaseUrl in configuration.");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                Guid workItemId = request.WorkItems.FirstOrDefault();

                var query = @" SELECT SubcontractorID AS Id, EmailID AS Email, 
                       ISNULL(Name, 'Subcontractor') AS Name
                       FROM Subcontractors 
                       WHERE SubcontractorID IN @Ids
                       AND EmailID <> '' AND IsActive = 1 AND IsDeleted = 0";

                var subcontractors = (await _connection.QueryAsync<(Guid Id, string Email, string Name)>(
                                        query, new { Ids = request.SubcontractorIDs })).ToList();

                if (!subcontractors.Any())
                    throw new Exception("No valid subcontractor emails found.");

                foreach (var sub in subcontractors)
                {
                    string projectSummaryUrl =
                        $"{webBaseUrl}/project-summary?rfqId={request.RfqID}&subId={sub.Id}&workItemId={workItemId}";

                    // 🔹 Use request.Body if provided, else fallback
                    string emailBody = request.Body;
                    if (!string.IsNullOrWhiteSpace(emailBody))
                    {
                        // Inject the project summary link dynamically if you want it included
                        emailBody += $@"
                <p><a href='{projectSummaryUrl}'
                  style='padding:12px 20px;background-color:#f97316;color:white;text-decoration:none;border-radius:6px;'>
                  View Project Summary</a></p>";
                    }
                    else
                    {
                        // Default body if none provided
                        emailBody = $@"
                <p>Dear {WebUtility.HtmlEncode(sub.Name)},</p>
                <p>You have been invited to participate in a Quote Request (RFQ).</p>
                <p>Please click the link below to view project details and respond:</p>
                <p><a href='{projectSummaryUrl}'
                      style='padding:12px 20px;background-color:#f97316;color:white;text-decoration:none;border-radius:6px;'>
                      View Project Summary</a></p>
                <p>Thank you,<br/>Unibouw Team</p>";
                    }
                    var subject = "RFQ Invitation - Unibouw";

                    // Remove newlines and trim
                    subject = subject.Replace("\r", " ").Replace("\n", " ").Trim();

                    var mail = new MailMessage()
                    {
                        From = new MailAddress(fromEmail, displayName),
                        Subject = subject,
                        Body = emailBody,   // This can contain multiline text
                        IsBodyHtml = true
                    };

                    mail.To.Add(sub.Email);

                    await client.SendMailAsync(mail);
                }

                return true;
            }
            catch (SmtpException smtpEx)
            {
                throw new Exception($"SMTP error occurred: {smtpEx.Message}", smtpEx);
            }
            catch (SqlException sqlEx)
            {
                throw new Exception($"Database error occurred: {sqlEx.Message}", sqlEx);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send RFQ emails: {ex.Message}", ex);
            }
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

        /*public async Task<bool> SendReminderEmailAsync(Guid subcontractorId, string email, string name, Guid rfqId)
        {
            try
            {
                //email = "ashwini.p@flatworldsolutions.com";
                //name = "Ashwini";

                var smtpSettings = _configuration.GetSection("SmtpSettings");
                string host = smtpSettings["Host"];
                int port = int.Parse(smtpSettings["Port"]);
                bool enableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"];
                string fromEmail = smtpSettings["FromEmail"];
                string displayName = smtpSettings["DisplayName"];

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                // Email Body
                string htmlBody = $@"
            <p>Dear {WebUtility.HtmlEncode(name)},</p>
            <p>This is a reminder to upload your quote before the due date. 
               Please ensure all required documents are submitted on time.</p>
            <p>Thank you.<br/>Unibouw Team</p>
        ";

                var mail = new MailMessage()
                {
                    From = new MailAddress(fromEmail, displayName),
                    Subject = "Reminder: Upload Your Quote - Unibouw",
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add(email);

                await client.SendMailAsync(mail);
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send reminder email: {ex.Message}", ex);
            }
        }
*/

        public async Task<bool> SendReminderEmailAsync(
    Guid subcontractorId,
    string email,
    string name,
    Guid rfqId,
    string emailBody)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                string host = smtpSettings["Host"];
                int port = int.Parse(smtpSettings["Port"]);
                bool enableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"];
                string fromEmail = smtpSettings["FromEmail"];
                string displayName = smtpSettings["DisplayName"];

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                // Build dynamic email message

                string htmlBody = $@"
            <p>Dear {WebUtility.HtmlEncode(name)},</p>

            <p>{emailBody}</p>

            <p>Thank you.<br/>Unibouw Team</p>
        ";

                var mail = new MailMessage()
                {
                    From = new MailAddress(fromEmail, displayName),
                    Subject = "Reminder: Upload Your Quote - Unibouw",
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add(email);

                await client.SendMailAsync(mail);
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send reminder email: {ex.Message}", ex);
            }
        }

    }
}
