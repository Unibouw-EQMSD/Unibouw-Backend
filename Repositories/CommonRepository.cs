using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class CommonRepository : ICommon
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

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

        //------ SubcontractorWorkItemMapping
        public async Task<IEnumerable<SubcontractorWorkItemMapping>> GetAllSubcontractorWorkItemMapping()
        {
            var query = @"
                    SELECT 
                        m.*, 
                        s.Name AS SubcontractorName,
                        w.Name AS WorkItemName
                    FROM SubcontractorWorkItemsMapping m
                    LEFT JOIN WorkItems w ON m.WorkItemID = w.WorkItemID
                    LEFT JOIN Subcontractors s ON m.SubcontractorID = s.SubcontractorID;
                ";

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
            WHERE m.SubcontractorID = @Id";

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

        //------ WorkPlanner
        public async Task<IEnumerable<WorkPlanner>> GetAllWorkPlanner()
        {
            return await _connection.QueryAsync<WorkPlanner>(
                "SELECT * FROM WorkPlanners");
        }

        public async Task<WorkPlanner?> GetWorkPlannerById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<WorkPlanner>(
                "SELECT * FROM WorkPlanners WHERE WorkPlannerID = @Id",
                new { Id = id });
        }

        //------ ProjectManager
        public async Task<IEnumerable<ProjectManager>> GetAllProjectManager()
        {
            return await _connection.QueryAsync<ProjectManager>(
                "SELECT * FROM ProjectManagers");
        }

        public async Task<ProjectManager?> GetProjectManagerById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<ProjectManager>(
                "SELECT * FROM ProjectManagers WHERE ProjectManagerID = @Id",
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


    }
}
