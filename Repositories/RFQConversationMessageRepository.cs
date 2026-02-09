using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Helpers;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using Azure.Identity;



namespace UnibouwAPI.Repositories
{
    public class RFQConversationMessageRepository : IRFQConversationMessage
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IEmail _emailRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

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
            /*var getPmSql = @"
        SELECT ProjectManagerID
        FROM ProjectManagers
        WHERE Email = @Email";

            var projectManagerId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                getPmSql,
                new { Email = message.CreatedBy }
            );
            if (projectManagerId == null)
                throw new Exception($"Project manager not found for email: {message.CreatedBy}");*/


            // 2️⃣ Set message fields
            message.ConversationMessageID = Guid.NewGuid();
            message.CreatedOn = amsterdamNow;
            message.MessageDateTime = amsterdamNow;
            message.Status = "Active";


            // 3️⃣ Insert conversation message
            var insertSql = @"
                INSERT INTO RFQConversationMessage
                (
                    ConversationMessageID,
                    ProjectID,
                    SubcontractorID,
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
                    @SubcontractorID,
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

        public async Task<IEnumerable<RFQConversationMessage>> GetMessagesByProjectAndSubcontractorAsync(Guid projectId, Guid subcontractorId)
        {
            var sql = @"SELECT * FROM RFQConversationMessage WHERE ProjectID = @ProjectID AND SubcontractorID = @SubcontractorID AND Status = 'Active' ORDER BY MessageDateTime ASC";
            using var connection = new SqlConnection(_connectionString);
            var messages = (await connection.QueryAsync<RFQConversationMessage>(sql, new { ProjectID = projectId, SubcontractorID = subcontractorId })).ToList();

            // For each message, load attachments
            var attachmentSql = @"SELECT * FROM RFQConversationMessageAttachment WHERE ConversationMessageID = @Id AND IsActive = 1";
            foreach (var msg in messages)
            {
                var attachments = await connection.QueryAsync<RFQConversationMessageAttachment>(attachmentSql, new { Id = msg.ConversationMessageID });
                msg.Attachments = attachments.ToList();
            }

            return messages;
        }

        public async Task<RfqLogConversation> AddLogConversationAsync(RfqLogConversation logConversation)
        {
            if (logConversation == null)
                throw new ArgumentNullException(nameof(logConversation));

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1️⃣ Get Subcontractor Email
            if (logConversation.SubcontractorID != Guid.Empty)
            {
                const string emailSql = @"
                    SELECT Email
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
            logConversation.CreatedOn = amsterdamNow;
            logConversation.MessageDateTime = logConversation.MessageDateTime == null
               ? amsterdamNow
               : logConversation.MessageDateTime;

            var sql = @"
        INSERT INTO RfqLogConversation
        (
            LogConversationID,
            ProjectID,
            SubcontractorID,
            ConversationType,
            MessageDateTime,
            Subject,
            Message,
            CreatedBy,
            CreatedOn
        )
        VALUES
        (
            @LogConversationID,
            @ProjectID,
            @SubcontractorID,
            @ConversationType,
            @MessageDateTime,
            @Subject,
            @Message,
            @CreatedBy,
            @CreatedOn
        )";

            var rows = await connection.ExecuteAsync(sql, logConversation);

            if (rows <= 0)
                throw new Exception("Failed to insert LogConversation");

            return logConversation;
        }

        public async Task<IEnumerable<RfqLogConversation>> GetLogConversationsByProjectIdAsync(Guid projectId)
        {
            var sql = @"
                SELECT
                    LogConversationID,
                    ProjectID,
                    SubcontractorID,
                    ConversationType,
                    Subject,
                    Message,
                    MessageDateTime,
                    CreatedBy,
                    CreatedOn
                FROM RfqLogConversation
                WHERE ProjectID = @ProjectID
                ORDER BY MessageDateTime ASC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            return await connection.QueryAsync<RfqLogConversation>(
                sql,
                new { ProjectID = projectId }
            );
        }

        public async Task<List<ConversationMessageDto>> GetConversationAsync(Guid projectId, Guid rfqId, Guid subcontractorId)
        {
            var sql = @"
                WITH CombinedMessages AS
                (
                    SELECT
                        ConversationMessageID AS MessageID,
                        SubcontractorMessageID,
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
                        CAST(NULL AS UNIQUEIDENTIFIER) AS SubcontractorMessageID,
                        'Subcontractor' AS SenderType,
                        Message AS MessageText,
                        MessageDateTime,
                        Subject,
                        ConversationType
                    FROM RfqLogConversation
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
            attachment.UploadedOn = amsterdamNow;
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
    Guid SubcontractorMessageID,
    string messageText,
    string subject,
    string pmEmail,
    List<string>? attachmentPaths = null
)
{
    using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();

    using var transaction = connection.BeginTransaction();

    // 1) Load parent from RFQConversationMessage, else from RfqLogConversation
    RFQConversationMessage? parent = await connection.QuerySingleOrDefaultAsync<RFQConversationMessage>(
        @"SELECT * FROM RFQConversationMessage WHERE ConversationMessageID = @Id",
        new { Id = SubcontractorMessageID },
        transaction
    );

    if (parent == null)
    {
        parent = await connection.QuerySingleOrDefaultAsync<RFQConversationMessage>(
            @"SELECT 
                LogConversationID AS ConversationMessageID,
                NULL AS SubcontractorMessageID,
                ProjectID,
                SubcontractorID,
                'Subcontractor' AS SenderType,
                Message AS MessageText,
                Subject,
                MessageDateTime,
                CreatedBy
              FROM RfqLogConversation
              WHERE LogConversationID = @Id",
            new { Id = SubcontractorMessageID },
            transaction
        );
    }

    if (parent == null)
        throw new Exception("Parent message not found");

    var amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow); // if you already have amsterdamNow logic, keep it

            // 2) Create reply (save quickly)
            var reply = new RFQConversationMessage
    {
        ConversationMessageID = Guid.NewGuid(),
        SubcontractorMessageID = parent.ConversationMessageID,
        ProjectID = parent.ProjectID,
        SubcontractorID = parent.SubcontractorID,
        SenderType = "PM",
        Tag = "Outgoing - Reply",
        Status = "Sending",               // ✅ new
        MessageText = messageText,
        Subject = subject,
        MessageDateTime = amsterdamNow,
        CreatedBy = pmEmail,
        CreatedOn = amsterdamNow
    };

    await connection.ExecuteAsync(
        @"INSERT INTO RFQConversationMessage
          (ConversationMessageID, SubcontractorMessageID, ProjectID,
           SubcontractorID, SenderType, MessageText, MessageDateTime,
           Status, CreatedBy, CreatedOn, Subject, Tag)
          VALUES
          (@ConversationMessageID, @SubcontractorMessageID, @ProjectID,
           @SubcontractorID, @SenderType, @MessageText, @MessageDateTime,
           @Status, @CreatedBy, @CreatedOn, @Subject, @Tag)",
        reply,
        transaction
    );

    // 3) Commit BEFORE email (don’t hold transaction open during SMTP)
    transaction.Commit();

    // 4) Fire-and-forget email send + status update
    _ = Task.Run(async () =>
    {
        try
        {
            await SendReplyEmailAsync(parent, reply, attachmentPaths);

            using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await c.ExecuteAsync(
                @"UPDATE RFQConversationMessage
                  SET Status = @Status
                  WHERE ConversationMessageID = @Id",
                new { Status = "Sent", Id = reply.ConversationMessageID }
            );
        }
        catch (Exception ex)
        {
            using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();

            // Optional: store failure reason if you have a column for it
            // If you don’t have EmailError column, remove EmailError update.
            await c.ExecuteAsync(
                @"UPDATE RFQConversationMessage
                  SET Status = @Status
                  WHERE ConversationMessageID = @Id",
                new { Status = "Draft", Id = reply.ConversationMessageID }
            );

            // log the real reason
        }
    });

    return reply; // ✅ returns immediately (fast)
}
        private static string ConvertToHtml(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text
                    .Replace("\r\n", "<br/>")
                    .Replace("\n", "<br/>");
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
                @"SELECT Email, ISNULL(Name,'Subcontractor') AS Name
          FROM Subcontractors
          WHERE SubcontractorID = @Id",
                new { Id = parent.SubcontractorID }
            );
            if (sub == null || string.IsNullOrWhiteSpace(sub.Email))
                throw new Exception("Subcontractor email not found");

            // 2️⃣ Fetch Project Manager name
            var personDetails = await _connection.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT prj.*, p.Name AS PersonName, ppm.RoleID
          FROM Projects prj
          LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
          LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
          WHERE prj.ProjectID = @Id", new { id = parent.ProjectID }
            );
            string personName = personDetails?.PersonName ?? "Project Assignee";

            // 3️⃣ Prepare message body
            string parentMessageText = ConvertToHtml(parent.MessageText ?? "");
            string replyMessageText = ConvertToHtml(reply.MessageText ?? "");
            if (!parentMessageText.TrimStart().StartsWith($"Dear {sub.Name}", StringComparison.OrdinalIgnoreCase))
            {
                parentMessageText = $"Dear {sub.Name},<br/>{parentMessageText}";
            }
            string body = $@"
<p><strong>Your message:</strong><br/>{parentMessageText}</p>
<p><strong>Sent on:</strong> {parent.MessageDateTime:dd-MMM-yyyy HH:mm}</p>
<hr/>
<p><strong>Unibouw Response:</strong><br/>{replyMessageText}</p>
<p><strong>Replied on:</strong> {reply.MessageDateTime:dd-MMM-yyyy HH:mm}</p>
<p>Regards,<br/>
<strong>{personName}</strong><br/>
Project - Unibouw
</p>";

            // 4️⃣ Send via Microsoft Graph
            var tenantId = _configuration["GraphEmail:TenantId"];
            var clientId = _configuration["GraphEmail:ClientId"];
            var clientSecret = _configuration["GraphEmail:ClientSecret"];
            var senderUser = _configuration["GraphEmail:SenderUser"];
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            var message = new Message
            {
                Subject = reply.Subject ?? "Unibouw – Reply",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = body
                },
                ToRecipients = new List<Recipient>
        {
            new Recipient { EmailAddress = new EmailAddress { Address = sub.Email } }
        }
            };

            // Attachments (if any)
            if (attachmentPaths?.Any() == true)
            {
                message.Attachments = new List<Microsoft.Graph.Models.Attachment>();
                foreach (var path in attachmentPaths.Where(File.Exists))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    message.Attachments.Add(new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = Path.GetFileName(path),
                        ContentBytes = bytes,
                        ContentType = "application/octet-stream"
                    });
                }
            }

            var sendMailBody = new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            };

            await graphClient.Users[senderUser].SendMail.PostAsync(sendMailBody);

            return true;
        }
        // 🔹 Helper methods from your working RFQ/Reminder
        private string GetSenderEmail()
        {
            var email = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value
                        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value;
            return _configuration["GraphEmail:SenderUser"];
        }

        private string GetSenderName()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value
                   ?? _configuration["GraphEmail:DisplayName"] ?? "Unibouw Communications";
        }
    }
}
