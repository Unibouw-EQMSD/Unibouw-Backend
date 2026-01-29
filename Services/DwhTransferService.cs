using Dapper;
using Microsoft.Data.SqlClient;
using UnibouwAPI.Models;

namespace UnibouwAPI.Services
{
    public class DwhTransferService
    {
        private readonly string _sourceConnection;
        private readonly string _targetConnection;

        public DwhTransferService(IConfiguration config)
        {
            _sourceConnection = config.GetConnectionString("DWHDb");
            _targetConnection = config.GetConnectionString("UnibouwDbConnection");
        }

        public async Task<(
            (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Categories,(List<long?> Inserted, List<long?> Updated, List<long?> Skipped) WorkItems,
            (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Customers,(List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Projects
        )> SyncAllAsync()
        {
            var categories = await SyncWorkItemCategoryTypesAsync();
            var workItems = await SyncWorkItemsAsync();
            var customers = await SyncCustomersAsync();
            var projects = await SyncProjectsAsync();

            return (categories, workItems, customers, projects);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> TransferDataAsync1()
        {
            // Step 1: Read all categories from source
            IEnumerable<WorkItemCategoryType> categories;
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();
                string categoryQuery = @"SELECT CategoryID, CategoryName FROM dbo.WorkItemCategoryTypes";
                categories = await sourceConn.QueryAsync<WorkItemCategoryType>(categoryQuery);
            }

            // Step 2: Upsert categories into target
            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();
                const string upsertCategorySql = @"
                    MERGE dbo.WorkItemCategoryTypes AS target
                    USING (SELECT @CategoryID AS CategoryID, @CategoryName AS CategoryName) AS source
                    ON target.CategoryID = source.CategoryID
                    WHEN MATCHED THEN
                        UPDATE SET CategoryName = source.CategoryName
                    WHEN NOT MATCHED BY TARGET THEN
                        INSERT (CategoryID, CategoryName) VALUES (source.CategoryID, source.CategoryName);";
                foreach (var cat in categories)
                {
                    await targetConn.ExecuteAsync(upsertCategorySql, cat);
                }
            }

            // Step 3: Read work items from source
            IEnumerable<WorkItem> sourceData;
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();
                string selectQuery = @"
                    SELECT
                        ERP_ID,
                        CategoryID,
                        Number,
                        Name,
                        WorkItemParentID,
                        IsActive,
                        CreatedOn,
                        CreatedBy,
                        ModifiedOn,
                        ModifiedBy,
                        DeletedOn,
                        DeletedBy,
                        IsDeleted
                    FROM dbo.WorkItems";
                sourceData = await sourceConn.QueryAsync<WorkItem>(selectQuery);
                sourceData = sourceData.Where(x => x.ERP_ID != null);
            }

            // Step 4: Upsert work items into target
            var insertedIds = new List<long?>();
            var updatedIds = new List<long?>();
            var skippedIds = new List<long?>();

            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();
                const string mergeWorkItemSql = @"
                    MERGE dbo.WorkItems AS target
                    USING (
                        SELECT
                            @ERP_ID            AS ERP_ID,
                            @CategoryID        AS CategoryID,
                            @Number            AS Number,
                            @Name              AS Name,
                            @WorkItemParentID  AS WorkItemParentID,
                            @IsActive          AS IsActive,
                            @CreatedOn         AS CreatedOn,
                            @CreatedBy         AS CreatedBy,
                            @ModifiedOn        AS ModifiedOn,
                            @ModifiedBy        AS ModifiedBy,
                            @DeletedOn         AS DeletedOn,
                            @DeletedBy         AS DeletedBy,
                            @IsDeleted         AS IsDeleted
                    ) AS source
                    ON target.ERP_ID = source.ERP_ID
                    WHEN MATCHED AND (
                           ISNULL(target.CategoryID, '') <> ISNULL(source.CategoryID, '')
                        OR ISNULL(target.Number, '') <> ISNULL(source.Number, '')
                        OR ISNULL(target.Name, '') <> ISNULL(source.Name, '')
                        OR ISNULL(target.WorkItemParentID, 0) <> ISNULL(source.WorkItemParentID, 0)
                        OR ISNULL(target.IsActive, 0) <> ISNULL(source.IsActive, 0)
                        OR ISNULL(target.ModifiedOn, '1900-01-01') <> ISNULL(source.ModifiedOn, '1900-01-01')
                        OR ISNULL(target.IsDeleted, 0) <> ISNULL(source.IsDeleted, 0)
                    )
                    THEN UPDATE SET
                        CategoryID       = source.CategoryID,
                        Number           = source.Number,
                        Name             = source.Name,
                        WorkItemParentID = source.WorkItemParentID,
                        IsActive         = source.IsActive,
                        ModifiedOn       = source.ModifiedOn,
                        ModifiedBy       = source.ModifiedBy,
                        DeletedOn        = source.DeletedOn,
                        DeletedBy        = source.DeletedBy,
                        IsDeleted        = source.IsDeleted
                    WHEN NOT MATCHED BY TARGET THEN
                        INSERT (
                            WorkItemID,
                            ERP_ID,
                            CategoryID,
                            Number,
                            Name,
                            WorkItemParentID,
                            IsActive,
                            CreatedOn,
                            CreatedBy,
                            ModifiedOn,
                            ModifiedBy,
                            DeletedOn,
                            DeletedBy,
                            IsDeleted
                        )
                        VALUES (
                            NEWID(),
                            source.ERP_ID,
                            source.CategoryID,
                            source.Number,
                            source.Name,
                            source.WorkItemParentID,
                            source.IsActive,
                            source.CreatedOn,
                            source.CreatedBy,
                            source.ModifiedOn,
                            source.ModifiedBy,
                            source.DeletedOn,
                            source.DeletedBy,
                            source.IsDeleted
                        )
                    OUTPUT $action, source.ERP_ID;
                ";

                foreach (var item in sourceData)
                {
                    var result = await targetConn.QueryAsync<(string Action, long? ERP_ID)>(mergeWorkItemSql, item);
                    if (!result.Any())
                    {
                        skippedIds.Add(item.ERP_ID);
                    }
                    else
                    {
                        foreach (var r in result)
                        {
                            if (r.Action == "INSERT")
                                insertedIds.Add(r.ERP_ID);
                            else if (r.Action == "UPDATE")
                                updatedIds.Add(r.ERP_ID);
                        }
                    }
                }
            }

            // Step 5: Return results for API use
            return (insertedIds, updatedIds, skippedIds);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncWorkItemCategoryTypesAsync()
        {
            // Step 1: Read categories from source
            IEnumerable<WorkItemCategoryType> categories;
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();

                const string categoryQuery =
                    @"SELECT CategoryID, CategoryName 
              FROM dbo.WorkItemCategoryTypes";

                categories = await sourceConn.QueryAsync<WorkItemCategoryType>(categoryQuery);
            }

            var insertedIds = new List<long?>();
            var updatedIds = new List<long?>();
            var skippedIds = new List<long?>();

            // Step 2: Upsert into target
            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();

                const string upsertSql = @"
            MERGE dbo.WorkItemCategoryTypes AS target
            USING (
                SELECT 
                    @CategoryID   AS CategoryID,
                    @CategoryName AS CategoryName
            ) AS source
            ON target.CategoryID = source.CategoryID

            WHEN MATCHED AND
                 ISNULL(target.CategoryName, '') <> ISNULL(source.CategoryName, '')
            THEN
                UPDATE SET CategoryName = source.CategoryName

            WHEN NOT MATCHED BY TARGET THEN
                INSERT (CategoryID, CategoryName)
                VALUES (source.CategoryID, source.CategoryName)

            OUTPUT $action, source.CategoryID;
        ";

                foreach (var category in categories)
                {
                    var result = await targetConn
                        .QueryAsync<(string Action, long? CategoryID)>(upsertSql, category);

                    if (!result.Any())
                    {
                        skippedIds.Add(category.CategoryID);
                        continue;
                    }

                    foreach (var r in result)
                    {
                        if (r.Action == "INSERT")
                            insertedIds.Add(r.CategoryID);
                        else if (r.Action == "UPDATE")
                            updatedIds.Add(r.CategoryID);
                    }
                }
            }

