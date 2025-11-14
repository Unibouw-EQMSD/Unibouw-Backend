using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace UnibouwAPI.Repositories
{
    public class SubcontractorsRepository : ISubcontractors
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

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
                ModifiedOn = DateTime.UtcNow,
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

                        // ✅ Step 1: Check for duplicate EmailID
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
                            throw new InvalidOperationException("A subcontractor with this email address already exists.");

                        // ✅ Step 2: Generate new ID if not already present
                        subcontractor.SubcontractorID = Guid.NewGuid();

                        subcontractor.CreatedOn = DateTime.UtcNow;
                        subcontractor.IsActive ??= true;
                        subcontractor.IsDeleted = false;

                        // ✅ Step 3: Insert subcontractor
                        string insertSubcontractorQuery = @"
                            INSERT INTO Subcontractors 
                            (SubcontractorID, ERP_ID, Name, Rating, EmailID, PhoneNumber1, PhoneNumber2, 
                             Location, Country, OfficeAdress, BillingAddress, RegisteredDate, PersonID, 
                             IsActive, CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
                            VALUES 
                            (@SubcontractorID, @ERP_ID, @Name, @Rating, @EmailID, @PhoneNumber1, @PhoneNumber2, 
                             @Location, @Country, @OfficeAdress, @BillingAddress, @RegisteredDate, @PersonID, 
                             @IsActive, @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted);
                        ";

                        await connection.ExecuteAsync(insertSubcontractorQuery, subcontractor, transaction);

                        // ✅ Step 4: Insert WorkItem Mappings if provided
                        if (subcontractor.WorkItemIDs != null && subcontractor.WorkItemIDs.Any())
                        {
                            string insertMappingQuery = @"
                                INSERT INTO SubcontractorWorkItemsMapping (SubcontractorID, WorkItemID)
                                VALUES (@SubcontractorID, @WorkItemID);
                            ";

                            foreach (var workItemId in subcontractor.WorkItemIDs)
                            {
                                await connection.ExecuteAsync(insertMappingQuery, new
                                {
                                    SubcontractorID = subcontractor.SubcontractorID,
                                    WorkItemID = workItemId
                                }, transaction);
                            }
                        }

                        // ✅ Step 5: Commit
                        transaction.Commit();
                        return true;
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }


    }
}
