using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class ProjectsRepository : IProjects
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ProjectsRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Project>> GetAllProject()
        {
            var query = @"
                SELECT 
                    p.*, 
                    c.CustomerName,
                    wp.WorkPlannerName,
                    pm.ProjectManagerName,
                    per.Name AS PersonName
                FROM Projects p
                LEFT JOIN Customers c ON p.CustomerID = c.CustomerID
                LEFT JOIN WorkPlanners wp ON p.WorkPlannerID = wp.WorkPlannerID
                LEFT JOIN ProjectManagers pm ON p.ProjectMangerID = pm.ProjectMangerID
                LEFT JOIN Persons per ON p.PersonID = per.PersonID
                WHERE p.IsDeleted = 0";

            return await _connection.QueryAsync<Project>(query);
        }

        public async Task<Project?> GetProjectById(Guid id)
        {
            var query = @"
                SELECT 
                    p.*, 
                    c.CustomerName,
                    wp.WorkPlannerName,
                    pm.ProjectManagerName,
                    per.Name AS PersonName
                FROM Projects p
                LEFT JOIN Customers c ON p.CustomerID = c.CustomerID
                LEFT JOIN WorkPlanners wp ON p.WorkPlannerID = wp.WorkPlannerID
                LEFT JOIN ProjectManagers pm ON p.ProjectMangerID = pm.ProjectMangerID
                LEFT JOIN Persons per ON p.PersonID = per.PersonID
                WHERE p.ProjectID = @Id AND p.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Project>(query, new { Id = id });
        }

    }
}
