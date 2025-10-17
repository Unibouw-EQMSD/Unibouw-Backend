using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using UnibouwAPI.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace UnibouwAPI.Services
{
    public class WorkItemSyncService : BackgroundService
    {
        private readonly ILogger<WorkItemSyncService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly DWHService _dwhService;

        public WorkItemSyncService(ILogger<WorkItemSyncService> logger, IConfiguration configuration, DWHService dwhService)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
            _dwhService = dwhService;
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        //This background task runs continuously and performs sync at a fixed interval.   
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkItemSyncService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting work item sync at {time}", DateTime.Now);

                    var (inserted, updated) = await SyncWorkItems();

                    _logger.LogInformation(
                        "Sync complete. Inserted: {insertedCount}, Updated: {updatedCount}",
                        inserted.Count, updated.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during WorkItem sync");
                }

                // Wait for next run (e.g., every 10 minutes)
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        // Sync work items from DWH into local DB using Dapper.
        public async Task<(List<int> insertedIds, List<int> updatedIds)> SyncWorkItems11()
        {
            var insertedIds = new List<int>();
            var updatedIds = new List<int>();

            // 1. Fetch all latest work items from DWH
            var dwhWorkItems = (await _dwhService.GetLatestWorkItems()).ToList();
            if (!dwhWorkItems.Any())
            {
                _logger.LogInformation("No new or updated work items found in DWH.");
                return (insertedIds, updatedIds);
            }


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
                WorkItemsDwh? existing = null;
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
                            UpdatedAt = item.UpdatedAt ?? DateTime.Now,
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

        public async Task<(List<int> insertedIds, List<int> updatedIds)> SyncWorkItems()
        {
            var insertedIds = new List<int>();
            var updatedIds = new List<int>();

            // 1. Fetch latest work items from DWH
            var dwhWorkItems = (await _dwhService.GetLatestWorkItems()).ToList();
            if (!dwhWorkItems.Any())
            {
                _logger.LogInformation("No new or updated work items found in DWH.");
                return (insertedIds, updatedIds);
            }

            // 2. Get all existing local IDs
            var localIds = (await _connection.QueryAsync<int>("SELECT Id FROM WorkItemsDwh")).ToHashSet();

            // 3. SQL commands
            var sqlInsert = @"
INSERT INTO WorkItemsDwh 
    (Id, Type, Number, Name, WorkItemParent_ID, Created_At, Updated_At, Deleted_At)
VALUES 
    (@Id, @Type, @Number, @Name, @WorkItemParentId, @CreatedAt, @UpdatedAt, @DeletedAt);";

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

            foreach (var item in dwhWorkItems)
            {
                WorkItemsDwh? existing = null;
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
                    bool isDifferent =
                        existing.Type != item.Type ||
                        existing.Number != item.Number ||
                        (existing.Name ?? "") != (item.Name ?? "") ||
                        existing.WorkItemParent_ID != item.WorkItemParent_ID ||
                        existing.DeletedAt != item.DeletedAt;

                    if (isDifferent)
                    {
                        await _connection.ExecuteAsync(sqlUpdate, parameters);
                        updatedIds.Add(item.ID);
                        _logger.LogInformation("Updated: {id}", item.ID);
                    }
                }
                else
                {
                    await _connection.ExecuteAsync(sqlInsert, parameters);
                    insertedIds.Add(item.ID);
                    _logger.LogInformation("Inserted: {id}", item.ID);
                }
            }

            return (insertedIds, updatedIds);
        }
    }
}
