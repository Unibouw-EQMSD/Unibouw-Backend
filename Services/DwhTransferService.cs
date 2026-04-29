using Dapper;
using Microsoft.Data.SqlClient;
using UnibouwAPI.Models;

namespace UnibouwAPI.Services
{
    public class DwhTransferService
    {
        private readonly string _dwhConnection;
        private readonly string _unibouwConnection;
        private readonly ISharePointQuoteStorage _sp;


        public DwhTransferService(IConfiguration config, ISharePointQuoteStorage sp)
        {
            _dwhConnection = config.GetConnectionString("DWHDb");
            _unibouwConnection = config.GetConnectionString("UnibouwDbConnection");
            _sp = sp;

        }

        public async Task<(
         (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Categories,
         (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) WorkItems,
         (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Customers,
         (List<long?> Inserted, List<long?> Updated, List<long?> Skipped) Projects,
         (List<Guid> Inserted, List<Guid> Updated, List<Guid> Skipped) Subcontractors,
         (List<(long, long)> Inserted, List<(long, long)> Updated, List<(long, long)> Skipped) SubcontractorWorkItems
     )> SyncAllAsync()
        {
            await SyncPersonsAsync(); // (if referenced)
            var categories = await SyncWorkItemCategoryTypesAsync();
            var workItems = await SyncWorkItemsAsync();
            var customers = await SyncCustomersAsync();
            var projects = await SyncProjectsAsync();
            var subcontractors = await SyncSubcontractorsAsync();
            var subcontractorWorkItems = await SyncSubcontractorWorkItemsMappingAsync();
            return (categories, workItems, customers, projects, subcontractors, subcontractorWorkItems);
        }
        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> TransferDataAsync1()
        {
            // Step 1: Read all categories from source
            IEnumerable<WorkItemCategoryType> categories;
            using (var sourceConn = new SqlConnection(_dwhConnection))
            {
                await sourceConn.OpenAsync();
                string categoryQuery = @"SELECT CategoryID, CategoryName FROM dbo.WorkItemCategoryTypes";
                categories = await sourceConn.QueryAsync<WorkItemCategoryType>(categoryQuery);
            }

            // Step 2: Upsert categories into target
            using (var targetConn = new SqlConnection(_unibouwConnection))
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
            using (var sourceConn = new SqlConnection(_dwhConnection))
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

            using (var targetConn = new SqlConnection(_unibouwConnection))
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
            using (var sourceConn = new SqlConnection(_dwhConnection))
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
            using (var targetConn = new SqlConnection(_unibouwConnection))
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
            IEnumerable<WorkItem> sourceData;
            using (var sourceConn = new SqlConnection(_dwhConnection))
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
            FROM dbo.WorkItems
            WHERE ERP_ID IS NOT NULL;";
                sourceData = await sourceConn.QueryAsync<WorkItem>(selectQuery);
                Console.WriteLine($"[DWH SYNC] Source DWH WorkItems: {sourceData.Count()}");
            }

            var insertedIds = new List<long?>();
            var updatedIds = new List<long?>();
            var skippedIds = new List<long?>();

            using (var targetConn = new SqlConnection(_unibouwConnection))
            {
                await targetConn.OpenAsync();

                const string mergeSql = @"
MERGE dbo.WorkItems AS target
USING (
    SELECT
        @ERP_ID AS ERP_ID,
        @CategoryID AS CategoryID,
        @Number AS Number,
        @Name AS Name,
        @WorkItemParentID AS WorkItemParentID,
        @IsActive AS IsActive,
        @CreatedOn AS CreatedOn,
        @CreatedBy AS CreatedBy,
        @ModifiedOn AS ModifiedOn,
        @ModifiedBy AS ModifiedBy,
        @DeletedOn AS DeletedOn,
        @DeletedBy AS DeletedBy,
        @IsDeleted AS IsDeleted
) AS source
ON target.ERP_ID = source.ERP_ID
WHEN MATCHED THEN
    UPDATE SET
        CategoryID = source.CategoryID,
        Number = source.Number,
        Name = source.Name,
        WorkItemParentID = source.WorkItemParentID,
        IsActive = source.IsActive,
        CreatedOn = source.CreatedOn,
        CreatedBy = source.CreatedBy,
        ModifiedOn = source.ModifiedOn,
        ModifiedBy = source.ModifiedBy,
        DeletedOn = source.DeletedOn,
        DeletedBy = source.DeletedBy,
        IsDeleted = source.IsDeleted
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        WorkItemID, ERP_ID, CategoryID, Number, Name, WorkItemParentID,
        IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted
    )
    VALUES (
        NEWID(), source.ERP_ID, source.CategoryID, source.Number, source.Name, source.WorkItemParentID,
        source.IsActive, source.CreatedOn, source.CreatedBy, source.ModifiedOn, source.ModifiedBy,
        source.DeletedOn, source.DeletedBy, source.IsDeleted
    )
OUTPUT $action, source.ERP_ID;
";
                foreach (var item in sourceData)
                {
                    try
                    {
                        var result = await targetConn.QueryAsync<(string Action, long? ERP_ID)>(mergeSql, item);
                        if (!result.Any())
                        {
                            skippedIds.Add(item.ERP_ID);
                            continue;
                        }
                        foreach (var r in result)
                        {
                            if (r.Action == "INSERT") insertedIds.Add(r.ERP_ID);
                            else if (r.Action == "UPDATE") updatedIds.Add(r.ERP_ID);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DWH SYNC] Error upserting WorkItem (ERP_ID={item.ERP_ID}): {ex.Message}");
                        skippedIds.Add(item.ERP_ID);
                    }
                }
                Console.WriteLine($"[DWH SYNC] WorkItems Inserted: {insertedIds.Count}, Updated: {updatedIds.Count}, Skipped: {skippedIds.Count}");
            }
            return (insertedIds, updatedIds, skippedIds);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncCustomersAsync()
        {
            IEnumerable<Customer> customers;

            // Step 1: Read customers from source
            using (var sourceConn = new SqlConnection(_dwhConnection))
            {
                await sourceConn.OpenAsync();
                const string sql = @"SELECT CustomerID, CustomerName FROM dbo.Customers";
                customers = await sourceConn.QueryAsync<Customer>(sql);
            }

            var inserted = new List<long?>();
            var updated = new List<long?>();
            var skipped = new List<long?>();

            // Step 2: Upsert into target
            using (var targetConn = new SqlConnection(_unibouwConnection))
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


        public async Task<(List<Guid> Inserted, List<Guid> Updated, List<Guid> Skipped)> SyncSubcontractorsAsync()
        {
            IEnumerable<Subcontractor> sourceData;
            using (var sourceConn = new SqlConnection(_dwhConnection))
            {
                await sourceConn.OpenAsync();
                const string selectSql = @"
            SELECT
                SubcontractorID, ERP_ID, Name, Rating, Email, Location, Country, OfficeAddress, BillingAddress,
                RegisteredDate, IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted, PersonID
            FROM dbo.Subcontractors
            WHERE ERP_ID IS NOT NULL;";
                sourceData = await sourceConn.QueryAsync<Subcontractor>(selectSql);
                Console.WriteLine($"[DWH SYNC] Source DWH Subcontractors: {sourceData.Count()}");
            }

            var inserted = new List<Guid>();
            var updated = new List<Guid>();
            var skipped = new List<Guid>();

            using (var targetConn = new SqlConnection(_unibouwConnection))
            {
                await targetConn.OpenAsync();
                const string mergeSql = @"
MERGE dbo.Subcontractors AS target
USING (
    SELECT
        @ERP_ID AS ERP_ID,
        @SubcontractorID AS SubcontractorID,
        @Name AS Name,
        @Rating AS Rating,
        @Email AS Email,
        @Location AS Location,
        @Country AS Country,
        @OfficeAddress AS OfficeAddress,
        @BillingAddress AS BillingAddress,
        @RegisteredDate AS RegisteredDate,
        @IsActive AS IsActive,
        @CreatedOn AS CreatedOn,
        @CreatedBy AS CreatedBy,
        @ModifiedOn AS ModifiedOn,
        @ModifiedBy AS ModifiedBy,
        @DeletedOn AS DeletedOn,
        @DeletedBy AS DeletedBy,
        @IsDeleted AS IsDeleted,
        @PersonID AS PersonID
) AS source
ON target.ERP_ID = source.ERP_ID
WHEN MATCHED THEN
    UPDATE SET
        SubcontractorID = source.SubcontractorID,
        Name = source.Name,
        Rating = source.Rating,
        Email = source.Email,
        Location = source.Location,
        Country = source.Country,
        OfficeAddress = source.OfficeAddress,
        BillingAddress = source.BillingAddress,
        RegisteredDate = source.RegisteredDate,
        IsActive = source.IsActive,
        CreatedOn = source.CreatedOn,
        CreatedBy = source.CreatedBy,
        ModifiedOn = source.ModifiedOn,
        ModifiedBy = source.ModifiedBy,
        DeletedOn = source.DeletedOn,
        DeletedBy = source.DeletedBy,
        IsDeleted = source.IsDeleted,
        PersonID = source.PersonID
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        ERP_ID, SubcontractorID, Name, Rating, Email, Location, Country,
        OfficeAddress, BillingAddress, RegisteredDate, IsActive, CreatedOn,
        CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted, PersonID
    )
    VALUES (
        source.ERP_ID, source.SubcontractorID, source.Name, source.Rating, source.Email,
        source.Location, source.Country, source.OfficeAddress, source.BillingAddress,
        source.RegisteredDate, source.IsActive, source.CreatedOn, source.CreatedBy,
        source.ModifiedOn, source.ModifiedBy, source.DeletedOn, source.DeletedBy, source.IsDeleted, source.PersonID
    )
OUTPUT $action, source.SubcontractorID;
";
                foreach (var sub in sourceData)
                {
                    try
                    {
                        var result = await targetConn.QueryAsync<(string Action, Guid SubcontractorID)>(mergeSql, sub);
                        if (!result.Any())
                        {
                            skipped.Add(sub.SubcontractorID);
                            continue;
                        }
                        foreach (var r in result)
                        {
                            if (r.Action == "INSERT") inserted.Add(r.SubcontractorID);
                            else if (r.Action == "UPDATE") updated.Add(r.SubcontractorID);
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped.Add(sub.SubcontractorID);
                    }
                }
                Console.WriteLine($"[DWH SYNC] Subcontractors Inserted: {inserted.Count}, Updated: {updated.Count}, Skipped: {skipped.Count}");
            }
            return (inserted, updated, skipped);
        }
        public async Task<(List<long> Inserted, List<long> Updated, List<long> Skipped)> SyncPersonsAsync()
        {
            IEnumerable<Person> sourceData;
            using (var sourceConn = new SqlConnection(_dwhConnection))
            {
                await sourceConn.OpenAsync();
                const string selectSql = @"
            SELECT
                PersonID,
                Name,
                Email,
                PhoneNumber1,
                PhoneNumber2,
                CreatedOn,
                CreatedBy,
                ModifiedOn,
                ModifiedBy,
                DeletedOn,
                DeletedBy,
                IsDeleted
            FROM dbo.Persons
            WHERE PersonID IS NOT NULL;";
                sourceData = await sourceConn.QueryAsync<Person>(selectSql);
            }

            var inserted = new List<long>();
            var updated = new List<long>();
            var skipped = new List<long>();

            using (var targetConn = new SqlConnection(_unibouwConnection))
            {
                await targetConn.OpenAsync();

                // Enable explicit PersonID insert
                await targetConn.ExecuteAsync("SET IDENTITY_INSERT dbo.Persons ON");

                const string mergeSql = @"
MERGE dbo.Persons AS target
USING (
    SELECT
        @PersonID AS PersonID,
        @Name AS Name,
        @Email AS Email,
        @PhoneNumber1 AS PhoneNumber1,
        @PhoneNumber2 AS PhoneNumber2,
        @CreatedOn AS CreatedOn,
        @CreatedBy AS CreatedBy,
        @ModifiedOn AS ModifiedOn,
        @ModifiedBy AS ModifiedBy,
        @DeletedOn AS DeletedOn,
        @DeletedBy AS DeletedBy,
        @IsDeleted AS IsDeleted
) AS source
ON target.PersonID = source.PersonID
WHEN MATCHED AND (
    ISNULL(target.Name,'') <> ISNULL(source.Name,'')
    OR ISNULL(target.Email,'') <> ISNULL(source.Email,'')
    OR ISNULL(target.PhoneNumber1,'') <> ISNULL(source.PhoneNumber1,'')
    OR ISNULL(target.PhoneNumber2,'') <> ISNULL(source.PhoneNumber2,'')
    OR ISNULL(target.IsDeleted, 0) <> ISNULL(source.IsDeleted, 0)
)
THEN UPDATE SET
    Name = source.Name,
    Email = source.Email,
    PhoneNumber1 = source.PhoneNumber1,
    PhoneNumber2 = source.PhoneNumber2,
    ModifiedOn = source.ModifiedOn,
    ModifiedBy = source.ModifiedBy,
    DeletedOn = source.DeletedOn,
    DeletedBy = source.DeletedBy,
    IsDeleted = source.IsDeleted
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        PersonID, Name, Email, PhoneNumber1, PhoneNumber2, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted
    )
    VALUES (
        source.PersonID, source.Name, source.Email, source.PhoneNumber1, source.PhoneNumber2, source.CreatedOn, source.CreatedBy, source.ModifiedOn, source.ModifiedBy, source.DeletedOn, source.DeletedBy, source.IsDeleted
    )
OUTPUT $action, source.PersonID;";

                foreach (var person in sourceData)
                {
                    var result = await targetConn.QueryAsync<(string Action, long PersonID)>(mergeSql, person);
                    if (!result.Any())
                    {
                        if (person.PersonID.HasValue)
                            skipped.Add(person.PersonID.Value);
                        continue;
                    }
                    foreach (var r in result)
                    {
                        if (r.Action == "INSERT") inserted.Add(r.PersonID);
                        else if (r.Action == "UPDATE") updated.Add(r.PersonID);
                    }
                }

                // Disable explicit PersonID insert
                await targetConn.ExecuteAsync("SET IDENTITY_INSERT dbo.Persons OFF");
            }
            return (inserted, updated, skipped);
        }

        public async Task<(List<long?> Inserted, List<long?> Updated, List<long?> Skipped)> SyncProjectsAsync()
        {
            IEnumerable<Project> projects;

            // Step 1: Read projects from source
            using (var sourceConn = new SqlConnection(_dwhConnection))
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
            using (var targetConn = new SqlConnection(_unibouwConnection))
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
                    var result = await targetConn.QueryAsync<(string Action, int? ERP_ID)>(mergeSql, project);

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

                    // ✅ Ensure SharePoint folder after sync when link exists
                    if (!string.IsNullOrWhiteSpace(project.SharepointURL))
                    {
                        var projectFolderName = $"{project.Number}_{project.Name}";
                        try
                        {
                            await _sp.EnsureProjectFolderAsync(project.SharepointURL, projectFolderName);
                        }
                        catch (Exception ex)
                        {
                        }
                    }
                }
            }

