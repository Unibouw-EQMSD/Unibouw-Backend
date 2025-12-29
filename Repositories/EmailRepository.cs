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

        public async Task<List<EmailRequest>> SendRfqEmailAsync(EmailRequest request)
        {
            var sentEmails = new List<EmailRequest>();

            try
            {
                if (request == null || request.SubcontractorIDs == null || !request.SubcontractorIDs.Any())
                    throw new ArgumentException("Invalid request.");

                // ---------- SMTP ----------
                var smtp = _configuration.GetSection("SmtpSettings");
                var client = new SmtpClient(smtp["Host"], int.Parse(smtp["Port"]))
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(smtp["Username"], smtp["Password"])
                };

                string fromEmail = smtp["FromEmail"];
                string displayName = smtp["DisplayName"];
                string baseUrl = _configuration["WebSettings:BaseUrl"]?.TrimEnd('/');

                // ---------- RFQ ----------
                var rfq = await _connection.QuerySingleAsync<dynamic>(
                    @"SELECT RfqID, RfqNumber, DueDate, ProjectID
                        FROM Rfq WHERE RfqID = @id",
                    new { id = request.RfqID });

                // ---------- Project ----------
                string projectName = await _connection.QuerySingleAsync<string>(
                    @"SELECT Name FROM Projects WHERE ProjectID = @id",
                    new { id = rfq.ProjectID });

                // ---------- Work Items ----------
                var workItems = (await _connection.QueryAsync<dynamic>(
                    @"SELECT WorkItemID, Name
                        FROM WorkItems
                        WHERE WorkItemID IN @ids",
                    new { ids = request.WorkItems }
                )).ToList();

                // ---------- Subcontractors ----------
                var subcontractors = (await _connection.QueryAsync<dynamic>(
                    @"SELECT SubcontractorID, EmailID, ISNULL(Name,'Subcontractor') Name
                          FROM Subcontractors
                          WHERE SubcontractorID IN @ids
                          AND EmailID IS NOT NULL",
                    new { ids = request.SubcontractorIDs }
                )).ToList();

                foreach (var sub in subcontractors)
                {
                    // ---------- RFQ–Subcontractor mapping (ONCE) ----------
                    await _connection.ExecuteAsync(@"
                            IF NOT EXISTS (
                                SELECT 1 FROM RfqSubcontractorMapping
                                WHERE RfqID=@RfqID AND SubcontractorID=@SubId
                            )
                            INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
                            VALUES (@RfqID, @SubId)",
                        new { RfqID = rfq.RfqID, SubId = sub.SubcontractorID });

                    // ---------- INSERT RESPONSE PER WORK ITEM ----------
                    foreach (var wi in workItems)
                    {
                        await _connection.ExecuteAsync(@"
                                IF NOT EXISTS (
                                    SELECT 1 FROM RfqSubcontractorResponse
                                    WHERE RfqID=@RfqID 
                                      AND SubcontractorID=@SubId
                                      AND WorkItemID=@WorkItemID
                                )
                                INSERT INTO RfqSubcontractorResponse
                                (
                                    RfqSubcontractorResponseID,
                                    RfqID,
                                    SubcontractorID,
                                    WorkItemID,
                                    CreatedOn,
                                    SubmissionCount
                                )
                                VALUES
                                (
                                    NEWID(),
                                    @RfqID,
                                    @SubId,
                                    @WorkItemID,
                                    GETDATE(),
                                    0
                                )",
                            new
                            {
                                RfqID = rfq.RfqID,
                                SubId = sub.SubcontractorID,
                                WorkItemID = wi.WorkItemID
                            });
                    }

                    // ---------- ✅ SINGLE EMAIL PER SUBCONTRACTOR ----------
                    string workItemListHtml = string.Join("",
                        workItems.Select(w =>
                            $"<li><strong>{w.Name}</strong></li>")
                    );

                    string workItemIdsCsv = string.Join(",", workItems.Select(w => w.WorkItemID));

                    string summaryUrl =
                        $"{baseUrl}/project-summary" +
                        $"?rfqId={rfq.RfqID}" +
                        $"&subId={sub.SubcontractorID}" +
                        $"&workItemIds={workItemIdsCsv}";

                    string body = $@"
                                <p>Dear {sub.Name},</p>

                                <p>{request.Body}</p>

                                <p>You are invited to submit quotations for the following:</p>

                                <ul>
                                  <li><strong>Project:</strong> {projectName}</li>
                                  <li><strong>RFQ No:</strong> {rfq.RfqNumber}</li>
                                  <li><strong>Due Date:</strong> {rfq.DueDate:dd-MMM-yyyy}</li>
                                </ul>

                                <p><strong>Work Items:</strong></p>
                                <ul>
                                  {workItemListHtml}
                                </ul>

                                <p>
                                <a href='{summaryUrl}'
                                style='padding:12px 20px;background:#f97316;color:#fff;text-decoration:none;border-radius:5px;'>
                                View Project Summary
                                </a>
                                </p>
                                <br/>
                                <p>Regards,<br/>Unibouw Team</p>";

                    var mail = new MailMessage
                    {
                        From = new MailAddress(fromEmail, displayName),
                        Subject = $"RFQ – {projectName}",
                        Body = body,
                        IsBodyHtml = true
                    };

                    mail.To.Add(sub.EmailID);
                    await client.SendMailAsync(mail);

                    // ✅ CAPTURE EMAIL DETAILS
                    sentEmails.Add(new EmailRequest
                    {
                        RfqID = request.RfqID,
                        SubcontractorIDs = new List<Guid> { sub.SubcontractorID },
                        ToEmail = sub.EmailID,
                        Subject = request.Subject,
                        WorkItems = request.WorkItems,
                        Body = body
                    });
                }
                return sentEmails;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to send RFQ emails", ex);
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

        public async Task<bool> SendReminderEmailAsync(Guid subcontractorId, string email, string name, Guid rfqId, string emailBody)
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

        /* public async Task<bool> SendMailAsync(string toEmail,string subject,string body,string name)
         {
             try
             {
                 var smtpSettings = _configuration.GetSection("SmtpSettings");

                 ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                 using var client = new SmtpClient(
                     smtpSettings["Host"],
                     int.Parse(smtpSettings["Port"])
                 )
                 {
                     EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true"),
                     UseDefaultCredentials = false,
                     Credentials = new NetworkCredential(
                         smtpSettings["Username"],
                         smtpSettings["Password"]
                     ),
                     DeliveryMethod = SmtpDeliveryMethod.Network
                 };

                 // ✅ Build dynamic email message
                 string htmlBody = $@"
             <p>Dear {WebUtility.HtmlEncode(name)},</p>

             <p>{body}</p>

             <p>Thank you,<br/>
             <strong>Unibouw Team</strong></p>
         ";

                 var mail = new MailMessage
                 {
                     From = new MailAddress(
                         smtpSettings["FromEmail"],
                         smtpSettings["DisplayName"]
                     ),
                     Subject = subject,
                     Body = htmlBody,
                     IsBodyHtml = true
                 };

                 mail.To.Add(toEmail);

                 await client.SendMailAsync(mail);
                 return true;
             }
             catch (Exception ex)
             {
                 throw new Exception($"Failed to send email: {ex.Message}", ex);
             }
         }*/

        public async Task<bool> SendMailAsync(string toEmail,string subject,string body,string name,List<string>? attachmentFilePaths = null)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using var client = new SmtpClient(
                    smtpSettings["Host"],
                    int.Parse(smtpSettings["Port"])
                )
                {
                    EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true"),
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(
                        smtpSettings["Username"],
                        smtpSettings["Password"]
                    ),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                string htmlBody = $@"
                        <p>Dear {WebUtility.HtmlEncode(name)},</p>
                        <p>{body}</p>
                        <p>Thank you,<br/>
                        <strong>Unibouw Team</strong></p>
                    ";

                using var mail = new MailMessage
                {
                    From = new MailAddress(
                        smtpSettings["FromEmail"],
                        smtpSettings["DisplayName"]
                    ),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add(toEmail);

                // Add attachments
                if (attachmentFilePaths?.Any() == true)
                {
                    foreach (var filePath in attachmentFilePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                        {
                            mail.Attachments.Add(new Attachment(filePath));
                        }
                    }
                }

                await client.SendMailAsync(mail);
                return true;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw new Exception($"Failed to send email: {ex.Message}", ex);
            }
        }


    }
}
