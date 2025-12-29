using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RFQConversationMessageRepository : IRFQConversationMessage
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public RFQConversationMessageRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
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
            var sql = @"
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

            var result = await _connection.QueryAsync<ConversationMessageDto>(
                sql,
                new { projectId, subcontractorId }
            );

            return result.ToList();
        }

    }
}