            return (inserted, updated, skipped);
        }

        public async Task<List<Subcontractor>> GetMissingSubcontractorDetailsAsync1()
        {
            IEnumerable<Subcontractor> unibouwSubcontractors;
            IEnumerable<Guid> dwhSubcontractorIds;

            // Step 1: Fetch all subcontractor details from unibouw DB
            using (var unibouwConn = new SqlConnection(_unibouwConnection)) // unibouw DB
            {
                await unibouwConn.OpenAsync();
                const string sql = "SELECT * FROM dbo.Subcontractors";
                unibouwSubcontractors = await unibouwConn.QueryAsync<Subcontractor>(sql);
            }

            // Step 2: Fetch SubcontractorID from dwh DB
            using (var dwhConn = new SqlConnection(_dwhConnection)) // DWH DB
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
       
        public async Task<(
     List<(long SubcontractorERP_ID, long WorkItemERP_ID)> Inserted,
     List<(long SubcontractorERP_ID, long WorkItemERP_ID)> Updated,
     List<(long SubcontractorERP_ID, long WorkItemERP_ID)> Skipped
 )> SyncSubcontractorWorkItemsMappingAsync()
        {
            // Step 1: Read all mappings from DWH
            IEnumerable<(long SubcontractorERP_ID, long WorkItemERP_ID, DateTime? CreatedOn, string CreatedBy)> sourceData;

            using (var sourceConn = new SqlConnection(_dwhConnection))
            {
                await sourceConn.OpenAsync();

                const string selectQuery = @"
            SELECT
                SubcontractorID AS SubcontractorERP_ID,
                WorkItemID AS WorkItemERP_ID,
                CreatedOn,
                CreatedBy
            FROM dbo.SubcontractorWorkItemsMapping";

                sourceData = await sourceConn.QueryAsync<(long, long, DateTime?, string)>(selectQuery);

                Console.WriteLine($"[DWH SYNC] Source mapping rows from DWH: {sourceData.Count()}");
            }

            var inserted = new List<(long, long)>();
            var updated = new List<(long, long)>();
            var skipped = new List<(long, long)>();

            using (var targetConn = new SqlConnection(_unibouwConnection))
            {
                await targetConn.OpenAsync();

                // 🔥 NO FILTER — TAKE ALL DATA
                var allMappings = sourceData.ToList();
                Console.WriteLine($"[DWH SYNC] Processing ALL mappings: {allMappings.Count}");

                const string mergeSql = @"
MERGE dbo.SubcontractorWorkItemsMapping AS target
USING (
    SELECT
        @SubcontractorERP_ID AS SubcontractorERP_ID,
        @WorkItemERP_ID AS WorkItemERP_ID,
        @CreatedOn AS CreatedOn,
        @CreatedBy AS CreatedBy
) AS source
ON target.SubcontractorERP_ID = source.SubcontractorERP_ID 
   AND target.WorkItemERP_ID = source.WorkItemERP_ID

WHEN MATCHED THEN
    UPDATE SET 
        CreatedOn = source.CreatedOn, 
        CreatedBy = source.CreatedBy

WHEN NOT MATCHED BY TARGET THEN
    INSERT (SubcontractorERP_ID, WorkItemERP_ID, CreatedOn, CreatedBy)
    VALUES (source.SubcontractorERP_ID, source.WorkItemERP_ID, source.CreatedOn, source.CreatedBy)

OUTPUT $action, source.SubcontractorERP_ID, source.WorkItemERP_ID;
";

                int processed = 0;

                foreach (var item in allMappings)
                {
                    try
                    {
                        var result = await targetConn.QueryAsync<(string Action, long SubcontractorERP_ID, long WorkItemERP_ID)>(
                            mergeSql,
                            new
                            {
                                SubcontractorERP_ID = item.SubcontractorERP_ID,
                                WorkItemERP_ID = item.WorkItemERP_ID,
                                CreatedOn = item.CreatedOn,
                                CreatedBy = item.CreatedBy
                            });

                        if (!result.Any())
                        {
                            skipped.Add((item.SubcontractorERP_ID, item.WorkItemERP_ID));
                        }
                        else
                        {
                            foreach (var r in result)
                            {
                                if (r.Action == "INSERT")
                                    inserted.Add((r.SubcontractorERP_ID, r.WorkItemERP_ID));
                                else if (r.Action == "UPDATE")
                                    updated.Add((r.SubcontractorERP_ID, r.WorkItemERP_ID));
                            }
                        }

                        processed++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DWH SYNC] Error mapping ({item.SubcontractorERP_ID}, {item.WorkItemERP_ID}): {ex.Message}");
                        skipped.Add((item.SubcontractorERP_ID, item.WorkItemERP_ID));
                    }
                }

                Console.WriteLine($"[DWH SYNC] Processed: {processed}");
                Console.WriteLine($"[DWH SYNC] Inserted: {inserted.Count}, Updated: {updated.Count}, Skipped: {skipped.Count}");

                // 🔁 Step 2: Update GUID references (linking phase)
                try
                {
                    var updatedSubs = await targetConn.ExecuteAsync(@"
UPDATE swi
SET swi.SubcontractorID = s.SubcontractorID
FROM dbo.SubcontractorWorkItemsMapping swi
INNER JOIN dbo.Subcontractors s 
    ON swi.SubcontractorERP_ID = s.ERP_ID
WHERE swi.SubcontractorID IS NULL OR swi.SubcontractorID <> s.SubcontractorID;
");

                    Console.WriteLine($"[DWH SYNC] Updated Subcontractor GUIDs: {updatedSubs}");

                    var updatedWorks = await targetConn.ExecuteAsync(@"
UPDATE swi
SET swi.WorkItemID = w.WorkItemID
FROM dbo.SubcontractorWorkItemsMapping swi
INNER JOIN dbo.WorkItems w 
    ON swi.WorkItemERP_ID = w.ERP_ID
WHERE swi.WorkItemID IS NULL OR swi.WorkItemID <> w.WorkItemID;
");

                    Console.WriteLine($"[DWH SYNC] Updated WorkItem GUIDs: {updatedWorks}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DWH SYNC] Error updating GUID references: {ex.Message}");
                }

                // 🔍 Debug: Check unresolved mappings
                var nullCount = await targetConn.ExecuteScalarAsync<int>(@"
SELECT COUNT(*) 
FROM dbo.SubcontractorWorkItemsMapping
WHERE SubcontractorID IS NULL OR WorkItemID IS NULL");

                Console.WriteLine($"[DWH SYNC] Unresolved mappings (NULL GUIDs): {nullCount}");
            }

            return (inserted, updated, skipped);
        }

        public async Task<MissingSubcontractorResponseDto> GetMissingSubcontractorDetailsAsync()
        {
            List<Subcontractor> unibouwSubcontractors;
            HashSet<Guid> dwhSubcontractorIds;

            using var unibouwConn = new SqlConnection(_unibouwConnection);
            using var dwhConn = new SqlConnection(_dwhConnection);

            await unibouwConn.OpenAsync();
            await dwhConn.OpenAsync();

            // Step 1: Fetch all subcontractor details from Unibouw DB
            const string unibouwSql = @"
                 SELECT 
                     s.*, 
                     p.Name AS ContactName,
                     p.Email AS ContactEmail,
                     p.PhoneNumber1 AS ContactPhone
                 FROM Subcontractors s
                 LEFT JOIN Persons p ON s.PersonID = p.PersonID
                 WHERE s.IsDeleted = 0";
            unibouwSubcontractors = (await unibouwConn.QueryAsync<Subcontractor>(unibouwSql)).ToList();

            // Step 2: Fetch SubcontractorID from DWH DB
            const string dwhSql = "SELECT SubcontractorID FROM dbo.Subcontractors";
            dwhSubcontractorIds = (await dwhConn.QueryAsync<Guid>(dwhSql)).ToHashSet();

            // Step 3: Find missing subcontractors
            var missingSubcontractors = unibouwSubcontractors
                .Where(x => !dwhSubcontractorIds.Contains(x.SubcontractorID))
                .ToList();

            if (!missingSubcontractors.Any())
            {
                return new MissingSubcontractorResponseDto
                {
                    MissingSubcontractorsDetails = new List<Subcontractor>(),
                    RfqDetails = new List<Rfq>(),
                    QuoteDetails = new List<RfqSubcontractorResponse>()
                };
            }

            // Step 4: Get WorkItems Mapping for missing subcontractors
            var subcontractorIds = missingSubcontractors.Select(x => x.SubcontractorID).ToList();

            // Fetch mapping of SubcontractorID ↔ WorkItemID and Name
            var workItemMappings = (await unibouwConn.QueryAsync<(Guid SubcontractorID, Guid WorkItemID, string WorkItemName)>(
                        @"SELECT sw.SubcontractorID, sw.WorkItemID, wi.Name AS WorkItemName
              FROM dbo.SubcontractorWorkItemsMapping sw
              INNER JOIN dbo.WorkItems wi ON sw.WorkItemID = wi.WorkItemID
              WHERE sw.SubcontractorID IN @SubcontractorIDs",
                        new { SubcontractorIDs = subcontractorIds }
                    )).ToList();

            // Build dictionary: SubcontractorID → List of WorkItemIDs/Names
            var workItemDict = workItemMappings
                .GroupBy(x => x.SubcontractorID)
                .ToDictionary(
                    g => g.Key,
                    g => new {
                        WorkItemIDs = g.Select(x => x.WorkItemID).ToList(),
                        WorkItemNames = g.Select(x => x.WorkItemName).ToList()
                    }
                );

            // Assign WorkItemIDs and WorkItemNames to each missing subcontractor
            foreach (var sub in missingSubcontractors)
            {
                if (workItemDict.TryGetValue(sub.SubcontractorID, out var wi))
                {
                    sub.WorkItemIDs = wi.WorkItemIDs;
                    sub.WorkItemName = wi.WorkItemNames;
                }
                else
                {
                    sub.WorkItemIDs = new List<Guid>();
                    sub.WorkItemName = new List<string>();
                }
            }

            // Step 5: Get RFQ IDs from mapping table
            var rfqMappings = await unibouwConn.QueryAsync<(Guid SubcontractorID, Guid RfqID)>(
                @"SELECT SubcontractorID, RfqID
                  FROM dbo.RfqSubcontractorMapping
                  WHERE SubcontractorID IN @SubcontractorIDs",
                new { SubcontractorIDs = subcontractorIds }
            );

            var rfqIds = rfqMappings.Select(x => x.RfqID).Distinct().ToList();

            if (!rfqIds.Any())
            {
                return new MissingSubcontractorResponseDto
                {
                    MissingSubcontractorsDetails = missingSubcontractors,
                    RfqDetails = new List<Rfq>(),
                    QuoteDetails = new List<RfqSubcontractorResponse>()
                };
            }

            // Step 5: Get RFQ details
           /* var rfqDetails = (await unibouwConn.QueryAsync<Rfq>(
                @"SELECT RfqID, RfqNumber, ProjectID
                  FROM dbo.Rfq
                  WHERE RfqID IN @RfqIDs",
                new { RfqIDs = rfqIds }
            )).ToList();*/

            var rfqDetails = (await unibouwConn.QueryAsync<Rfq>(
                @"SELECT r.RfqID,
                         r.RfqNumber,
                         r.ProjectID,
                         p.Number AS ProjectCode
                  FROM dbo.Rfq r
                  INNER JOIN dbo.Projects p ON r.ProjectID = p.ProjectID
                  WHERE r.RfqID IN @RfqIDs",
                new { RfqIDs = rfqIds }
            )).ToList();


            // Step 6: Get Project Names
            var projectIds = rfqDetails
                .Where(r => r.ProjectID.HasValue)
                .Select(r => r.ProjectID.Value)
                .Distinct()
                .ToList();

            var projects = await unibouwConn.QueryAsync<(Guid ProjectID, string Name)>(
                @"SELECT ProjectID, Name
                  FROM dbo.Projects
                  WHERE ProjectID IN @ProjectIDs",
                new { ProjectIDs = projectIds }
            );

            var projectDict = projects.ToDictionary(x => x.ProjectID, x => x.Name);

            foreach (var rfq in rfqDetails)
            {
                if (rfq.ProjectID.HasValue && projectDict.TryGetValue(rfq.ProjectID.Value, out var projectName))
                {
                    rfq.ProjectName = projectName;
                }
                else
                {
                    rfq.ProjectName = "N/A";
                }
            }

            // Step 7: Get Quote Details
            /* var quoteDetails = (await unibouwConn.QueryAsync<RfqSubcontractorResponse>(
                         @"SELECT * FROM dbo.RfqSubcontractorResponse WHERE RfqID IN @RfqIDs",
                 new { RfqIDs = rfqIds }
             )).ToList();*/

            var quoteDetails = (await unibouwConn.QueryAsync<RfqSubcontractorResponse>(
                @"SELECT rsr.*,
                         wi.Name AS WorkItemName
                  FROM dbo.RfqSubcontractorResponse rsr
                  INNER JOIN dbo.WorkItems wi ON rsr.WorkItemID = wi.WorkItemID
                  WHERE rsr.RfqID IN @RfqIDs",
                new { RfqIDs = rfqIds }
            )).ToList();


            // Step 8: Return structured response
            return new MissingSubcontractorResponseDto
            {
                MissingSubcontractorsDetails = missingSubcontractors,
                RfqDetails = rfqDetails,
                QuoteDetails = quoteDetails
            };
        }

    }
}
