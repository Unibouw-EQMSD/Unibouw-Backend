using System.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class EmailRepository : IEmailRepository
    {
        private readonly IConfiguration _config;
        private readonly string _connectionString;

        public EmailRepository(IConfiguration config)
        {
            _config = config;
            _connectionString = _config.GetConnectionString("UnibouwDbConnection");
        }

        public async Task<bool> SendRfqEmailAsync(EmailRequest request)
        {
            if (request == null || request.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                throw new ArgumentException("Invalid request: subcontractors are required.");

            var smtpSettings = _config.GetSection("SmtpSettings");
            string host = smtpSettings["Host"];
            int port = int.Parse(smtpSettings["Port"]);
            bool enableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
            string username = smtpSettings["Username"];
            string password = smtpSettings["Password"];
            string fromEmail = smtpSettings["FromEmail"];
            string displayName = smtpSettings["DisplayName"];

            // Base URL for Angular web app
            string webBaseUrl = _config["WebSettings:BaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(webBaseUrl))
                throw new Exception("Missing WebSettings:BaseUrl in configuration.");

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                // ONLY ONE WORKITEM (as you said)
                Guid workItemId = request.WorkItems.FirstOrDefault();

                // Fetch subcontractors from DB
                List<(Guid Id, string Email, string Name)> subcontractors = new();

                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var ids = string.Join(",", request.SubcontractorIDs.Select(id => $"'{id}'"));
                    var query = $@"
                SELECT SubcontractorID, EmailID, ISNULL(Name, 'Subcontractor') AS Name 
                FROM Subcontractors 
                WHERE SubcontractorID IN ({ids}) 
                  AND ISNULL(EmailID, '') <> '' 
                  AND IsActive = 1 
                  AND IsDeleted = 0";

                    using (var cmd = new SqlCommand(query, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            subcontractors.Add((
                                reader.GetGuid(0),
                                reader.GetString(1),
                                reader.GetString(2)
                            ));
                        }
                    }
                }

                if (!subcontractors.Any())
                    throw new Exception("No valid subcontractor emails found.");

                foreach (var sub in subcontractors)
                {
                    // Build RFQ Email Link
                    string projectSummaryUrl =
    $"{webBaseUrl}/project-summary?rfqId={request.RfqID}&subId={sub.Id}&workItemId={workItemId}";

                    string htmlBody = $@"
<p>Dear {WebUtility.HtmlEncode(sub.Name)},</p>
<p>You have been invited to participate in a Quote Request (RFQ).</p>

<p>Please click the link below to view project details and respond:</p>

<p>
    <a href='{projectSummaryUrl}'
       style='padding:12px 20px;background-color:#f97316;color:white;
              text-decoration:none;border-radius:6px;'>
       View Project Summary
    </a>
</p>

<p>Thank you,<br/>Unibouw Team</p>";

                    // Send the email using YOUR SMTP client
                    var mail = new MailMessage()
                    {
                        From = new MailAddress(fromEmail, displayName),
                        Subject = request.Subject ?? "RFQ Invitation - Unibouw",
                        Body = htmlBody,
                        IsBodyHtml = true
                    };

                    mail.To.Add(sub.Email);

                    await client.SendMailAsync(mail);
                }

                return true;
            }
            catch
            {
                throw;
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


    }
}