            return (insertedIds, updatedIds, skippedIds);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncWorkItemsAsync()
        {
            // Step 1: Read work items from source
            IEnumerable<WorkItem> sourceData;
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();

                const string selectQuery = @"
            SELECT
                ERP_ID,
                CategoryID,
                Number,
                Name,
                WorkItemParentID,
                IsActive,
                CreatedOn,
                CreatedBy,
                ModifiedOn,
                ModifiedBy,
                DeletedOn,
                DeletedBy,
                IsDeleted
            FROM dbo.WorkItems";

                sourceData = await sourceConn.QueryAsync<WorkItem>(selectQuery);
                sourceData = sourceData.Where(x => x.ERP_ID != null);
            }

            var insertedIds = new List<long?>();
            var updatedIds = new List<long?>();
            var skippedIds = new List<long?>();

            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();

                const string mergeSql = @"
            MERGE dbo.WorkItems AS target
            USING (
                SELECT
                    @ERP_ID           AS ERP_ID,
                    @CategoryID       AS CategoryID,
                    @Number           AS Number,
                    @Name             AS Name,
                    @WorkItemParentID AS WorkItemParentID,
                    @IsActive         AS IsActive,
                    @CreatedOn        AS CreatedOn,
                    @CreatedBy        AS CreatedBy,
                    @ModifiedOn       AS ModifiedOn,
                    @ModifiedBy       AS ModifiedBy,
                    @DeletedOn        AS DeletedOn,
                    @DeletedBy        AS DeletedBy,
                    @IsDeleted        AS IsDeleted
            ) AS source
            ON target.ERP_ID = source.ERP_ID

