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
             p.Name AS PersonName
         FROM Subcontractors s
         LEFT JOIN Persons p ON s.PersonID = p.PersonID
         WHERE s.IsDeleted = 0";

            return await _connection.QueryAsync<Subcontractor>(query);

            //return await _connection.QueryAsync<Subcontractor>("SELECT * FROM Subcontractors");
        }


        public async Task<Subcontractor?> GetSubcontractorById(Guid id)
        {
            var query = @"
        SELECT 
            s.*, 
            p.Name AS PersonName
        FROM Subcontractors s
        LEFT JOIN Persons p ON s.PersonID = p.PersonID
        WHERE s.SubcontractorID = @Id AND s.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Subcontractor>(query, new { Id = id });

            //return await _connection.QueryFirstOrDefaultAsync<Subcontractor>("SELECT * FROM Subcontractors WHERE SubcontractorID = @Id", new { Id = id });
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

                        // ============================
                        //  STEP 1: CHECK DUPLICATE EMAIL or NAME
                        // ============================
                        // Email check
                        string duplicateEmailQuery = @"
                                SELECT COUNT(1) 
                                FROM Subcontractors 
                                WHERE LOWER(EmailID) = LOWER(@EmailID)
                                  AND IsDeleted = 0;
                            ";

                        int existingEmailCount = await connection.ExecuteScalarAsync<int>(
                            duplicateEmailQuery,
                            new { subcontractor.EmailID },
                            transaction
                        );

                        if (existingEmailCount > 0)
                            throw new InvalidOperationException("A subcontractor with this email already exists");

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
                            throw new InvalidOperationException("A subcontractor with this name already exists");


                        // ============================
                        //  STEP 2: CREATE CONTACT PERSON
                        // ============================
                        var person = new Person
                        {
                            PersonID = Guid.NewGuid(),
                            Name = subcontractor.ContactName,
                            Mail = subcontractor.ContactEmailID,
                            PhoneNumber1 = subcontractor.ContactPhone,
                            CreatedOn = amsterdamNow,
                            CreatedBy = subcontractor.CreatedBy,
                            IsDeleted = false
                        };

                        string insertPersonQuery = @"
                INSERT INTO Persons
                (PersonID, ERP_ID, Name, Mail, PhoneNumber1, PhoneNumber2, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
                VALUES
                (@PersonID, @ERP_ID, @Name, @Mail, @PhoneNumber1, @PhoneNumber2, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted);
            ";

                        await connection.ExecuteAsync(insertPersonQuery, person, transaction);

                        // Store generated PersonID in subcontractor
                        subcontractor.PersonID = person.PersonID;


                        // ============================
                        //  STEP 3: INSERT SUBCONTRACTOR
                        // ============================
                        subcontractor.SubcontractorID = Guid.NewGuid();
                        subcontractor.CreatedOn = amsterdamNow;
                        subcontractor.IsActive ??= true;
                        subcontractor.IsDeleted = false;

                        string insertSubcontractorQuery = @"
                INSERT INTO Subcontractors 
                (SubcontractorID, ERP_ID, Name, Rating, EmailID, PhoneNumber1, PhoneNumber2, 
                 Location, Country, OfficeAddress, BillingAddress, RegisteredDate, PersonID, 
                 IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
                VALUES 
                (@SubcontractorID, @ERP_ID, @Name, @Rating, @EmailID, @PhoneNumber1, @PhoneNumber2, 
                 @Location, @Country, @OfficeAddress, @BillingAddress, @RegisteredDate, @PersonID, 
                 @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted);
            ";

                        await connection.ExecuteAsync(insertSubcontractorQuery, subcontractor, transaction);


                        // ============================
                        //  STEP 4: INSERT WORKITEM MAPPINGS
                        // ============================
                        if (subcontractor.WorkItemIDs != null && subcontractor.WorkItemIDs.Any())
                        {
                            string insertMappingQuery = @"
                    INSERT INTO SubcontractorWorkItemsMapping 
                    (SubcontractorID, WorkItemID)
                    VALUES (@SubcontractorID, @WorkItemID);
                ";

                            foreach (var workItemId in subcontractor.WorkItemIDs)
                            {
                                await connection.ExecuteAsync(insertMappingQuery,
                                    new { SubcontractorID = subcontractor.SubcontractorID, WorkItemID = workItemId },
                                    transaction
                                );
                            }
                        }

                        // ============================
                        //  STEP 5: COMMIT
                        // ============================
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
    }
}


