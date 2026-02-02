using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Claims;
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

        public async Task<IEnumerable<Subcontractor>> GetAllSubcontractor()
        {
            var query = @"
                 SELECT 
                     s.*, 
                     p.Name AS ContactName,
                     p.Email AS ContactEmail,
                     p.PhoneNumber1 AS ContactPhone
                 FROM Subcontractors s
                 LEFT JOIN Persons p ON s.PersonID = p.PersonID
                 WHERE s.IsDeleted = 0";

            return await _connection.QueryAsync<Subcontractor>(query);
        }

        public async Task<Subcontractor?> GetSubcontractorById(Guid id)
        {
            var query = @"
                    SELECT 
                        s.*, 
                        p.Name AS ContactName,
                        p.Email AS ContactEmail,
                        p.PhoneNumber1 AS ContactPhone
                    FROM Subcontractors s
                    LEFT JOIN Persons p ON s.PersonID = p.PersonID
                    WHERE s.SubcontractorID = @Id AND s.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Subcontractor>(query, new { Id = id });
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

        public async Task<bool> CreateSubcontractorWithMappings(Subcontractor subcontractor)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        //  STEP 1: CHECK DUPLICATE EMAIL or NAME
                        // Email check
                        string duplicateEmailQuery = @"
                                SELECT COUNT(1) 
                                FROM Subcontractors 
                                WHERE LOWER(Email) = LOWER(@Email)
                                  AND IsDeleted = 0;
                            ";

                        int existingEmailCount = await connection.ExecuteScalarAsync<int>(
                            duplicateEmailQuery,
                            new { subcontractor.Email },
                            transaction
                        );

                        if (existingEmailCount > 0)
                            throw new InvalidOperationException("A subcontractor with this email already exists.");

                        // Name check
                        string duplicateNameQuery = @"
                                SELECT COUNT(1) 
                                FROM Subcontractors 
                                WHERE LOWER(Name) = LOWER(@Name)
                                  AND IsDeleted = 0;
                            ";

                        int existingNameCount = await connection.ExecuteScalarAsync<int>(
                            duplicateNameQuery,
                            new { subcontractor.Name },
                            transaction
                        );

                        if (existingNameCount > 0)
                            throw new InvalidOperationException("A subcontractor with this name already exists.");

                        //  STEP 2: INSERT INTO PERSONS TABLE
                        var person = new Person
                        {
                            PersonID = Guid.NewGuid(),
                            Name = subcontractor.ContactName,
                            Email = subcontractor.ContactEmail,
                            PhoneNumber1 = subcontractor.ContactPhone,
                            CreatedOn = amsterdamNow,
                            CreatedBy = subcontractor.CreatedBy,
                            IsDeleted = false
                        };

                        string insertPersonQuery = @"
                                INSERT INTO Persons
                                (PersonID, Name, Email, PhoneNumber1, PhoneNumber2, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
                                VALUES
                                (@PersonID, @Name, @Email, @PhoneNumber1, @PhoneNumber2, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted);
                            ";

                        await connection.ExecuteAsync(insertPersonQuery, person, transaction);

                        // Store generated PersonID in subcontractor
                        subcontractor.PersonID = person.PersonID;

                        //  STEP 3: INSERT INTO SUBCONTRACTORS TABLE
                        subcontractor.SubcontractorID = Guid.NewGuid();
                        subcontractor.CreatedOn = amsterdamNow;
                        subcontractor.IsActive ??= true;
                        subcontractor.IsDeleted = false;

                        string insertSubcontractorQuery = @"
                                INSERT INTO Subcontractors 
                                (SubcontractorID, Name, Rating, Email, Location, Country, OfficeAddress, BillingAddress, RegisteredDate, PersonID, 
                                 IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted, RemindersSent)
                                VALUES 
                                (@SubcontractorID, @Name, @Rating, @Email, @Location, @Country, @OfficeAddress, @BillingAddress, @RegisteredDate, @PersonID, 
                                 @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted, @RemindersSent);
                            ";

                        await connection.ExecuteAsync(insertSubcontractorQuery, subcontractor, transaction);

                        //  STEP 4: INSERT INTO SubcontractorWorkItemsMapping table 
                        if (subcontractor.WorkItemIDs != null && subcontractor.WorkItemIDs.Any())
                        {
                            string insertMappingQuery = @"
                                INSERT INTO SubcontractorWorkItemsMapping 
                                (SubcontractorID, WorkItemID, CreatedOn, CreatedBy)
                                VALUES (@SubcontractorID, @WorkItemID, @CreatedOn, @CreatedBy);
                            ";

                            foreach (var workItemId in subcontractor.WorkItemIDs)
                            {
                                await connection.ExecuteAsync(
                                        insertMappingQuery,
                                        new
                                        {
                                            SubcontractorID = subcontractor.SubcontractorID,
                                            WorkItemID = workItemId,
                                            CreatedOn = amsterdamNow,
                                            CreatedBy = subcontractor.CreatedBy
                                        },
                                        transaction
                                    );
                            }
                        }

                        //  STEP 5: COMMIT
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