            WHEN MATCHED AND (
                   ISNULL(target.CategoryID, '') <> ISNULL(source.CategoryID, '')
                OR ISNULL(target.Number, '') <> ISNULL(source.Number, '')
                OR ISNULL(target.Name, '') <> ISNULL(source.Name, '')
                OR ISNULL(target.WorkItemParentID, 0) <> ISNULL(source.WorkItemParentID, 0)
                OR ISNULL(target.IsActive, 0) <> ISNULL(source.IsActive, 0)
                OR ISNULL(target.ModifiedOn, '1900-01-01') <> ISNULL(source.ModifiedOn, '1900-01-01')
                OR ISNULL(target.IsDeleted, 0) <> ISNULL(source.IsDeleted, 0)
            )
            THEN UPDATE SET
                CategoryID       = source.CategoryID,
                Number           = source.Number,
                Name             = source.Name,
                WorkItemParentID = source.WorkItemParentID,
                IsActive         = source.IsActive,
                ModifiedOn       = source.ModifiedOn,
                ModifiedBy       = source.ModifiedBy,
                DeletedOn        = source.DeletedOn,
                DeletedBy        = source.DeletedBy,
                IsDeleted        = source.IsDeleted

            WHEN NOT MATCHED BY TARGET THEN
                INSERT (
                    WorkItemID,
                    ERP_ID,
                    CategoryID,
                    Number,
                    Name,
                    WorkItemParentID,
                    IsActive,
                    CreatedOn,
                    CreatedBy,
                    ModifiedOn,
                    ModifiedBy,
                    DeletedOn,
                    DeletedBy,
                    IsDeleted
                )
                VALUES (
                    NEWID(),
                    source.ERP_ID,
                    source.CategoryID,
                    source.Number,
                    source.Name,
                    source.WorkItemParentID,
                    source.IsActive,
                    source.CreatedOn,
                    source.CreatedBy,
                    source.ModifiedOn,
                    source.ModifiedBy,
                    source.DeletedOn,
                    source.DeletedBy,
                    source.IsDeleted
                )
            OUTPUT $action, source.ERP_ID;
        ";

