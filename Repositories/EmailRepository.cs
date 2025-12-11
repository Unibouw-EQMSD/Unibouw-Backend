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

                // Required work item

                if (request.WorkItems == null || !request.WorkItems.Any())

                    throw new Exception("WorkItem ID is required.");

                var workItemId = request.WorkItems.FirstOrDefault();

                if (workItemId == Guid.Empty)

                    throw new Exception("WorkItem ID is invalid.");

                var workItemQuery = @"SELECT Name, Number

                              FROM WorkItems

                              WHERE WorkItemID = @WorkItemID AND IsDeleted = 0";

                var workItem = await _connection.QuerySingleOrDefaultAsync(workItemQuery, new { WorkItemID = workItemId });

                if (workItem == null)

                    throw new Exception($"WorkItem not found for ID: {workItemId}");

                // Compute link validity

                var now = DateTime.UtcNow;

                var dueDate = rfq.DueDate;

                bool isLinkActive = dueDate != null && now <= dueDate;

                // Fetch subcontractors

                var subQuery = @"SELECT SubcontractorID AS Id, EmailID AS Email, 

                         ISNULL(Name, 'Subcontractor') AS Name

                         FROM Subcontractors 

                         WHERE SubcontractorID IN @Ids

                         AND EmailID <> '' AND IsActive = 1 AND IsDeleted = 0";

                var subcontractors = (await _connection.QueryAsync<(Guid Id, string Email, string Name)>(

                    subQuery, new { Ids = request.SubcontractorIDs })).ToList();

                if (!subcontractors.Any())

                    throw new Exception("No valid subcontractor emails found.");

                // Insert mappings

                foreach (var sub in subcontractors)

                {

                    var mappingQuery = @"

            IF NOT EXISTS (SELECT 1 FROM RfqSubcontractorMapping 

                           WHERE RfqID = @RfqID AND SubcontractorID = @SubId)

            BEGIN

                INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)

                VALUES (@RfqID, @SubId)

            END";

                    await _connection.ExecuteAsync(mappingQuery, new { RfqID = rfq.RfqID, SubId = sub.Id });

                    var responseQuery = @"

            IF NOT EXISTS (SELECT 1 FROM RfqSubcontractorResponse

                           WHERE RfqID = @RfqID AND SubcontractorID = @SubId)

            BEGIN

                INSERT INTO RfqSubcontractorResponse (RfqID, SubcontractorID, CreatedOn, SubmissionCount)

                VALUES (@RfqID, @SubId, GETDATE(), 0)

            END";

                    await _connection.ExecuteAsync(responseQuery, new { RfqID = rfq.RfqID, SubId = sub.Id });

                }

                // Send email

                foreach (var sub in subcontractors)

                {

                    string projectSummaryUrl = isLinkActive

                        ? $"{webBaseUrl}/project-summary?rfqId={request.RfqID}&subId={sub.Id}&workItemId={workItemId}"

                        : null;

                    // EXACT editable content from preview modal

                    string userMessage = string.Empty;

                    if (!string.IsNullOrWhiteSpace(request.Body))

                    {

                        userMessage = WebUtility.HtmlEncode(request.Body)

                            .Replace("\r\n", "<br/>")

                            .Replace("\n", "<br/>");

                    }

                    // FINAL EMAIL BODY (correct format)

                    string emailBody = $@"
<p>Dear {WebUtility.HtmlEncode(sub.Name)},</p>
 
<p>{userMessage}</p>
 
<p>You are invited to submit a quotation for the following work item under the 
<strong>{WebUtility.HtmlEncode(projectName)}</strong> project.</p>
 
<h4><b>Project & RFQ Details</b></h4>
<ul>
<li><strong>Project Name:</strong> {WebUtility.HtmlEncode(projectName)}</li>
<li><strong>RFQ ID:</strong> {rfq.RfqNumber ?? rfq.RfqID}</li>
<li><strong>Work Item:</strong> {WebUtility.HtmlEncode(workItem.Name)}</li>
<li><strong>Due Date:</strong> ({rfq.DueDate:dd-MMM-yyyy})</li>
</ul>

";

                    if (!string.IsNullOrEmpty(projectSummaryUrl))

                    {

                        emailBody += $@"
<p><a href='{projectSummaryUrl}' 

       style='padding:12px 20px;background-color:#f97316;color:white;text-decoration:none;border-radius:6px;'>

       View Project Summary
</a></p>";

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
