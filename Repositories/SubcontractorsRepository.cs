using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class SubcontractorsRepository : ISubcontractors
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);


        public SubcontractorsRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<dynamic>> GetAllSubcontractor(bool onlyActive = false)
        {
            using var connection = new SqlConnection(_connectionString);

            var query = @"
SELECT 
    s.SubcontractorID,
    s.Name,
    s.Rating,
    s.Email,
    s.Location,
    s.Country,
    s.OfficeAddress,
    s.BillingAddress,
    s.RegisteredDate,
    s.IsActive,
    s.CreatedOn,
    s.CreatedBy,
    s.ModifiedOn,
    s.ModifiedBy,
    s.DeletedOn,
    s.DeletedBy,
    s.IsDeleted,
    s.RemindersSent,

    p.Name AS ContactName,
    p.Email AS ContactEmail,
    p.PhoneNumber1 AS ContactPhone,

    STRING_AGG(w.Name, ', ') AS WorkItemName

FROM Subcontractors s
LEFT JOIN Persons p 
    ON s.PersonID = p.PersonID

LEFT JOIN SubcontractorWorkItemsMapping m 
    ON m.SubcontractorID = s.SubcontractorID

LEFT JOIN WorkItems w 
    ON w.WorkItemID = m.WorkItemID

WHERE s.IsDeleted = 0
";

            if (onlyActive)
                query += " AND s.IsActive = 1 ";

            query += @"
GROUP BY 
    s.SubcontractorID, s.Name, s.Rating, s.Email, s.Location,
    s.Country, s.OfficeAddress, s.BillingAddress,
    s.RegisteredDate, s.IsActive, s.CreatedOn, s.CreatedBy,
    s.ModifiedOn, s.ModifiedBy, s.DeletedOn, s.DeletedBy,
    s.IsDeleted, s.RemindersSent,
    p.Name, p.Email, p.PhoneNumber1
";

            if (onlyActive)
                query += " AND s.IsActive = 1";

            var subcontractors = (await connection.QueryAsync(query)).ToList();

            foreach (var sub in subcontractors)
            {
                var workItems = await connection.QueryAsync<Guid>(@"
            SELECT w.WorkItemID
            FROM SubcontractorWorkItemsMapping m
            INNER JOIN WorkItems w ON w.WorkItemID = m.WorkItemID
            WHERE m.SubcontractorID = @Id",
                    new { Id = sub.SubcontractorID });

                var workItemNames = await connection.QueryAsync<string>(@"
            SELECT w.Name
            FROM SubcontractorWorkItemsMapping m
            INNER JOIN WorkItems w ON w.WorkItemID = m.WorkItemID
            WHERE m.SubcontractorID = @Id",
                    new { Id = sub.SubcontractorID });

                sub.WorkItemIDs = workItems.ToList();
                sub.WorkItemName = string.Join(", ", workItemNames);
            }

            return subcontractors;
        }
        public async Task<dynamic?> GetSubcontractorById(Guid id)
        {
            // 1. Basic subcontractor info + contact
            var query = @"
        SELECT 
            s.*, 
            p.Name AS ContactName,
            p.Email AS ContactEmail,
            p.PhoneNumber1 AS ContactPhone
        FROM Subcontractors s
        LEFT JOIN Persons p ON s.PersonID = p.PersonID
        WHERE s.SubcontractorID = @Id AND s.IsDeleted = 0 AND s.IsActive = 1;";
            var sub = await _connection.QueryFirstOrDefaultAsync<Subcontractor>(query, new { Id = id });
            if (sub == null) return null;

            // 2. Unibouw work items (CategoryID = 1)
            var unibouwWorkItems = await _connection.QueryAsync<string>(
                @"SELECT w.Name
          FROM SubcontractorWorkItemsMapping m
          INNER JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
          WHERE m.SubcontractorID = @Id AND w.CategoryID = 1",
                new { Id = id });

            // 3. Standard work items (CategoryID = 2, 'NL-SfB')
            var standardWorkItems = await _connection.QueryAsync<string>(
                @"SELECT w.Name
          FROM SubcontractorWorkItemsMapping m
          INNER JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
          WHERE m.SubcontractorID = @Id AND w.CategoryID = 2",
                new { Id = id });

            // 4. Return as an anonymous object (matching your Angular expectations)
            return new
            {
                sub.SubcontractorID,
                sub.Name,
                sub.Rating,
                sub.Email,
                sub.Location,
                sub.Country,
                sub.OfficeAddress,
                sub.BillingAddress,
                sub.RegisteredDate,
                sub.IsActive,
                sub.ContactName,
                sub.ContactEmail,
                sub.ContactPhone,
                unibouwWorkItems = unibouwWorkItems.ToList(),
                standardWorkItems = standardWorkItems.ToList()
            };
        }

        public async Task<int> UpdateSubcontractorIsActive(Guid id, bool isActive, string modifiedBy)
        {
            // Check if subcontractor is non-deleted subcontractor
            var subcontractor = await _connection.QueryFirstOrDefaultAsync<WorkItem>(
                "SELECT * FROM Subcontractors WHERE SubcontractorID = @Id AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found (either inactive or deleted)
            if (subcontractor == null)
                return 0;

            var query = @"
        UPDATE Subcontractors SET
            IsActive = @IsActive,
            ModifiedOn = @ModifiedOn,
            ModifiedBy = @ModifiedBy
        WHERE SubcontractorID = @Id";

            var parameters = new
            {
                Id = id,
                IsActive = isActive,
                ModifiedOn = amsterdamNow,
                ModifiedBy = modifiedBy
            };

            return await _connection.ExecuteAsync(query, parameters);
        }

        public async Task<bool> CreateSubcontractorWithMappings(Subcontractor subcontractor, string language)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {

                        static string T(string lang, string key) => (lang?.ToLowerInvariant(), key) switch
                        {
                            ("nl", "SubExistsByEmail") => "Er bestaat al een onderaannemer met dit e-mailadres.",
                            ("nl", "SubExistsByName") => "Er bestaat al een onderaannemer met deze naam.",
                            (_, "SubExistsByEmail") => "A subcontractor with this email address already exists.",
                            (_, "SubExistsByName") => "A subcontractor with this name already exists.",
                            _ => "Validation error."
                        };
                        // STEP 1: DUPLICATE CHECK (EMAIL)
                        var existingEmailCount = await connection.ExecuteScalarAsync<int>(
                            @"SELECT COUNT(1) FROM Subcontractors 
                      WHERE LOWER(Email) = LOWER(@Email) AND IsDeleted = 0",
                            new { subcontractor.Email }, transaction);

                        if (existingEmailCount > 0)
                            throw new InvalidOperationException(T(language, "SubExistsByEmail"));

                        // STEP 2: DUPLICATE CHECK (NAME)
                        var existingNameCount = await connection.ExecuteScalarAsync<int>(
                            @"SELECT COUNT(1) FROM Subcontractors 
                      WHERE LOWER(Name) = LOWER(@Name) AND IsDeleted = 0",
                            new { subcontractor.Name }, transaction);

                        

                        // usage
                        if (existingNameCount > 0)
                            throw new InvalidOperationException(T(language, "SubExistsByName"));

                        // STEP 3: INSERT PERSON
                        var person = new
                        {
                            Name = subcontractor.ContactName,
                            Email = subcontractor.ContactEmail,
                            PhoneNumber1 = subcontractor.ContactPhone,
                            PhoneNumber2 = "",
                            CreatedOn = amsterdamNow,
                            CreatedBy = subcontractor.CreatedBy,
                            ModifiedOn = (DateTime?)null,
                            ModifiedBy = (string?)null,
                            DeletedOn = (DateTime?)null,
                            DeletedBy = (string?)null,
                            IsDeleted = false
                        };

                        long personId = await connection.ExecuteScalarAsync<long>(
                            @"INSERT INTO Persons
                      (Name, Email, PhoneNumber1, PhoneNumber2, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
                      VALUES
                      (@Name, @Email, @PhoneNumber1, @PhoneNumber2, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted);
                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                            person, transaction);

                        subcontractor.PersonID = personId;

                        // STEP 4: INSERT SUBCONTRACTOR + GET ERP_ID
                        subcontractor.SubcontractorID = Guid.NewGuid();
                        subcontractor.CreatedOn = amsterdamNow;
                        subcontractor.IsActive ??= true;
                        subcontractor.IsDeleted = false;

                        long subcontractorErpId = await connection.ExecuteScalarAsync<long>(
                            @"INSERT INTO Subcontractors 
                      (SubcontractorID, Name, Rating, Email, Location, Country, OfficeAddress, BillingAddress, RegisteredDate, PersonID,
                       IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted, RemindersSent)
                      VALUES 
                      (@SubcontractorID, @Name, @Rating, @Email, @Location, @Country, @OfficeAddress, @BillingAddress, @RegisteredDate, @PersonID,
                       @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted, @RemindersSent);

                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                            subcontractor, transaction);

                        // STEP 5: GET WORK ITEM ERP IDs
                        var workItemErpMap = (await connection.QueryAsync<(Guid WorkItemID, long ERP_ID)>(
                            @"SELECT WorkItemID, ERP_ID 
                      FROM WorkItems 
                      WHERE WorkItemID IN @Ids",
                            new { Ids = subcontractor.WorkItemIDs },
                            transaction))
                            .ToDictionary(x => x.WorkItemID, x => x.ERP_ID);

                        // STEP 6: INSERT MAPPINGS USING ERP IDs
                        if (subcontractor.WorkItemIDs != null && subcontractor.WorkItemIDs.Any())
                        {
                            string insertMappingQuery = @"
INSERT INTO SubcontractorWorkItemsMapping 
(SubcontractorERP_ID, WorkItemERP_ID, SubcontractorID, WorkItemID, CreatedOn, CreatedBy)
VALUES 
(@SubcontractorERP_ID, @WorkItemERP_ID, @SubcontractorID, @WorkItemID, @CreatedOn, @CreatedBy);";

                            foreach (var workItemId in subcontractor.WorkItemIDs)
                            {
                                if (!workItemErpMap.ContainsKey(workItemId))
                                    throw new Exception($"WorkItem ERP_ID not found for WorkItemID: {workItemId}");

                                await connection.ExecuteAsync(
                                    insertMappingQuery,
                                    new
                                    {
                                        SubcontractorERP_ID = subcontractorErpId,
                                        WorkItemERP_ID = workItemErpMap[workItemId],
                                        SubcontractorID = subcontractor.SubcontractorID,
                                        WorkItemID = workItemId,
                                        CreatedOn = amsterdamNow,
                                        CreatedBy = subcontractor.CreatedBy
                                    },
                                    transaction
                                );
                            }
                        }

                        // STEP 7: COMMIT
                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public async Task<Subcontractor> GetSubcontractorRemindersSent(Guid id)
        {
            var query = @"SELECT RemindersSent FROM Subcontractors WHERE IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Subcontractor>(query, new { Id = id });
        }

        public async Task<int> UpdateSubcontractorRemindersSent(Guid id, int reminderSent)
        {
            // Check if subcontractor is non-deleted subcontractor
            var subcontractor = await _connection.QueryFirstOrDefaultAsync<WorkItem>(
                "SELECT * FROM Subcontractors WHERE SubcontractorID = @Id AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found (either inactive or deleted)
            if (subcontractor == null)
                return 0;

            var query = @"
            UPDATE Subcontractors SET
            RemindersSent = @RemindersSent WHERE SubcontractorID = @Id";

            var parameters = new
            {
                Id = id,
                RemindersSent = reminderSent,
            };

            return await _connection.ExecuteAsync(query, parameters);
        }



        public async Task<int> DeleteSubcontractor(Guid id)
        {
            // Check if subcontractor exists and is not already deleted
            var subcontractor = await _connection.QueryFirstOrDefaultAsync<Subcontractor>(
                @"SELECT * 
          FROM Subcontractors 
          WHERE SubcontractorID = @Id AND IsDeleted = 0",
                new { Id = id }
            );

            // Return 0 if not found or already deleted
            if (subcontractor == null)
                return 0;

            var query = @"
        UPDATE Subcontractors
        SET
            IsDeleted = 1,
            DeletedOn = @DeletedOn,
            DeletedBy = @DeletedBy
        WHERE SubcontractorID = @Id";

            var parameters = new
            {
                Id = id,
                DeletedOn = amsterdamNow,
                DeletedBy = "System auto-deleted at the 3rd reminder."
            };

            return await _connection.ExecuteAsync(query, parameters);
        }


    }
}


