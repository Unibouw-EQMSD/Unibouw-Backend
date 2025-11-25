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

        public async Task<Rfq?> GetRfqByProjectId(Guid projectId)
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

            return await _connection.QueryFirstOrDefaultAsync<Rfq>(query, new { projectId });
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
            var query = @"
        INSERT INTO Rfq
        (RfqID, SentDate, DueDate, RfqSent, QuoteReceived, CustomerID, ProjectID, CustomerNote, DeadLine, CreatedOn, CreatedBy, IsDeleted)
        VALUES
        (@RfqID, @SentDate, @DueDate, @RfqSent, @QuoteReceived, @CustomerID, @ProjectID, @CustomerNote, @DeadLine, GETUTCDATE(), @CreatedBy, 0)";

            rfq.RfqID = Guid.NewGuid(); // generate new ID

            using var connection = _connection;
            var rows = await connection.ExecuteAsync(query, rfq);
            if (rows == 0)
                throw new Exception("Failed to create RFQ.");

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



    }
}
