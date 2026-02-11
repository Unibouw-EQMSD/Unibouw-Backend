using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations.Schema;
using UnibouwAPI.Helpers;


namespace UnibouwAPI.Repositories
{
    public class CommonRepository : ICommon
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

        public CommonRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        //------ WorkItemCategoryType
        public async Task<IEnumerable<WorkItemCategoryType>> GetAllWorkItemCategoryTypes()
        {
            return await _connection.QueryAsync<WorkItemCategoryType>("SELECT * FROM WorkItemCategoryTypes");
        }

        public async Task<WorkItemCategoryType?> GetWorkItemCategoryTypeById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<WorkItemCategoryType>("SELECT * FROM WorkItemCategoryTypes WHERE CategoryID = @Id", new { Id = id });
        }

        //------ Person
        public async Task<IEnumerable<Person>> GetAllPerson()
        {
            return await _connection.QueryAsync<Person>("SELECT * FROM Persons");
        }

        public async Task<Person?> GetPersonById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<Person>(
                "SELECT * FROM Persons WHERE PersonID = @Id",
                new { Id = id });
        }

        public async Task<int> CreatePerson(Person person)
        {
            var sql = @"
        INSERT INTO Persons 
        (PersonID, ERP_ID, Name, Mail, PhoneNumber1, PhoneNumber2, Address, State, City, Country, PostalCode,
         CreatedOn, CreatedBy, ModifiedOn, ModifiedBy, DeletedOn, DeletedBy, IsDeleted)
        VALUES 
        (@PersonID, @ERP_ID, @Name, @Mail, @PhoneNumber1, @PhoneNumber2, @Address, @State, @City, @Country, @PostalCode,
         @CreatedOn, @CreatedBy, @ModifiedOn, @ModifiedBy, @DeletedOn, @DeletedBy, @IsDeleted)";

            person.PersonID = Guid.NewGuid();
            person.CreatedOn = amsterdamNow;
            person.IsDeleted = false;

            return await _connection.ExecuteAsync(sql, person);
        }


        //------ SubcontractorWorkItemMapping
        public async Task<IEnumerable<SubcontractorWorkItemMapping>> GetAllSubcontractorWorkItemMapping(bool onlyActive = false)
        {
            var query = @"
        SELECT DISTINCT
            m.WorkItemID,
            m.SubcontractorID,
            s.Name AS SubcontractorName,
            w.Name AS WorkItemName
        FROM SubcontractorWorkItemsMapping m
        INNER JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
        INNER JOIN Subcontractors s ON m.SubcontractorID = s.SubcontractorID
        WHERE s.IsDeleted = 0";

            if (onlyActive)
                query += " AND s.IsActive = 1";

            return await _connection.QueryAsync<SubcontractorWorkItemMapping>(query);
        }

        public async Task<List<SubcontractorWorkItemMapping?>> GetSubcontractorWorkItemMappingById(Guid id)
        {
            var query = @"
            SELECT 
                m.*, 
                s.Name AS SubcontractorName,
                w.Name AS WorkItemName
            FROM SubcontractorWorkItemsMapping m
            LEFT JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
            LEFT JOIN Subcontractors s ON m.SubcontractorID = s.SubcontractorID
            WHERE m.SubcontractorID = @Id AND s.IsActive = 1";

            var result = await _connection.QueryAsync<SubcontractorWorkItemMapping>(query, new { Id = id });
            return result.ToList();
        }

        public async Task<bool> CreateSubcontractorWorkItemMapping(SubcontractorWorkItemMapping mapping)
        {
            var query = @"
                INSERT INTO SubcontractorWorkItemsMapping (SubcontractorID, WorkItemID)
                VALUES (@SubcontractorID, @WorkItemID);
            ";

            using (var connection = new SqlConnection(_connectionString))
            {
                var rows = await connection.ExecuteAsync(query, mapping);
                return rows > 0;
            }
        }

        //------ SubcontractorAttachmentMapping
        public async Task<IEnumerable<SubcontractorAttachmentMapping>> GetAllSubcontractorAttachmentMapping()
        {
            var query = @"
            SELECT 
                m.*, 
                s.Name AS SubcontractorName
            FROM SubcontractorAttachmentsMapping m
            LEFT JOIN Subcontractors s ON m.SubcontractorID = s.SubcontractorID";

            return await _connection.QueryAsync<SubcontractorAttachmentMapping>(query);
        }

        public async Task<List<SubcontractorAttachmentMapping>> GetSubcontractorAttachmentMappingById(Guid id)
        {
            var query = @"
            SELECT 
                m.*, 
                s.Name AS SubcontractorName
            FROM SubcontractorAttachmentsMapping m
            LEFT JOIN Subcontractors s ON m.SubcontractorID = s.SubcontractorID
            WHERE m.SubcontractorID = @Id";

            var result = await _connection.QueryAsync<SubcontractorAttachmentMapping>(query, new { Id = id });
            return result.ToList();
        }

        public async Task<bool> CreateSubcontractorAttachmentMappingsAsync(SubcontractorAttachmentMapping model)
        {
            if (model.Files == null || model.Files.Count == 0)
                throw new ArgumentException("No files uploaded.");

            string uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "subcontractors");
            if (!Directory.Exists(uploadRoot))
                Directory.CreateDirectory(uploadRoot);

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var file in model.Files)
                        {
                            string uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                            string filePath = Path.Combine(uploadRoot, uniqueFileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            var query = @"
                        INSERT INTO SubcontractorAttachmentsMapping
                        (SubcontractorID, FileName, FileType, FilePath, UploadedOn, UploadedBy)
                        VALUES (@SubcontractorID, @FileName, @FileType, @FilePath, @UploadedOn, @UploadedBy)";

                            await connection.ExecuteAsync(query, new
                            {
                                model.SubcontractorID,
                                FileName = file.FileName,
                                FileType = file.ContentType,
                                FilePath = $"/uploads/subcontractors/{uniqueFileName}",
                                UploadedOn = amsterdamNow,
                                model.UploadedBy
                            }, transaction);
                        }

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


        //------ Customer
        public async Task<IEnumerable<Customer>> GetAllCustomer()
        {
            return await _connection.QueryAsync<Customer>(
                "SELECT * FROM Customers");
        }

        public async Task<Customer?> GetCustomerById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<Customer>(
                "SELECT * FROM Customers WHERE CustomerID = @Id",
                new { Id = id });
        }

        //------ RfqResponseStatus
        public async Task<IEnumerable<RfqResponseStatus>> GetAllRfqResponseStatus()
        {
            return await _connection.QueryAsync<RfqResponseStatus>(
                "SELECT * FROM RfqResponseStatuses");
        }

        public async Task<RfqResponseStatus?> GetRfqResponseStatusById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<RfqResponseStatus>(
                "SELECT * FROM RfqResponseStatuses WHERE RfqResponseStatusID = @Id",
                new { Id = id });
        }

        //------ Global Rfq Reminder Set
        // Get all reminder settings
        public async Task<IEnumerable<RfqGlobalReminder>> GetRfqGlobalReminder()
        {
            string query = @"SELECT * FROM RfqGlobalReminder";
            return await _connection.QueryAsync<RfqGlobalReminder>(query);
        }

        // Update reminder settings
        public async Task<int> SaveRfqGlobalReminder(RfqGlobalReminder reminder)
        {
            // Check if exists
            const string checkSql = "SELECT COUNT(1) FROM RfqGlobalReminder";
            int exists = await _connection.ExecuteScalarAsync<int>(checkSql, new { reminder.RfqGlobalReminderID });

            if (exists > 0)
            {
                const string sql = @"
                    UPDATE TOP (1) RfqGlobalReminder
                    SET 
                        ReminderSequence = @ReminderSequence,
                        ReminderTime = @ReminderTime,
                        ReminderEmailBody = @ReminderEmailBody,
                        UpdatedBy = @UpdatedBy,
                        UpdatedAt = @UpdatedAt,
                        IsEnable = @IsEnable";
                return await _connection.ExecuteAsync(sql, reminder);
            }
            else
            {
                reminder.RfqGlobalReminderID = Guid.NewGuid();
                const string sql = @"
                INSERT INTO RfqGlobalReminder
                    (RfqGlobalReminderID, ReminderSequence, ReminderTime, ReminderEmailBody, UpdatedBy, UpdatedAt, IsEnable)
                VALUES
                    (@RfqGlobalReminderID, @ReminderSequence, @ReminderTime, @ReminderEmailBody, @UpdatedBy, @UpdatedAt, @IsEnable)";

                return await _connection.ExecuteAsync(sql, reminder);
            }
        }
            
    }
}
