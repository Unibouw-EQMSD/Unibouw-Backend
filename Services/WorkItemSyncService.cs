using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using UnibouwAPI.Models;

namespace UnibouwAPI.Services
{
    public class WorkItemSyncService
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly DWHService _dwhService;

        public WorkItemSyncService(IConfiguration configuration, DWHService dwhService)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
            _dwhService = dwhService;
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);
 
        public async Task<(List<int> insertedIds, List<int> updatedIds)> SyncWorkItems()
        {
            var insertedIds = new List<int>();
            var updatedIds = new List<int>();

            // 1. Fetch all latest work items from DWH
            var dwhWorkItems = (await _dwhService.GetLatestWorkItems()).ToList();
            if (!dwhWorkItems.Any()) return (insertedIds, updatedIds);

            // 2. Get all existing local IDs
            var localIds = (await _connection.QueryAsync<int>("SELECT Id FROM WorkItemsDwh"))
                           .ToHashSet();

            // 3. SQL for insert
            var sqlInsert = @"
INSERT INTO WorkItemsDwh 
    (Id, Type, Number, Name, WorkItemParent_ID, Created_At, Updated_At, Deleted_At)
VALUES 
    (@Id, @Type, @Number, @Name, @WorkItemParentId, @CreatedAt, @UpdatedAt, @DeletedAt);";

            // 4. SQL for update
            var sqlUpdate = @"
UPDATE WorkItemsDwh
SET             
    Type = @Type,
    Number = @Number,
    Name = @Name,
    WorkItemParent_ID = @WorkItemParentId,
    Updated_At = @UpdatedAt,
    Deleted_At = @DeletedAt
WHERE Id = @Id;";

            // 5. Loop through all DWH items
            foreach (var item in dwhWorkItems)
            {
                // Fetch existing row if it exists
                WorkItemsDwh existing = null;
                if (localIds.Contains(item.ID))
                {
                    existing = await _connection.QueryFirstOrDefaultAsync<WorkItemsDwh>(
                        "SELECT * FROM WorkItemsDwh WHERE Id = @Id", new { Id = item.ID });
                }

                var parameters = new
                {
                    Id = item.ID,
                    Type = item.Type,
                    Number = item.Number,
                    Name = item.Name,
                    WorkItemParentId = item.WorkItemParent_ID,
                    CreatedAt = item.CreatedAt ?? DateTime.Now,
                    UpdatedAt = item.UpdatedAt ?? DateTime.Now,
                    DeletedAt = item.DeletedAt
                };

                if (existing != null)
                {
                    // Null-safe comparison
                    bool isDifferent =
                        existing.Type != item.Type ||
                        existing.Number != item.Number ||
                        (existing.Name ?? "") != (item.Name ?? "") ||
                        existing.WorkItemParent_ID != item.WorkItemParent_ID ||
                        existing.DeletedAt != item.DeletedAt;

                    if (isDifferent)
                    {
                        // Only update if something really changed
                        // Set UpdatedAt to now if item.UpdatedAt is null
                        var updateParams = parameters; // copy existing
                        updateParams = new
                        {
                            parameters.Id,
                            parameters.Type,
                            parameters.Number,
                            parameters.Name,
                            parameters.WorkItemParentId,
                            CreatedAt = existing.CreatedAt ?? parameters.CreatedAt,
                            UpdatedAt = item.UpdatedAt ?? DateTime.UtcNow,
                            parameters.DeletedAt
                        };
                        await _connection.ExecuteAsync(sqlUpdate, parameters);
                        updatedIds.Add(item.ID);
                        Console.WriteLine($"Updated: {item.ID}");
                    }
                }
                else
                {
                    // Insert missing
                    await _connection.ExecuteAsync(sqlInsert, parameters);
                    insertedIds.Add(item.ID);
                    Console.WriteLine($"Inserted: {item.ID}");
                }
            }

            Console.WriteLine("Sync complete.");
            return (insertedIds, updatedIds);
        }

    }
}
