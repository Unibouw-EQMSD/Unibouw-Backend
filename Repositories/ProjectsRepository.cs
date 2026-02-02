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

        public async Task<IEnumerable<Project>> GetAllProject(string loggedInEmail, string role)
        {
            var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

            /*var query = @"
                    SELECT 
                        prj.*, 
                        c.CustomerName
                    FROM Projects prj
                    LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                    WHERE prj.IsDeleted = 0
                    " + (isAdmin ? "" : " AND prj.CreatedBy = @Email"); // If user is not an Admin, filter projects to only those created by the logged-in user; Admins see all projects.
            */

            var query = @"
                    SELECT 
                        prj.*, 
                        c.CustomerName,
                        p.Name AS PersonName,
                        r.RoleName AS PersonRole
                    FROM Projects prj
                    LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                    LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
                    LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
                    LEFT JOIN Roles r ON ppm.RoleID = r.RoleID
                    WHERE prj.IsDeleted = 0
                    " + (isAdmin ? "" : " AND prj.CreatedBy = @Email");

            return await _connection.QueryAsync<Project>(query, new { Email = loggedInEmail });
        }


        public async Task<Project?> GetProjectById(Guid id, string loggedInEmail, string role)
        {
            var isAdmin = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

            var query = @"
                    SELECT 
                        prj.*, 
                        c.CustomerName,
                        p.Name AS PersonName,
                        r.RoleName AS PersonRole
                    FROM Projects prj
                    LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                    LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
                    LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
                    LEFT JOIN Roles r ON ppm.RoleID = r.RoleID
                    WHERE prj.ProjectID = @Id AND prj.IsDeleted = 0
                    " + (isAdmin ? "" : " AND prj.CreatedBy = @Email");

            return await _connection.QueryFirstOrDefaultAsync<Project>(query, new { Id = id, Email = loggedInEmail });
        }

    }
}
