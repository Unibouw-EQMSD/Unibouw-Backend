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

            // Web App Base URL  
            string webBaseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');
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

                // Only ONE work item  (as you said)
                Guid workItemId = request.WorkItems.FirstOrDefault();

                //Fetch subcontractors from DB
                var query = @" SELECT SubcontractorID AS Id,EmailID AS Email,ISNULL(Name, 'Subcontractor') AS Name
                            FROM Subcontractors WHERE SubcontractorID IN @Ids
                            AND EmailID <> '' AND IsActive = 1 AND IsDeleted = 0";

                var subcontractors = (await _connection.QueryAsync<(Guid Id, string Email, string Name)>(
                                      query, new { Ids = request.SubcontractorIDs } )).ToList();

                if (!subcontractors.Any())
                    throw new Exception("No valid subcontractor emails found.");

                // SEND EMAILS
                foreach (var sub in subcontractors)
                {
                    // Build RFQ Email Link
                    string projectSummaryUrl = $"{webBaseUrl}/project-summary?rfqId={request.RfqID}&subId={sub.Id}&workItemId={workItemId}";

                    string htmlBody = $@" <p>Dear {WebUtility.HtmlEncode(sub.Name)},</p>
                                          <p>You have been invited to participate in a Quote Request (RFQ).</p>
                                          <p>Please click the link below to view project details and respond:</p>
                                          <p> <a href='{projectSummaryUrl}'style='padding:12px 20px;background-color:#f97316;color:white;text-decoration:none;border-radius:6px;'>View Project Summary</a></p>
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