                foreach (var item in sourceData)
                {
                    var result = await targetConn
                        .QueryAsync<(string Action, long? ERP_ID)>(mergeSql, item);

                    if (!result.Any())
                    {
                        skippedIds.Add(item.ERP_ID);
                        continue;
                    }

                    foreach (var r in result)
                    {
                        if (r.Action == "INSERT")
                            insertedIds.Add(r.ERP_ID);
                        else if (r.Action == "UPDATE")
                            updatedIds.Add(r.ERP_ID);
                    }
                }
            }

            return (insertedIds, updatedIds, skippedIds);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncCustomersAsync()
        {
            IEnumerable<Customer> customers;

            // Step 1: Read customers from source
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();
                const string sql = @"SELECT CustomerID, CustomerName FROM dbo.Customers";
                customers = await sourceConn.QueryAsync<Customer>(sql);
            }

            var inserted = new List<long?>();
            var updated = new List<long?>();
            var skipped = new List<long?>();

            // Step 2: Upsert into target
            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();

                const string mergeSql = @"
                    MERGE dbo.Customers AS target
                    USING (
                        SELECT 
                            @CustomerID   AS CustomerID,
                            @CustomerName AS CustomerName
                    ) AS source
                    ON target.CustomerID = source.CustomerID
                    WHEN MATCHED AND ISNULL(target.CustomerName, '') <> ISNULL(source.CustomerName, '')
                        THEN UPDATE SET CustomerName = source.CustomerName
                    WHEN NOT MATCHED BY TARGET
                        THEN INSERT (CustomerID, CustomerName)
                             VALUES (source.CustomerID, source.CustomerName)
                    OUTPUT $action, source.CustomerID;
                    ";

