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

                // SMTP settings
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

                // Fetch RFQ details
                var rfqQuery = @" SELECT RfqID, RfqNumber, DueDate, GlobalDueDate, ProjectID
                          FROM Rfq
                          WHERE RfqID = @RfqID AND IsDeleted = 0";

                var rfq = await _connection.QuerySingleOrDefaultAsync(rfqQuery, new { RfqID = request.RfqID });
                if (rfq == null)
                    throw new Exception("RFQ not found.");

                // Fetch Project name
                var projectQuery = @"SELECT Name
                             FROM Projects
                             WHERE ProjectID = @ProjectID AND IsDeleted = 0";

                string projectName = await _connection.QuerySingleOrDefaultAsync<string>(projectQuery, new { ProjectID = rfq.ProjectID });

                if (string.IsNullOrEmpty(projectName))
                    throw new Exception("Project not found for this RFQ.");

                // ✅ Fix for WorkItem not found
                if (request.WorkItems == null || !request.WorkItems.Any())
                {
                    throw new Exception("WorkItem ID is required.");
                }

                var workItemId = request.WorkItems.FirstOrDefault();

                // Check for null/empty GUID
                if (workItemId == Guid.Empty)
                {
                    throw new Exception("WorkItem ID is invalid.");
                }

                var workItemQuery = @"SELECT Name, Number
                              FROM WorkItems
                              WHERE WorkItemID = @WorkItemID AND IsDeleted = 0";

                var workItem = await _connection.QuerySingleOrDefaultAsync(workItemQuery, new { WorkItemID = workItemId });
                if (workItem == null)
                    throw new Exception($"WorkItem not found for ID: {workItemId}");

                // Compute Project Summary link validity
                var now = DateTime.UtcNow;
                var dueDate = rfq.DueDate;
                bool isLinkActive = dueDate != null && now <= dueDate;

                // Fetch subcontractors
                var subQuery = @"SELECT SubcontractorID AS Id, EmailID AS Email, ISNULL(Name, 'Subcontractor') AS Name
                         FROM Subcontractors 
                         WHERE SubcontractorID IN @Ids
                         AND EmailID <> '' AND IsActive = 1 AND IsDeleted = 0";

                var subcontractors = (await _connection.QueryAsync<(Guid Id, string Email, string Name)>(
                    subQuery, new { Ids = request.SubcontractorIDs })).ToList();

                if (!subcontractors.Any())
                    throw new Exception("No valid subcontractor emails found.");

                // Ensure mappings exist AND create initial response row
                foreach (var sub in subcontractors)
                {
                    var mappingQuery = @"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM RfqSubcontractorMapping 
                    WHERE RfqID = @RfqID AND SubcontractorID = @SubId
                )
                BEGIN
                    INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
                    VALUES (@RfqID, @SubId)
                END";
                    await _connection.ExecuteAsync(mappingQuery, new { RfqID = rfq.RfqID, SubId = sub.Id });

                    var responseQuery = @"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM RfqSubcontractorResponse
                    WHERE RfqID = @RfqID AND SubcontractorID = @SubId
                )
                BEGIN
                    INSERT INTO RfqSubcontractorResponse (RfqID, SubcontractorID, CreatedOn, SubmissionCount)
                    VALUES (@RfqID, @SubId, GETDATE(), 0)
                END";
                    await _connection.ExecuteAsync(responseQuery, new { RfqID = rfq.RfqID, SubId = sub.Id });
                }

                // Send emails
                foreach (var sub in subcontractors)
                {
                    string projectSummaryUrl = isLinkActive
                        ? $"{webBaseUrl}/project-summary?rfqId={request.RfqID}&subId={sub.Id}&workItemId={workItemId}"
                        : null;

                    string emailBody = $@" 
                <p>Dear {WebUtility.HtmlEncode(sub.Name)},</p>

                <p>You are invited to submit a quotation for the following work item under the <strong>{WebUtility.HtmlEncode(projectName)}</strong> project.</p>
                
                <h4><b>Project & RFQ Details</b></h4>
                <ul>
                    <li><strong>Project Name:</strong> {WebUtility.HtmlEncode(projectName)}</li>
                    <li><strong>RFQ ID:</strong> {rfq.RfqNumber ?? rfq.RfqID}</li>
                    <li><strong>Work Item:</strong> {WebUtility.HtmlEncode(workItem.Name)}</li>
                    <li><strong>Due Date:</strong> ({rfq.DueDate:dd-MMM-yyyy})</li>
                </ul>";

                    if (!string.IsNullOrEmpty(projectSummaryUrl))
                    {
                        emailBody += $@"
                    <p>
                        <br/>
                        <a href='{projectSummaryUrl}' 
                           style='padding:12px 20px;background-color:#f97316;color:white;text-decoration:none;border-radius:6px;'>
                           View Project Summary
                        </a>                     
                    </p>
                    <br/><br/>
                    <p><strong>Expiry Information</strong><br/>
                    This link is valid until the due date ({rfq.DueDate:dd-MMM-yyyy}).
                    After this time, it will automatically expire and you won't be able to submit your quote.
                    </p>";
                    }
                    else
                    {
                        emailBody +=
                            $@"<p><em>Please note that the <strong>Project Summary link has expired</strong>, and you will not be able to access the project summary through the previous link.</em></p>
                       <p>If you still require the project details or need assistance in order to submit your quotation, please contact us and we will be happy to help.</p>";
                    }

                    emailBody += "<p>Thank you,<br/>Unibouw Team</p>";

                    var mail = new MailMessage()
                    {
                        From = new MailAddress(fromEmail, displayName),
                        Subject = "RFQ Invitation - Unibouw",
                        Body = emailBody,
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
