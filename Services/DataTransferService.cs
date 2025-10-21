using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UnibouwAPI.Models;

public class DataTransferService
{
    private readonly string _sourceConnection;
    private readonly string _targetConnection;

    public DataTransferService(IConfiguration config)
    {
        _sourceConnection = config.GetConnectionString("DWHDb");
        _targetConnection = config.GetConnectionString("UnibouwDbConnection");
    }

    public async Task<(List<int> Inserted, List<int> Updated, List<int> Skipped)> TransferDataAsync()
    {
        IEnumerable<WorkItemsLocal> sourceData;

        // Step 1: Read from source
        using (var sourceConn = new SqlConnection(_sourceConnection))
        {
            await sourceConn.OpenAsync();
            string selectQuery = "SELECT * FROM dbo.WorkItems";
            sourceData = await sourceConn.QueryAsync<WorkItemsLocal>(selectQuery);
        }

        // Step 2: Merge into target and track actions
        var insertedIds = new List<int>();
        var updatedIds = new List<int>();
        var skippedIds = new List<int>();

        using (var targetConn = new SqlConnection(_targetConnection))
        {
            await targetConn.OpenAsync();

            string mergeQuery = @"
                MERGE dbo.WorkItemsLocal AS target
                USING (SELECT @Id AS Id, @Type AS Type, @Number AS Number, 
                              @Name AS Name, @WorkItemParent_ID AS WorkItemParent_ID,
                              @created_at AS created_at, @updated_at AS updated_at) AS source
                ON target.Id = source.Id

                WHEN MATCHED AND (
                    ISNULL(target.Type, '') <> ISNULL(source.Type, '') OR
                    ISNULL(target.Number, '') <> ISNULL(source.Number, '') OR
                    ISNULL(target.Name, '') <> ISNULL(source.Name, '') OR
                    ISNULL(target.WorkItemParent_ID, 0) <> ISNULL(source.WorkItemParent_ID, 0) OR
                    ISNULL(target.created_at, '') <> ISNULL(source.created_at, '') OR
                    ISNULL(target.updated_at, '') <> ISNULL(source.updated_at, '')
                )
                THEN UPDATE SET 
                    Type = source.Type,
                    Number = source.Number,
                    Name = source.Name,
                    WorkItemParent_ID = source.WorkItemParent_ID,
                    created_at = source.created_at,
                    updated_at = source.updated_at

                WHEN NOT MATCHED BY TARGET THEN 
                    INSERT (Id, Type, Number, Name, WorkItemParent_ID, created_at, updated_at)
                    VALUES (source.Id, source.Type, source.Number, source.Name, source.WorkItemParent_ID, source.created_at, source.updated_at)

                OUTPUT 
                    $action AS MergeAction, 
                    inserted.Id AS AffectedId;
            ";

            foreach (var item in sourceData)
            {
                var results = await targetConn.QueryAsync<(string MergeAction, int? AffectedId)>(mergeQuery, item);

                if (!results.Any())
                {
                    // No change → record exists and identical
                    skippedIds.Add(item.Id);
                }
                else
                {
                    foreach (var res in results)
                    {
                        if (res.MergeAction == "INSERT")
                            insertedIds.Add(res.AffectedId ?? item.Id);
                        else if (res.MergeAction == "UPDATE")
                            updatedIds.Add(res.AffectedId ?? item.Id);
                    }
                }
            }
        }

        // Step 3: Log summary
        Console.WriteLine($"Inserted: {insertedIds.Count} → IDs: {string.Join(", ", insertedIds)}");
        Console.WriteLine($"Updated: {updatedIds.Count} → IDs: {string.Join(", ", updatedIds)}");
        Console.WriteLine($"No Change: {skippedIds.Count} → IDs: {string.Join(", ", skippedIds)}");

        // Step 4: Return results for API use
        return (insertedIds, updatedIds, skippedIds);
    }
}
