using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using System.Data;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqRepository : IRfq
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

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
        FROM Rfq r
        LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
        LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
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
    wi.WorkItemID,
    wi.Name AS WorkItemName
FROM Rfq r
LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
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

        public async Task<IEnumerable<WorkItem>> GetRfqWorkItemsAsync(Guid rfqId)
        {
            const string sql = @"
SELECT 
    wi.WorkItemID,
    wi.Name
FROM RfqWorkItemMapping rwm
INNER JOIN WorkItems wi ON rwm.WorkItemID = wi.WorkItemID
WHERE rwm.RfqID = @RfqId
  AND wi.IsDeleted = 0"; 
     
    return await _connection.QueryAsync<WorkItem>(sql, new { RfqId = rfqId });
        }
        public async Task<IEnumerable<Rfq>> GetRfqByProjectId(Guid projectId)
        {
            var sql = @"
SELECT 
    r.*,
    c.CustomerName,
    p.Name AS ProjectName,
    wi.WorkItemID,
    wi.Name AS WorkItemName
FROM Rfq r
LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
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
        public async Task<Guid> CreateRfqAsync(Rfq rfq, List<Guid> subcontractorIds)
        {
            rfq.RfqID = Guid.NewGuid();
            rfq.CreatedOn = amsterdamNow;
            rfq.IsDeleted = false;

            if (rfq.SubcontractorDueDates == null || !rfq.SubcontractorDueDates.Any())
                throw new Exception("Subcontractor due dates are required");

            // ✅ 1️⃣ Earliest subcontractor due date
            var mainDueDate = rfq.SubcontractorDueDates
                .Min(x => x.DueDate!.Value.Date);

            rfq.DueDate = mainDueDate;

            // ✅ 2️⃣ Respect frontend GlobalDueDate
            rfq.GlobalDueDate = rfq.GlobalDueDate?.Date ?? mainDueDate;
            

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // ✅ Insert RFQ
                /*                const string insertRfq = @"
                INSERT INTO Rfq
                (
                    RfqID, SentDate, DueDate, RfqSent, QuoteReceived,
                    CustomerID, ProjectID, CustomerNote, DeadLine,
                    CreatedOn, CreatedBy, IsDeleted, Status, GlobalDueDate
                )
                VALUES
                (
                    @RfqID, @SentDate, @DueDate, @RfqSent, @QuoteReceived,
                    @CustomerID, @ProjectID, @CustomerNote, @DeadLine,
                    @CreatedOn, @CreatedBy, @IsDeleted, @Status, @GlobalDueDate
                )";*/

                const string insertRfq = @"
                        INSERT INTO Rfq
                        (
                            RfqID, SentDate, DueDate, GlobalDueDate, RfqSent, QuoteReceived,
                            CustomerID, ProjectID, Status, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy,
                            DeletedOn, DeletedBy, IsDeleted
                        )
                        VALUES
                        (
                            @RfqID, @SentDate, @DueDate, @GlobalDueDate, @RfqSent, @QuoteReceived,
                            @CustomerID, @ProjectID, @Status, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy,
                            @DeletedOn, @DeletedBy, @IsDeleted
                        )";

                await connection.ExecuteAsync(insertRfq, rfq, transaction);

                // ✅ Insert subcontractor-specific due dates
                const string insertSub = @"
INSERT INTO RfqSubcontractorMapping
(
    RfqID,
    SubcontractorID,
    DueDate
)
VALUES
(
    @RfqID,
    @SubcontractorID,
    @DueDate
)";
                foreach (var sub in rfq.SubcontractorDueDates)
                {
                    await connection.ExecuteAsync(insertSub, new
                    {
                        RfqID = rfq.RfqID,
                        SubcontractorID = sub.SubcontractorID,
                        DueDate = sub.DueDate!.Value.Date
                    }, transaction);
                }

                await transaction.CommitAsync();
                return rfq.RfqID;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }


        public async Task SaveSubcontractorWorkItemMappingAsync(
     Guid subcontractorId,
     Guid workItemId,
     string createdBy)
        {
            const string sql = @"
IF NOT EXISTS (
    SELECT 1 FROM SubcontractorWorkItemsMapping
    WHERE SubcontractorID = @SubcontractorID
      AND WorkItemID = @WorkItemID
)
INSERT INTO SubcontractorWorkItemsMapping
(
    SubcontractorID,
    WorkItemID,
    CreatedBy,
    CreatedOn
)
VALUES
(
    @SubcontractorID,
    @WorkItemID,
    @CreatedBy,
    GETDATE()
);";

            await _connection.ExecuteAsync(sql, new
            {
                SubcontractorID = subcontractorId,
                WorkItemID = workItemId,
                CreatedBy = createdBy
            });
        }


        public async Task<bool> SaveOrUpdateRfqSubcontractorMappingAsync(
    Guid rfqId,
    Guid subcontractorId,
    Guid workItemId,
    DateTime dueDate,
    string user)
        {
            const string sql = @"
IF EXISTS (
    SELECT 1
    FROM RfqSubcontractorMapping
    WHERE RfqID = @RfqID
      AND SubcontractorID = @SubcontractorID
)
BEGIN
    UPDATE RfqSubcontractorMapping
    SET 
        DueDate = @DueDate
    WHERE RfqID = @RfqID
      AND SubcontractorID = @SubcontractorID;
END
ELSE
BEGIN
    INSERT INTO RfqSubcontractorMapping
    (
        RfqID,
        SubcontractorID,
        DueDate
    )
    VALUES
    (
        @RfqID,
        @SubcontractorID,
        @DueDate
    );
END
";

            var rows = await _connection.ExecuteAsync(sql, new
            {
                RfqID = rfqId,
                SubcontractorID = subcontractorId,
                DueDate = dueDate.Date
            });

            // 🔹 ALSO ensure global subcontractor–workitem mapping
            await EnsureGlobalSubcontractorWorkItemMapping(workItemId, subcontractorId);

            return rows > 0;
        }



        public async Task<bool> UpdateRfqSubcontractorDueDateAsync(
    Guid rfqId,
    Guid subcontractorId,
    DateTime dueDate)
        {
            const string sql = @"
UPDATE RfqSubcontractorMapping
SET DueDate = @DueDate
WHERE RfqID = @RfqID AND SubcontractorID = @SubcontractorID";

            var rowsAffected = await _connection.ExecuteAsync(sql, new
            {
                RfqID = rfqId,
                SubcontractorID = subcontractorId,
                DueDate = dueDate
            });

            return rowsAffected > 0;
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


        public async Task<bool> UpdateRfqAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate)
        {
            var query = @"
        UPDATE RfqSubcontractorMapping
        SET DueDate = @DueDate
        WHERE RfqID = @RfqID AND SubcontractorID = @SubcontractorID";

            using var connection = _connection;
            var rowsAffected = await connection.ExecuteAsync(query, new
            {
                RfqID = rfqId,
                SubcontractorID = subcontractorId,
                DueDate = dueDate
            });

            return rowsAffected > 0;
        }

        public async Task<bool> UpdateRfqMainAsync(Rfq rfq)
        {
            const string sql = @"
        UPDATE Rfq
        SET
            GlobalDueDate = @GlobalDueDate,
            DueDate = @DueDate,
            CustomNote = @CustomNote,
            ModifiedBy = @ModifiedBy,
            ModifiedOn = @ModifiedOn,
            Status = @Status
        WHERE RfqID = @RfqID";

            var rowsAffected = await _connection.ExecuteAsync(sql, new
            {
                rfq.GlobalDueDate,
                rfq.DueDate,
                rfq.CustomNote,
                rfq.ModifiedBy,
                rfq.ModifiedOn,
                rfq.Status,
                rfq.RfqID
            });

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

        public async Task SaveRfqSubcontractorDueDateAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate)
        {
            const string sql = @"
IF EXISTS (
    SELECT 1 FROM RfqSubcontractorMapping
    WHERE RfqID = @RfqID AND SubcontractorID = @SubId
)
UPDATE RfqSubcontractorMapping
SET DueDate = @DueDate
WHERE RfqID = @RfqID AND SubcontractorID = @SubId
ELSE
INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID, DueDate)
VALUES (@RfqID, @SubId, @DueDate);";

            await _connection.ExecuteAsync(sql, new
            {
                RfqID = rfqId,
                SubId = subcontractorId,
                DueDate = dueDate.Date
            });
        }


        public async Task<bool> UpdateSubcontractorDueDateAsync(
      Guid rfqId,
      Guid subcontractorId,
      DateTime dueDate)
        {
            var query = @"
        UPDATE RfqSubcontractorMapping
        SET DueDate = @DueDate
        WHERE RfqID = @RfqID AND SubcontractorID = @SubcontractorID";

            using var connection = _connection;
            var rows = await connection.ExecuteAsync(query, new
            {
                RfqID = rfqId,
                SubcontractorID = subcontractorId,
                DueDate = dueDate
            });

            return rows > 0;
        }
        public async Task<IEnumerable<dynamic>> GetRfqSubcontractorDueDatesAsync(Guid rfqId)
        {
            const string query = @"
        SELECT
            rwm.WorkItemID,
            rsm.SubcontractorID,
            COALESCE(CONVERT(date, rsm.DueDate), r.GlobalDueDate) AS DueDate,
            r.GlobalDueDate
        FROM RfqWorkItemMapping rwm
        LEFT JOIN RfqSubcontractorMapping rsm
            ON rwm.RFQID = rsm.RFQID
        INNER JOIN Rfq r
            ON r.RFQID = rwm.RFQID
        WHERE rwm.RFQID = @RfqID
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

