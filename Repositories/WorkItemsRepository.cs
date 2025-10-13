using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;


namespace UnibouwAPI.Repositories
{
    public class WorkItemsRepository : IWorkItems
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public WorkItemsRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<WorkItem>> GetAllAsync()
        {
            return await _connection.QueryAsync<WorkItem>("SELECT * FROM WorkItems");
        }

        public async Task<WorkItem?> GetByIdAsync(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<WorkItem>("SELECT * FROM WorkItems WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreateAsync(WorkItem workItem)
        {
            var sql = @"
            INSERT INTO WorkItems 
            (ID, ERP_ID, CategoryID, Number, Name, WorkitemParent_ID, IsActive, CreatedOn, CreatedBy) 
            VALUES 
            (@Id, @ErpId, @CategoryId, @Number, @Name, @WorkitemParentId, @IsActive, @CreatedOn, @CreatedBy)";
           
            return await _connection.ExecuteAsync(sql, workItem);
        }

        public async Task<int> UpdateAsync(WorkItem workItem)
        {
            
            var sql = @"
            UPDATE WorkItems SET
                ERP_ID = @ErpId,
                CategoryID = @CategoryId,
                Number = @Number,
                Name = @Name,
                WorkitemParent_ID = @WorkitemParentId,
                ModifiedOn = @ModifiedOn,
                ModifiedBy = @ModifiedBy
            WHERE ID = @Id";
            workItem.ModifiedOn = DateTime.UtcNow;
            return await _connection.ExecuteAsync(sql, workItem);
        }

        public async Task<int> UpdateIsActiveAsync(Guid id, bool isActive, string modifiedBy)
        {
            var sql = @"
        UPDATE WorkItems SET
            IsActive = @IsActive,
            ModifiedOn = @ModifiedOn,
            ModifiedBy = @ModifiedBy
        WHERE ID = @Id";

            var parameters = new
            {
                Id = id,
                IsActive = isActive,
                ModifiedOn = DateTime.UtcNow,
                ModifiedBy = modifiedBy
            };

            return await _connection.ExecuteAsync(sql, parameters);
        }


        public async Task<int> DeleteAsync(Guid id)
        {
            var sql = "UPDATE WorkItems SET IsActive = 0, DeletedOn = @DeletedOn WHERE ID = @Id";
            return await _connection.ExecuteAsync(sql, new { Id = id, DeletedOn = DateTime.UtcNow });
        }

    }
}