                foreach (var customer in customers)
                {
                    var result = await targetConn
                        .QueryAsync<(string Action, long CustomerID)>(mergeSql, customer);

                    if (!result.Any())
                    {
                        skipped.Add(customer.CustomerID);
                    }
                    else
                    {
                        foreach (var r in result)
                        {
                            if (r.Action == "INSERT") inserted.Add(r.CustomerID);
                            else if (r.Action == "UPDATE") updated.Add(r.CustomerID);
                        }
                    }
                }
            }

            return (inserted, updated, skipped);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncProjectsAsync()
        {
            IEnumerable<Project> projects;

            // Step 1: Read projects from source
            using (var sourceConn = new SqlConnection(_sourceConnection))
            {
                await sourceConn.OpenAsync();

                const string selectSql = @"
                    SELECT
                        ERP_ID,
                        Company,
                        Number,
                        Name,
                        CustomerID,                       
                        StartDate,
                        CompletionDate,
                        SharepointURL,
                        Status,
                        CreatedOn,
                        CreatedBy,
                        ModifiedOn,
                        ModifiedBy,
                        DeletedOn,
                        DeletedBy,
                        IsDeleted
                    FROM dbo.Projects
                    WHERE ERP_ID IS NOT NULL";

                projects = await sourceConn.QueryAsync<Project>(selectSql);
            }

            var inserted = new List<long?>();
            var updated = new List<long?>();
            var skipped = new List<long?>();

            // Step 2: Upsert into target
            using (var targetConn = new SqlConnection(_targetConnection))
            {
                await targetConn.OpenAsync();

                const string mergeSql = @"
                        MERGE dbo.Projects AS target
                        USING (
                            SELECT
                                @ERP_ID             AS ERP_ID,
                                @Company            AS Company,
                                @Number             AS Number,
                                @Name               AS Name,
                                @CustomerID         AS CustomerID,
                                @StartDate          AS StartDate,
                                @CompletionDate     AS CompletionDate,
                                @SharepointURL      AS SharepointURL,
                                @Status             AS Status,
                                @CreatedOn          AS CreatedOn,
                                @CreatedBy          AS CreatedBy,
                                @ModifiedOn         AS ModifiedOn,
                                @ModifiedBy         AS ModifiedBy,
                                @DeletedOn          AS DeletedOn,
                                @DeletedBy          AS DeletedBy,
                                @IsDeleted          AS IsDeleted
                        ) AS source
                        ON target.ERP_ID = source.ERP_ID
                        WHEN MATCHED AND (
                               ISNULL(target.Name, '') <> ISNULL(source.Name, '')
                            OR ISNULL(target.Status, '') <> ISNULL(source.Status, '')
                            OR ISNULL(target.CustomerID, 0) <> ISNULL(source.CustomerID, 0)
                            OR ISNULL(target.ModifiedOn, '1900-01-01') <> ISNULL(source.ModifiedOn, '1900-01-01')
                            OR ISNULL(target.IsDeleted, 0) <> ISNULL(source.IsDeleted, 0)
                        )
                        THEN UPDATE SET
                            Company          = source.Company,
                            Number           = source.Number,
                            Name             = source.Name,
                            CustomerID       = source.CustomerID,
                            StartDate        = source.StartDate,
                            CompletionDate   = source.CompletionDate,
                            SharepointURL    = source.SharepointURL,
                            Status           = source.Status,
                            ModifiedOn       = source.ModifiedOn,
                            ModifiedBy       = source.ModifiedBy,
                            DeletedOn        = source.DeletedOn,
                            DeletedBy        = source.DeletedBy,
                            IsDeleted        = source.IsDeleted
                        WHEN NOT MATCHED BY TARGET
                        THEN INSERT (
                            ProjectID,
                            ERP_ID,
                            Company,
                            Number,
                            Name,
                            CustomerID,
                            StartDate,
                            CompletionDate,
                            SharepointURL,
                            Status,
                            CreatedOn,
                            CreatedBy,
                            ModifiedOn,
                            ModifiedBy,
                            DeletedOn,
                            DeletedBy,
                            IsDeleted
                        )
                        VALUES (
                            NEWID(),
                            source.ERP_ID,
                            source.Company,
                            source.Number,
                            source.Name,
                            source.CustomerID,
                            source.StartDate,
                            source.CompletionDate,
                            source.SharepointURL,
                            source.Status,
                            source.CreatedOn,
                            source.CreatedBy,
                            source.ModifiedOn,
                            source.ModifiedBy,
                            source.DeletedOn,
                            source.DeletedBy,
                            source.IsDeleted
                        )
                        OUTPUT $action, source.ERP_ID;
                        ";

                foreach (var project in projects)
                {
                    var result = await targetConn
                        .QueryAsync<(string Action, int? ERP_ID)>(mergeSql, project);

                    if (!result.Any())
                    {
                        skipped.Add(project.ERP_ID);
                    }
                    else
                    {
                        foreach (var r in result)
                        {
                            if (r.Action == "INSERT") inserted.Add(r.ERP_ID);
                            else if (r.Action == "UPDATE") updated.Add(r.ERP_ID);
                        }
                    }
                }
            }

            return (inserted, updated, skipped);
        }

        public async Task<List<Subcontractor>> GetMissingSubcontractorDetailsAsync()
        {
            IEnumerable<Subcontractor> unibouwSubcontractors;
            IEnumerable<Guid> dwhSubcontractorIds;

            // Step 1: Fetch all subcontractor details from unibouw DB
            using (var unibouwConn = new SqlConnection(_targetConnection)) // unibouw DB
            {
                await unibouwConn.OpenAsync();
                const string sql = "SELECT * FROM dbo.Subcontractors";
                unibouwSubcontractors = await unibouwConn.QueryAsync<Subcontractor>(sql);
            }

            // Step 2: Fetch SubcontractorID from dwh DB
            using (var dwhConn = new SqlConnection(_sourceConnection)) // DWH DB
            {
                await dwhConn.OpenAsync();
                const string sql = "SELECT SubcontractorID FROM dbo.Subcontractors";
                dwhSubcontractorIds = await dwhConn.QueryAsync<Guid>(sql);
            }

            // Step 3: Find subcontractors in unibouw DB but not in DWH DB
            var missingSubcontractors = unibouwSubcontractors
                .Where(x => !dwhSubcontractorIds.Contains(x.SubcontractorID))
                .ToList();

            // Step 4: Print to console
            Console.WriteLine("Subcontractor details in unibouw DB but not in DWH DB:");
            foreach (var subcontractor in missingSubcontractors)
            {
                Console.WriteLine($"{subcontractor.SubcontractorID} - {subcontractor.Name}");
            }

            return missingSubcontractors;
        }
    }
}
