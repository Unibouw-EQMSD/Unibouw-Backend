using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqRepository : IRfq
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public RfqRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Rfq>> GetAllRfq()
        {
            var query = @"
        SELECT 
            r.*, 
            c.CustomerName,
            p.Name AS ProjectName,
            rs.RfqResponseStatusName               
        FROM Rfq r
        LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
        LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
        LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
        WHERE r.IsDeleted = 0";

            return await _connection.QueryAsync<Rfq>(query);
        }
        public async Task<Rfq?> GetRfqById(Guid id)
        {
            var query = @"
        SELECT 
            r.*, 
            c.CustomerName,
            p.Name AS ProjectName,
            rs.RfqResponseStatusName               
        FROM Rfq r
        LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
        LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
        LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
        WHERE r.RfqID = @Id AND r.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Rfq>(query, new { Id = id });
        }

        public async Task<IEnumerable<Rfq>> GetRfqByProjectId(Guid projectId)
        {
            var query = @"
            SELECT 
                r.*, 
                c.CustomerName,
                p.Name AS ProjectName,
                rs.RfqResponseStatusName               
            FROM Rfq r
            LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
            LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
            LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
            WHERE p.ProjectID = @projectId AND p.IsDeleted = 0";

            return await _connection.QueryAsync<Rfq>(query, new { projectId });
        }

        public async Task<bool> UpdateRfqDueDate(Guid rfqId, DateTime dueDate, string modifiedBy)
        {
            const string query = @"
        UPDATE Rfq
        SET 
            DueDate = @DueDate,
            ModifiedOn = GETUTCDATE(),
            ModifiedBy = @ModifiedBy
        WHERE 
            RfqID = @RfqID AND IsDeleted = 0";

            using var connection = _connection;
            var rowsAffected = await connection.ExecuteAsync(query, new
            {
                RfqID = rfqId,
                DueDate = dueDate,
                ModifiedBy = modifiedBy
            });

            return rowsAffected > 0;
        }

        public async Task<Guid> CreateRfqAsync(Rfq rfq)
        {
            // Generate a new RFQ ID and set defaults
            rfq.RfqID = Guid.NewGuid();
            rfq.CreatedOn = DateTime.UtcNow;
            rfq.IsDeleted = false;

            // Ensure nullable GUIDs are null instead of Guid.Empty
            rfq.CustomerID = rfq.CustomerID == Guid.Empty ? null : rfq.CustomerID;
            rfq.ProjectID = rfq.ProjectID == Guid.Empty ? null : rfq.ProjectID;
            rfq.RfqResponseID = rfq.RfqResponseID == Guid.Empty ? null : rfq.RfqResponseID;

            using var connection = _connection;

            // Insert RFQ without RfqNumber (trigger handles it)
            var query = @"
INSERT INTO Rfq
(
    RfqID, SentDate, DueDate, RfqSent, QuoteReceived,
    CustomerID, ProjectID, RfqResponseID, CustomerNote, DeadLine,
    CreatedOn, CreatedBy, ModifiedOn, ModifiedBy,
    DeletedOn, DeletedBy, IsDeleted, Status,GlobalDueDate
)
VALUES
(
    @RfqID, @SentDate, @DueDate, @RfqSent, @QuoteReceived,
    @CustomerID, @ProjectID, @RfqResponseID, @CustomerNote, @DeadLine,
    @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy,
    @DeletedOn, @DeletedBy, @IsDeleted, @Status,@GlobalDueDate
)";

            var rows = await connection.ExecuteAsync(query, rfq);
            if (rows == 0)
                throw new Exception("Failed to create RFQ.");

            // Return only the GUID
            return rfq.RfqID;
        }




        public async Task InsertRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds)
        {
            if (workItemIds == null || workItemIds.Count == 0)
                return;

            var query = @"
        INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
        VALUES (@RfqID, @WorkItemID)";

            using var connection = _connection;
            foreach (var workItemId in workItemIds)
            {
                await connection.ExecuteAsync(query, new { RfqID = rfqId, WorkItemID = workItemId });
            }
        }

        public async Task<(string WorkItemName, int SubCount)> GetWorkItemInfoByRfqId(Guid rfqId)
        {
            var sql = @"
        SELECT TOP 1 w.Name
        FROM RfqWorkItemMapping m
        JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
        WHERE m.RfqID = @rfqId;

        SELECT COUNT(*) 
        FROM SubcontractorWorkItemsMapping s
        WHERE s.WorkItemID IN (
            SELECT WorkItemID FROM RfqWorkItemMapping WHERE RfqID = @rfqId
        );
    ";

            using var multi = await _connection.QueryMultipleAsync(sql, new { rfqId });

            var workItemName = await multi.ReadFirstOrDefaultAsync<string>() ?? "-";
            var subcontractorCount = await multi.ReadFirstAsync<int>();

            return (workItemName, subcontractorCount);
        }

        public async Task<bool> UpdateRfqAsync(Rfq rfq)
        {
            // Update logic for main RFQ fields
            var query = @"
        UPDATE Rfq
        SET 
            SentDate = @SentDate,
            DueDate = @DueDate,
            RfqSent = @RfqSent,
            QuoteReceived = @QuoteReceived,
            CustomerID = @CustomerID,
            ProjectID = @ProjectID,
            RfqResponseID = @RfqResponseID,
            CustomerNote = @CustomerNote,
            DeadLine = @DeadLine,
            ModifiedOn = GETUTCDATE(),
            ModifiedBy = @ModifiedBy,
            Status = @Status,
GlobalDueDate = @GlobalDueDate,
        WHERE 
            RfqID = @RfqID AND IsDeleted = 0";

            rfq.ModifiedOn = DateTime.UtcNow; // Set value for Dapper
            rfq.ModifiedBy = rfq.ModifiedBy ?? "System"; // Ensure ModifiedBy is set

            using var connection = _connection;
            var rowsAffected = await connection.ExecuteAsync(query, rfq);

            return rowsAffected > 0;
        }

        public async Task UpdateRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds)
        {
            using var connection = _connection;
            // 1. Delete all existing mappings
            await connection.ExecuteAsync("DELETE FROM RfqWorkItemMapping WHERE RfqID = @RfqID", new { RfqID = rfqId });

            // 2. Insert new mappings (reusing existing Insert method logic)
            if (workItemIds != null && workItemIds.Any())
            {
                var parameters = workItemIds.Select(id => new { RfqID = rfqId, WorkItemID = id });
                await connection.ExecuteAsync(
                    "INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID) VALUES (@RfqID, @WorkItemID)",
                    parameters
                );
            }
        }

        public async Task UpdateRfqSubcontractorsAsync(Guid rfqId, List<Guid> subcontractorIds)
        {
            using var connection = _connection;

            // 1️⃣ DELETE RESPONSES FIRST (avoid FK conflicts)
            var deleteResponsesQuery = @"
        DELETE FROM RfqSubcontractorResponse
        WHERE RfqID = @RfqID;
    ";
            await connection.ExecuteAsync(deleteResponsesQuery, new { RfqID = rfqId });

            // 2️⃣ DELETE EXISTING MAPPINGS
            var deleteMappingsQuery = @"
        DELETE FROM RfqSubcontractorMapping
        WHERE RfqID = @RfqID;
    ";
            await connection.ExecuteAsync(deleteMappingsQuery, new { RfqID = rfqId });

            // 3️⃣ INSERT NEW MAPPINGS
            if (subcontractorIds != null && subcontractorIds.Any())
            {
                var insertParams = subcontractorIds.Select(id => new
                {
                    RfqID = rfqId,
                    SubcontractorID = id
                });

                var insertQuery = @"
            INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
            VALUES (@RfqID, @SubcontractorID);
        ";

                await connection.ExecuteAsync(insertQuery, insertParams);
            }
        }

        public async Task<IEnumerable<dynamic>> GetRfqSubcontractorDueDatesAsync(Guid rfqId)
        {
            var query = @"
        SELECT 
            rwm.WorkItemID,
            CONVERT(NVARCHAR, r.DueDate, 126) AS DueDate
        FROM Rfq r
        JOIN RfqWorkItemMapping rwm 
            ON r.RfqID = rwm.RfqID
        WHERE r.RfqID = @RfqID
    ";

            return await _connection.QueryAsync(query, new { RfqID = rfqId });
        }

        // ⭐ GLOBAL MAPPING IMPLEMENTATION
        public async Task EnsureGlobalSubcontractorWorkItemMapping(Guid workItemId, Guid subcontractorId)
        {
            const string query = @"
                IF NOT EXISTS (
                    SELECT 1 
                    FROM SubcontractorWorkItemsMapping 
                    WHERE WorkItemID = @WorkItemID AND SubcontractorID = @SubcontractorID
                )
                BEGIN
                    INSERT INTO SubcontractorWorkItemsMapping (WorkItemID, SubcontractorID)
                    VALUES (@WorkItemID, @SubcontractorID);
                END";

            using var connection = _connection;
            await connection.ExecuteAsync(query, new { WorkItemID = workItemId, SubcontractorID = subcontractorId });
        }
    
}

}

