using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories
{
    public class WorkItemsRepository : IWorkItems
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

        public WorkItemsRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");         
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<WorkItem>> GetAllWorkItems()
        {
            var query = @"
        SELECT 
            w.*, 
            c.CategoryName
        FROM WorkItems w
        LEFT JOIN WorkItemCategoryTypes c ON w.CategoryID = c.CategoryID
        WHERE w.IsDeleted = 0";

            return await _connection.QueryAsync<WorkItem>(query);
            //return await _connection.QueryAsync<WorkItem>("SELECT * FROM WorkItems WHERE isDeleted=0");
        }

        public async Task<WorkItem?> GetWorkItemById(Guid id)
        {
            var query = @"
        SELECT 
            w.*, 
            c.CategoryName
        FROM WorkItems w
        LEFT JOIN WorkItemCategoryTypes c ON w.CategoryID = c.CategoryID
        WHERE w.WorkItemID = @Id AND w.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<WorkItem>(query, new { Id = id });
            // return await _connection.QueryFirstOrDefaultAsync<WorkItem>("SELECT * FROM WorkItems WHERE WorkItemID = @Id AND isDeleted=0", new { Id = id });
        }

        public async Task<int> UpdateWorkItemIsActive(Guid id, bool isActive, string modifiedBy)
        {
            // Check if WorkItem is non-deleted WorkItem
            var workItem = await _connection.QueryFirstOrDefaultAsync<WorkItem>(
                "SELECT * FROM WorkItems WHERE WorkItemID = @Id AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found (either inactive or deleted)
            if (workItem == null)
                return 0;

            var query = @"
        UPDATE WorkItems SET
            IsActive = @IsActive,
            ModifiedOn = @ModifiedOn,
            ModifiedBy = @ModifiedBy
        WHERE WorkItemID = @Id";

            var parameters = new
            {
                Id = id,
                IsActive = isActive,
                ModifiedOn = amsterdamNow,
                ModifiedBy = modifiedBy
            };

            return await _connection.ExecuteAsync(query, parameters);
        }

        public async Task<int> UpdateWorkItemDescription(Guid id, string description, string modifiedBy)
        {
            // Ensure WorkItem exists, active, and not deleted
            var workItem = await _connection.QueryFirstOrDefaultAsync<WorkItem>(
                "SELECT * FROM WorkItems WHERE WorkItemID = @Id AND IsActive = 1 AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found (either inactive or deleted)
            if (workItem == null)
                return 0;

            var query = @"UPDATE WorkItems
                  SET Description = @Description,
                      ModifiedBy = @ModifiedBy,
                      ModifiedOn = GETDATE()
                  WHERE WorkItemID = @Id";

            var parameters = new
            {
                Id = id,
                Description = description,
                ModifiedOn = amsterdamNow,
                ModifiedBy = modifiedBy
            };

            return await _connection.ExecuteAsync(query, parameters);
        }


    }
}
