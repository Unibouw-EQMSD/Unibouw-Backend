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
            return await _connection.QueryAsync<SubcontractorWorkItemMapping>(
                "SELECT * FROM SubcontractorWorkItemsMapping");
        }

        public async Task<SubcontractorWorkItemMapping?> GetSubcontractorWorkItemMappingById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<SubcontractorWorkItemMapping>(
                "SELECT * FROM SubcontractorWorkItemsMapping WHERE SubcontractorWorkItemID = @Id",
                new { Id = id });
        }

        //------ SubcontractorAttachmentMapping
        public async Task<IEnumerable<SubcontractorAttachmentMapping>> GetAllSubcontractorAttachmentMapping()
        {
            return await _connection.QueryAsync<SubcontractorAttachmentMapping>(
                "SELECT * FROM SubcontractorAttachmentsMapping");
        }

        public async Task<SubcontractorAttachmentMapping?> GetSubcontractorAttachmentMappingById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<SubcontractorAttachmentMapping>(
                "SELECT * FROM SubcontractorAttachmentsMapping WHERE ID = @Id",
                new { Id = id });
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
