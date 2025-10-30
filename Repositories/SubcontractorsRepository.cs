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

        public async Task<bool> CreateSubcontractor(Subcontractor subcontractor)
        {
            var query = @"
                INSERT INTO Subcontractors 
                (SubcontractorID, ERP_ID, Name, Rating, EmailID, PhoneNumber1, PhoneNumber2, Location, 
                 Country, OfficeAdress, BillingAddress, RegisteredDate, PersonID, IsActive, 
                 CreatedOn, CreatedBy, IsDeleted)
                VALUES 
                (@SubcontractorID, @ERP_ID, @Name, @Rating, @EmailID, @PhoneNumber1, @PhoneNumber2, @Location, 
                 @Country, @OfficeAdress, @BillingAddress, @RegisteredDate, @PersonID, @IsActive, 
                 @CreatedOn, @CreatedBy, @IsDeleted)";

            subcontractor.SubcontractorID = Guid.NewGuid();
            subcontractor.CreatedOn = DateTime.UtcNow;
            subcontractor.IsDeleted = false;

            var rows = await _connection.ExecuteAsync(query, subcontractor);
            return rows > 0;
        }

    }
}
