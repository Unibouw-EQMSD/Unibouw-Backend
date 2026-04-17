using Dapper;
using Microsoft.Data.SqlClient;
using UnibouwAPI.Helpers;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqEmailIngestionRepository : IRfqEmailIngestionRepository
    {
        private readonly string _cs;
        private readonly IConfiguration _config;
        private readonly string _connectionString;

        public RfqEmailIngestionRepository(IConfiguration config)
        {
            _config = config;
            _cs = config.GetConnectionString("UnibouwDbConnection")
                  ?? throw new InvalidOperationException("Connection string not configured.");
            _connectionString = config.GetConnectionString("UnibouwDbConnection")
                ?? throw new InvalidOperationException("Connection string not configured.");
        }

        public async Task<List<string>> GetAllPersonMailboxesAsync()
        {
            var sender = _config["GraphEmail:SenderUser"];

            if (string.IsNullOrWhiteSpace(sender))
                return new List<string>();

            return new List<string> { sender.Trim() };
        }

        public async Task<(DateTime cursorUtc, HashSet<string> processedIdsAtCursor)> GetOrCreateCursorUtcAsync(string pmMailbox)
        {
            const string sel = @"
    SELECT LastReceivedUtcProcessed, LastProcessedMessageIdsAtCursor
    FROM dbo.MailboxPollCursor
    WHERE PmMailbox = @PmMailbox;";
            using var conn = new SqlConnection(_cs);
            var row = await conn.QuerySingleOrDefaultAsync(sel, new { PmMailbox = pmMailbox });

            if (row != null)
            {
                DateTime cursor = (DateTime)row.LastReceivedUtcProcessed;
                var ids = (row.LastProcessedMessageIdsAtCursor as string ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return (cursor, ids);
            }

            var start = DateTime.UtcNow.AddDays(-7);
            const string ins = @"
    INSERT INTO dbo.MailboxPollCursor(PmMailbox, LastReceivedUtcProcessed, LastProcessedMessageIdsAtCursor)
    VALUES(@PmMailbox, @StartUtc, N'')";
            await conn.ExecuteAsync(ins, new { PmMailbox = pmMailbox, StartUtc = start });
            return (start, new HashSet<string>());
        }

        public async Task UpdateCursorUtcAsync(string pmMailbox, DateTime utc, string processedIdsCsv)
        {
            const string sql = @"
    UPDATE dbo.MailboxPollCursor
    SET LastReceivedUtcProcessed = @Utc,
        LastProcessedMessageIdsAtCursor = @ProcessedIdsCsv,
        LastRunUtc = SYSUTCDATETIME(),
        LastError = NULL
    WHERE PmMailbox = @PmMailbox;";
            using var conn = new SqlConnection(_cs);
            await conn.ExecuteAsync(sql, new { PmMailbox = pmMailbox, Utc = utc, ProcessedIdsCsv = processedIdsCsv ?? "" });
        }

        public async Task UpdateCursorRunAsync(string pmMailbox, string? error)
        {
            const string sql = @"
UPDATE dbo.MailboxPollCursor
SET LastRunUtc = SYSUTCDATETIME(),
    LastError = @Err
WHERE PmMailbox = @PmMailbox;";
            using var conn = new SqlConnection(_cs);
            await conn.ExecuteAsync(sql, new { PmMailbox = pmMailbox, Err = error });
        }

        public async Task InsertOutboundAnchorAsync(Guid projectId, Guid subcontractorId, Guid? rfqId,
            string pmMailbox, string? conversationId, string? internetMessageId, string? graphMessageId,
            DateTime sentUtc, string? subject)
        {
            const string sql = @"
INSERT INTO dbo.OutboundRfqEmailAnchor
(AnchorId, ProjectId, SubcontractorId, RfqId, PmMailbox, ConversationId, InternetMessageId, GraphMessageId, SentUtc, Subject, IsActive)
VALUES
(@AnchorId, @ProjectId, @SubcontractorId, @RfqId, @PmMailbox, @ConversationId, @InternetMessageId, @GraphMessageId, @SentUtc, @Subject, 1);";

            using var conn = new SqlConnection(_cs);
            await conn.ExecuteAsync(sql, new
            {
                AnchorId = Guid.NewGuid(),
                ProjectId = projectId,
                SubcontractorId = subcontractorId,
                RfqId = rfqId,
                PmMailbox = pmMailbox,
                ConversationId = conversationId,
                InternetMessageId = internetMessageId,
                GraphMessageId = graphMessageId,
                SentUtc = sentUtc,
                Subject = subject
            });
        }

        public async Task<bool> AnchorExistsAsync(Guid projectId, Guid subcontractorId, Guid rfqId, string pmMailbox, string conversationId)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.OutboundRfqEmailAnchor
WHERE IsActive = 1
  AND ProjectId = @ProjectId
  AND SubcontractorId = @SubId
  AND RfqId = @RfqId
  AND PmMailbox = @PmMailbox
  -- If any anchor for this combo exists, we consider it valid, regardless of ConversationId
";
            using var conn = new SqlConnection(_cs);
            var x = await conn.ExecuteScalarAsync<int?>(sql, new
            {
                ProjectId = projectId,
                SubId = subcontractorId,
                RfqId = rfqId,
                PmMailbox = pmMailbox
                // ConversationId is not used in the WHERE clause anymore
            });
            return x.HasValue;
        }

        public async Task<bool> IsAlreadyIngestedAsync(string graphMessageId, string folder)
        {
            const string sql = @"SELECT TOP 1 1 FROM dbo.InboundEmailIngested WHERE GraphMessageId = @Id AND Folder = @Folder;";
            using var conn = new SqlConnection(_cs);
            var x = await conn.ExecuteScalarAsync<int?>(sql, new { Id = graphMessageId, Folder = folder });
            return x.HasValue;
        }

        public async Task MarkIngestedAsync(string pmMailbox, string graphMessageId, string folder, DateTime receivedUtc)
        {

            const string sql = @"
INSERT INTO dbo.InboundEmailIngested(Id, PmMailbox, GraphMessageId, Folder, ReceivedUtc, CreatedUtc)
VALUES(@Id, @PmMailbox, @GraphMessageId, @Folder, @ReceivedUtc, SYSUTCDATETIME());";

            using var conn = new SqlConnection(_cs);
            var db = await conn.ExecuteScalarAsync<string>("SELECT DB_NAME()");
            Console.WriteLine($"[MarkIngestedAsync] DB={db}");
            try
            {
                await conn.ExecuteAsync(sql, new
                {
                    Id = Guid.NewGuid(),
                    PmMailbox = pmMailbox,
                    GraphMessageId = graphMessageId,
                    Folder = folder,
                    ReceivedUtc = receivedUtc
                });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // ignore duplicate
            }
        }

        public async Task InsertInboundToConversationAsync(
    Guid projectId,
    Guid subcontractorId,
    string subject,
    string message,
    DateTime receivedAms,
    string fromEmail)
        {
            const string sql = @"
INSERT INTO dbo.RFQConversationMessage
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
    Subject,
    Tag
)
VALUES
(
    @Id,
    @ProjectId,
    @SubId,
    'Subcontractor',
    @Message,
    @ReceivedAms,
    'Active',
    @CreatedBy,
    @CreatedOnAms,
    @Subject,
    'Inbound Email Reply'
);";

            using var conn = new SqlConnection(_cs);
            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SubId = subcontractorId,
                Message = message ?? "",
                ReceivedAms = receivedAms,
                CreatedBy = string.IsNullOrWhiteSpace(fromEmail) ? "unknown" : fromEmail,
                CreatedOnAms = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow),
                Subject = subject ?? ""
            });
        }

        public async Task InsertPmSentToConversationAsync(
     Guid projectId,
     Guid subcontractorId,
     string subject,
     string message,
     DateTime sentAms,
     string pmMailbox)
        {
            const string sql = @"
INSERT INTO dbo.RFQConversationMessage
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
    @Id,
    @ProjectId,
    @SubId,
    'PM',
    @Message,
    @SentAms,
    'Active',
    @CreatedBy,
    @CreatedOnAms,
    @Subject
);";

            using var conn = new SqlConnection(_cs);

            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SubId = subcontractorId,
                Message = message ?? "",
                SentAms = sentAms,
                CreatedBy = pmMailbox,
                CreatedOnAms = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow),
                Subject = subject ?? ""
            });
        }
        private readonly SqlConnection _connection;

        public async Task<dynamic> GetOfficialProjectDetailsAsync(Guid projectId, Guid rfqId, Guid subId)
        {
            return await _connection.QuerySingleAsync<dynamic>(
                @"SELECT 
            p.Number AS ProjectCode,
            p.Name AS ProjectName,
            r.RfqNumber,
            rsm.DueDate
        FROM Projects p
        INNER JOIN Rfq r ON r.ProjectID = p.ProjectID
        INNER JOIN RfqSubcontractorMapping rsm ON rsm.RfqID = r.RfqID
        WHERE p.ProjectID = @ProjectID AND r.RfqID = @RfqID AND rsm.SubcontractorID = @SubID",
                new { ProjectID = projectId, RfqID = rfqId, SubID = subId }
            );
        }

        public async Task InsertInboundToLogConversationAsync(
    Guid projectId,
    Guid subcontractorId,
    string subject,
    string message,
    DateTime receivedAms,
    string fromEmail)
        {
            const string sql = @"
INSERT INTO dbo.RfqLogConversation
(LogConversationID, ProjectID, SubcontractorID, ConversationType, MessageDateTime, Subject, Message, CreatedBy, CreatedOn)
VALUES
(@Id, @ProjectId, @SubId, 'Email', @ReceivedAms, @Subject, @Message, @CreatedBy, @CreatedOnAms);";

            using var conn = new SqlConnection(_cs);

            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SubId = subcontractorId,
                ReceivedAms = receivedAms,
                Subject = subject ?? "",
                Message = message ?? "",
                CreatedBy = string.IsNullOrWhiteSpace(fromEmail) ? "unknown" : fromEmail,
                CreatedOnAms = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow)
            });
        }
    }
    }