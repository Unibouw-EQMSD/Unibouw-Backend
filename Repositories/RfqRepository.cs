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
            var sql = @"
SELECT 
    r.*,
    c.CustomerName,
    p.Name AS ProjectName,
    rs.RfqResponseStatusName,
    wi.WorkItemID,
    wi.Name AS WorkItemName
FROM Rfq r
LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
LEFT JOIN RfqWorkItemMapping rw ON r.RfqID = rw.RfqID
LEFT JOIN WorkItems wi ON rw.WorkItemID = wi.WorkItemID
WHERE r.RfqID = @Id AND r.IsDeleted = 0";

            var rfqDictionary = new Dictionary<Guid, Rfq>();

            var result = await _connection.QueryAsync<Rfq, WorkItem, Rfq>(
                sql,
                (rfq, workItem) =>
                {
                    if (!rfqDictionary.TryGetValue(rfq.RfqID, out var rfqEntry))
                    {
                        rfqEntry = rfq;
                        rfqEntry.WorkItems = new List<WorkItem>();
                        rfqDictionary.Add(rfq.RfqID, rfqEntry);
                    }

                    if (workItem != null)
                        rfqEntry.WorkItems.Add(workItem);

                    return rfqEntry;
                },
                new { Id = id },
                splitOn: "WorkItemID"
            );

            return rfqDictionary.Values.FirstOrDefault();
        }

        public async Task<IEnumerable<Rfq>> GetRfqByProjectId(Guid projectId)
        {
            var sql = @"
SELECT 
    r.*,
    c.CustomerName,
    p.Name AS ProjectName,
    rs.RfqResponseStatusName,
    wi.WorkItemID,
    wi.Name AS WorkItemName
FROM Rfq r
LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
LEFT JOIN RfqWorkItemMapping rw ON r.RfqID = rw.RfqID
LEFT JOIN WorkItems wi ON rw.WorkItemID = wi.WorkItemID
WHERE p.ProjectID = @projectId
  AND r.IsDeleted = 0
ORDER BY r.CreatedOn DESC";

            var rfqDict = new Dictionary<Guid, Rfq>();

            await _connection.QueryAsync<Rfq, WorkItem, Rfq>(
                sql,
                (rfq, workItem) =>
                {
                    if (!rfqDict.TryGetValue(rfq.RfqID, out var rfqEntry))
                    {
                        rfqEntry = rfq;
                        rfqEntry.WorkItems = new List<WorkItem>();
                        rfqDict.Add(rfq.RfqID, rfqEntry);
                    }

                    if (workItem != null)
                    {
                        rfqEntry.WorkItems.Add(workItem);
                    }

                    return rfqEntry;
                },
                new { projectId },
                splitOn: "WorkItemID"
            );

            return rfqDict.Values;
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

            const string sql = @"
IF NOT EXISTS (
    SELECT 1 FROM RfqWorkItemMapping 
    WHERE RfqID = @RfqID AND WorkItemID = @WorkItemID
)
INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
VALUES (@RfqID, @WorkItemID);";

            using var connection = new SqlConnection(_connection.ConnectionString);
            await connection.OpenAsync();

            foreach (var id in workItemIds)
            {
                await connection.ExecuteAsync(sql, new
                {
                    RfqID = rfqId,
                    WorkItemID = id
                });
            }
        }

        public async Task<(string WorkItemNames, int SubCount)> GetWorkItemInfoByRfqId(Guid rfqId)
        {
            var sql = @"
    -- 🔹 ALL work item names for RFQ
    SELECT w.Name
    FROM RfqWorkItemMapping m
    JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
    WHERE m.RfqID = @rfqId;

    -- 🔹 Subcontractor count
    SELECT COUNT(DISTINCT s.SubcontractorID)
    FROM SubcontractorWorkItemsMapping s
    WHERE s.WorkItemID IN (
        SELECT WorkItemID 
        FROM RfqWorkItemMapping 
        WHERE RfqID = @rfqId
    );
    ";

            using var multi = await _connection.QueryMultipleAsync(sql, new { rfqId });

            var workItemNames = (await multi.ReadAsync<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var subcontractorCount = await multi.ReadFirstAsync<int>();

            return (
                workItemNames.Any() ? string.Join(", ", workItemNames) : "-",
                subcontractorCount
            );
        }


        public async Task<bool> UpdateRfqAsync(Rfq rfq)
        {
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
            GlobalDueDate = @GlobalDueDate
        WHERE 
            RfqID = @RfqID AND IsDeleted = 0";

            rfq.ModifiedOn = DateTime.UtcNow;
            rfq.ModifiedBy = rfq.ModifiedBy ?? "System";

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
        public async Task<bool?> DeleteRfqAsync(Guid rfqId, string deletedBy)
        {
            using var connection = _connection;

            /* 1️⃣ Check RFQ exists & not already deleted */
            const string rfqExistsSql = @"
        SELECT COUNT(1)
        FROM Rfq
        WHERE RfqID = @RfqID
          AND IsDeleted = 0";

            var exists = await connection.ExecuteScalarAsync<int>(
                rfqExistsSql,
                new { RfqID = rfqId }
            );

            if (exists == 0)
                return null; // RFQ not found or already deleted


            /* 2️⃣ Check if ANY subcontractor submitted a quote */
            const string quoteCheckSql = @"
        SELECT COUNT(1)
        FROM RfqSubcontractorResponse
        WHERE RfqID = @RfqID
          AND ISNULL(SubmissionCount, 0) > 0";

            var submittedQuotes = await connection.ExecuteScalarAsync<int>(
                quoteCheckSql,
                new { RfqID = rfqId }
            );

            if (submittedQuotes > 0)
                return false; // ❌ Block delete


            /* 3️⃣ Soft delete RFQ */
            const string deleteSql = @"
        UPDATE Rfq
        SET IsDeleted = 1,
            DeletedOn = GETUTCDATE(),
            DeletedBy = @DeletedBy
        WHERE RfqID = @RfqID
          AND IsDeleted = 0";

            await connection.ExecuteAsync(deleteSql, new
            {
                RfqID = rfqId,
                DeletedBy = deletedBy
            });

            return true; // ✅ Deleted successfully
        }

    }
}

