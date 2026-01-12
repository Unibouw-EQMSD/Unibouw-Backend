using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RFQConversationMessageRepository : IRFQConversationMessage
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IEmail _emailRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RFQConversationMessageRepository(IConfiguration configuration, IEmail emailRepository, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            _emailRepository = emailRepository;
            _httpContextAccessor = httpContextAccessor;

        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<RFQConversationMessage> AddRFQConversationMessageAsync(RFQConversationMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1️⃣ Get ProjectManagerID using CreatedBy (email)
            var getPmSql = @"
        SELECT ProjectManagerID
        FROM ProjectManagers
        WHERE Email = @Email";

            var projectManagerId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                getPmSql,
                new { Email = message.CreatedBy }
            );

            if (projectManagerId == null)
                throw new Exception($"Project manager not found for email: {message.CreatedBy}");

            // 2️⃣ Set message fields
            message.ConversationMessageID = Guid.NewGuid();
            message.ProjectManagerID = projectManagerId.Value;
            message.CreatedOn = DateTime.UtcNow;
            message.MessageDateTime = DateTime.UtcNow;
            message.Status = "Active";

            // 3️⃣ Insert conversation message
            var insertSql = @"
        INSERT INTO RFQConversationMessage
        (
            ConversationMessageID,
            ProjectID,
            WorkItemID,
            SubcontractorID,
            ProjectManagerID,
            SenderType,
            MessageText,
            MessageDateTime,
            Status,
            CreatedBy,
            CreatedOn,
            Subject
        )
        VALUES
        (
            @ConversationMessageID,
            @ProjectID,
            @WorkItemID,
            @SubcontractorID,
            @ProjectManagerID,
            @SenderType,
            @MessageText,
            @MessageDateTime,
            @Status,
            @CreatedBy,
            @CreatedOn,
            @Subject
        )";

            var rows = await connection.ExecuteAsync(insertSql, message);

            if (rows <= 0)
                throw new Exception("Failed to insert RFQConversationMessage");

            return message;
        }

        public async Task<IEnumerable<RFQConversationMessage>> GetMessagesByProjectAndSubcontractorAsync(Guid projectId,Guid subcontractorId)
        {
            var sql = @"
        SELECT 
            ConversationMessageID,
            ProjectID,
            RfqID,
            WorkItemID,
            SubcontractorID,
            ProjectManagerID,
            SenderType,
            MessageText,
            MessageDateTime,
            Status,
            CreatedBy,
            CreatedOn
        FROM RFQConversationMessage
        WHERE ProjectID = @ProjectID
          AND SubcontractorID = @SubcontractorID
          AND Status = 'Active'
        ORDER BY MessageDateTime ASC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            return await connection.QueryAsync<RFQConversationMessage>(sql, new
            {
                ProjectID = projectId,
                SubcontractorID = subcontractorId
            });
        }

        public async Task<LogConversation> AddLogConversationAsync(LogConversation logConversation)
        {
            if (logConversation == null)
                throw new ArgumentNullException(nameof(logConversation));

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1️⃣ Get Subcontractor Email
            if (logConversation.SubcontractorID != Guid.Empty)
            {
                const string emailSql = @"
                    SELECT EmailID
                    FROM Subcontractors
                    WHERE SubcontractorID = @SubcontractorID";

                var email = await connection.QueryFirstOrDefaultAsync<string>(
                    emailSql,
                    new { logConversation.SubcontractorID }
                );

                // Assign email to CreatedBy (fallback if null)
                logConversation.CreatedBy = email ?? "system";
            }

            // Set system values
            logConversation.LogConversationID = Guid.NewGuid();
            logConversation.CreatedOn = DateTime.UtcNow;
            logConversation.RfqID = null;

            var sql = @"
        INSERT INTO LogConversation
        (
            LogConversationID,
            ProjectID,
            RfqID,
            SubcontractorID,
            ProjectManagerID,
            ConversationType,
            Subject,
            Message,
            MessageDateTime,
            CreatedBy,
            CreatedOn
        )
        VALUES
        (
            @LogConversationID,
            @ProjectID,
            @RfqID,
            @SubcontractorID,
            @ProjectManagerID,
            @ConversationType,
            @Subject,
            @Message,
            @MessageDateTime,
            @CreatedBy,
            @CreatedOn
        )";

            var rows = await connection.ExecuteAsync(sql, logConversation);

            if (rows <= 0)
                throw new Exception("Failed to insert LogConversation");

            return logConversation;
        }

        public async Task<IEnumerable<LogConversation>> GetLogConversationsByProjectIdAsync(Guid projectId)
        {
            var sql = @"
        SELECT
            LogConversationID,
            ProjectID,
            RfqID,
            SubcontractorID,
            ProjectManagerID,
            ConversationType,
            Subject,
            Message,
            MessageDateTime,
            CreatedBy,
            CreatedOn
        FROM LogConversation
        WHERE ProjectID = @ProjectID
        ORDER BY MessageDateTime ASC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            return await connection.QueryAsync<LogConversation>(
                sql,
                new { ProjectID = projectId }
            );
        }

        public async Task<List<ConversationMessageDto>> GetConversationAsync(Guid projectId, Guid rfqId, Guid subcontractorId)
        {
            /*var sql = @"
                SELECT *
                FROM
                (
                    SELECT
                        ConversationMessageID AS MessageID,
                        'PM' AS SenderType,
                        MessageText,
                        MessageDateTime,
                        NULL AS Subject,
                        'Mail' AS ConversationType
                    FROM RFQConversationMessage
                    WHERE ProjectID = @projectId
                      AND SubcontractorID = @subcontractorId

                    UNION ALL

                    SELECT
                        LogConversationID AS MessageID,
                        'Subcontractor' AS SenderType,
                        Message AS MessageText,
                        MessageDateTime,
                        Subject,
                        ConversationType
                    FROM LogConversation
                    WHERE ProjectID = @projectId
                      AND SubcontractorID = @subcontractorId
                ) AS CombinedMessages
                ORDER BY MessageDateTime ASC;
                ";
*/

            var sql = @"
                WITH CombinedMessages AS
                (
                    SELECT
                        ConversationMessageID AS MessageID,
                        ParentMessageID,
                        'PM' AS SenderType,
                        MessageText,
                        MessageDateTime,
                        Subject,
                        'Mail' AS ConversationType
                    FROM RFQConversationMessage
                    WHERE ProjectID = @projectId
                      AND SubcontractorID = @subcontractorId
 
                    UNION ALL
 
                    SELECT
                        LogConversationID AS MessageID,
                        CAST(NULL AS UNIQUEIDENTIFIER) AS ParentMessageID,
                        'Subcontractor' AS SenderType,
                        Message AS MessageText,
                        MessageDateTime,
                        Subject,
                        ConversationType
                    FROM LogConversation
                    WHERE ProjectID = @projectId
                      AND SubcontractorID = @subcontractorId
                )
                SELECT *
                FROM CombinedMessages
                ORDER BY MessageDateTime ASC;
                ";

            var result = await _connection.QueryAsync<ConversationMessageDto>(
                sql,
                new { projectId, subcontractorId }
            );

            return result.ToList();
        }

        public async Task<RFQConversationMessageAttachment> AddAttachmentAsync(RFQConversationMessageAttachment attachment)
        {
            attachment.AttachmentID = Guid.NewGuid();
            attachment.UploadedOn = DateTime.UtcNow;
            attachment.IsActive = true;

            const string sql = @"
        INSERT INTO dbo.RFQConversationMessageAttachment
        (
            AttachmentID,
            ConversationMessageID,
            FileName,
            FileExtension,
            FileSize,
            FilePath,
            UploadedBy,
            UploadedOn,
            IsActive
        )
        VALUES
        (
            @AttachmentID,
            @ConversationMessageID,
            @FileName,
            @FileExtension,
            @FileSize,
            @FilePath,
            @UploadedBy,
            @UploadedOn,
            @IsActive
        );";

            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, attachment);

            return attachment;
        }
        public async Task<RFQConversationMessage> ReplyToConversationAsync(
            Guid parentMessageId,
            string messageText,
            string subject,
            string pmEmail)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            bool transactionCommitted = false;

            Exception? emailFailure = null;

            try
            {
                RFQConversationMessage? parent = null;
                bool isLogConversation = false;

                parent = await connection.QuerySingleOrDefaultAsync<RFQConversationMessage>(
                    @"SELECT * FROM RFQConversationMessage WHERE ConversationMessageID = @Id",
                    new { Id = parentMessageId },
                    transaction
                );

                if (parent == null)
                {
                    isLogConversation = true;
                    parent = await connection.QuerySingleOrDefaultAsync<RFQConversationMessage>(
    @"SELECT 
        LogConversationID AS ConversationMessageID,
        NULL AS ParentMessageID,
        ProjectID,
        RfqID,
        NULL AS WorkItemID,
        SubcontractorID,
        ProjectManagerID,
        'Subcontractor' AS SenderType,
        Message AS MessageText,
        Subject,
        MessageDateTime,              -- ✅ FIXED
        CreatedBy
      FROM LogConversation
      WHERE LogConversationID = @Id",
    new { Id = parentMessageId },
    transaction
);
                }

                if (parent == null)
                    throw new Exception("Parent message not found");

                Guid pmId = parent.ProjectManagerID != Guid.Empty
                    ? parent.ProjectManagerID
                    : (await connection.QuerySingleOrDefaultAsync<Guid?>(
                        @"SELECT ProjectManagerID FROM ProjectManagers WHERE Email = @Email",
                        new { Email = pmEmail },
                        transaction
                    )) ?? throw new Exception($"Project Manager not found for email: {pmEmail}");

                var reply = new RFQConversationMessage
                {
                    ConversationMessageID = Guid.NewGuid(),
                    ParentMessageID = parent.ConversationMessageID,
                    ProjectID = parent.ProjectID,
                    RfqID = isLogConversation ? null : parent.RfqID,
                    WorkItemID = parent.WorkItemID,
                    SubcontractorID = parent.SubcontractorID,
                    ProjectManagerID = pmId,
                    SenderType = "PM",
                    Tag = "Outgoing - Reply",
                    Status = "Draft",
                    MessageText = messageText,
                    Subject = subject,
                    MessageDateTime = DateTime.Now,
                    CreatedBy = pmEmail,
                    CreatedOn = DateTime.Now
                };

                await connection.ExecuteAsync(
                    @"INSERT INTO RFQConversationMessage
              (ConversationMessageID, ParentMessageID, ProjectID, RfqID, WorkItemID,
               SubcontractorID, ProjectManagerID, SenderType, MessageText, MessageDateTime,
               Status, CreatedBy, CreatedOn, Subject, Tag)
              VALUES
              (@ConversationMessageID, @ParentMessageID, @ProjectID, @RfqID, @WorkItemID,
               @SubcontractorID, @ProjectManagerID, @SenderType, @MessageText, @MessageDateTime,
               @Status, @CreatedBy, @CreatedOn, @Subject, @Tag)",
                    reply,
                    transaction
                );

                // 🔥 EMAIL SEND (CAPTURE REAL ERROR)
                try
                {
                    await SendReplyEmailAsync(parent, reply);
                    reply.Status = "Sent";
                }
                catch (Exception ex)
                {
                    reply.Status = "Draft";
                    emailFailure = ex;
                }

                await connection.ExecuteAsync(
                    @"UPDATE RFQConversationMessage
              SET Status = @Status
              WHERE ConversationMessageID = @Id",
                    new { Status = reply.Status, Id = reply.ConversationMessageID },
                    transaction
                );

                transaction.Commit();
                transactionCommitted = true;

                // 🔴 THROW WITH FULL DETAILS
                if (reply.Status == "Draft")
                    throw new Exception(
                        "Email failed. Reply saved as draft. Reason: " +
                        (emailFailure?.InnerException?.Message ?? emailFailure?.Message)
                    );

                return reply;
            }
            catch
            {
                if (!transactionCommitted)
                    transaction.Rollback();
                throw;
            }
        }
        private async Task<bool> SendReplyEmailAsync(
      RFQConversationMessage parent,
      RFQConversationMessage reply,
      List<string>? attachmentPaths = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1️⃣ Fetch subcontractor details
            var sub = await connection.QuerySingleOrDefaultAsync<SubcontractorEmailDto>(
                @"SELECT EmailID, ISNULL(Name,'Subcontractor') AS Name
          FROM Subcontractors
          WHERE SubcontractorID = @Id",
                new { Id = parent.SubcontractorID }
            );

            if (sub == null || string.IsNullOrWhiteSpace(sub.EmailID))
                throw new Exception("Subcontractor email not found");

            // 2️⃣ Fetch Project Manager name directly from ProjectManagerID
            string projectManagerName = "Project Manager";
            if (parent.ProjectManagerID != Guid.Empty)
            {
                var pm = await connection.QuerySingleOrDefaultAsync<dynamic>(
                    @"SELECT ISNULL(ProjectManagerName,'Project Manager') AS Name
              FROM ProjectManagers
              WHERE ProjectManagerID = @id",
                    new { id = parent.ProjectManagerID }
                );

                projectManagerName = pm?.Name ?? "Project Manager";
            }

            // 3️⃣ Prepare the parent message text
            string parentMessageText = parent.MessageText ?? "";
            if (!parentMessageText.TrimStart().StartsWith($"Dear {sub.Name}", StringComparison.OrdinalIgnoreCase))
            {
                parentMessageText = $"Dear {sub.Name},<br/>{parentMessageText}";
            }

            // 4️⃣ Build email body
            string body = $@"
<p><strong>Your message:</strong><br/>
{parentMessageText}</p>

<p><strong>Sent on:</strong> {parent.MessageDateTime:dd-MMM-yyyy HH:mm}</p>

<hr/>

<p><strong>Unibouw Response:</strong><br/>
{reply.MessageText}</p>

<p><strong>Replied on:</strong> {reply.MessageDateTime:dd-MMM-yyyy HH:mm}</p>

<p>
Regards,<br/>
<strong>{projectManagerName}</strong><br/>
(Project Manager)<br/>
Unibouw Team
</p>";

            var smtp = _configuration.GetSection("SmtpSettings");

            using var client = new SmtpClient(smtp["Host"], int.Parse(smtp["Port"]))
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(
                    smtp["Username"],
                    smtp["Password"]
                )
            };

            // 🔑 Use logged-in user safely
            string fromEmail = smtp["Username"];           // Authenticated mailbox (must stay)
            string fromName = GetSenderName();            // Logged-in user's name
            string replyTo = GetSenderEmail();            // Logged-in user's email

            var mail = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = reply.Subject ?? "Unibouw – Reply",
                Body = body,
                IsBodyHtml = true
            };

            mail.To.Add(sub.EmailID);

            // ✅ Key change: logged-in user in Reply-To
            mail.ReplyToList.Add(new MailAddress(replyTo, fromName));

            // Attachments
            if (attachmentPaths?.Any() == true)
            {
                foreach (var path in attachmentPaths.Where(File.Exists))
                {
                    mail.Attachments.Add(new Attachment(path));
                }
            }

            await client.SendMailAsync(mail);
            return true;
        }

        // 🔹 Helper methods from your working RFQ/Reminder
        private string GetSenderEmail()
        {
            var email = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value
                        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value;
            return email ?? _configuration["SmtpSettings:FromEmail"];
        }

        private string GetSenderName()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value
                   ?? _configuration["SmtpSettings:DisplayName"];
        }
    }
}
