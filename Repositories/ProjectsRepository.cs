using Dapper;
using Microsoft.Data.SqlClient;
using OfficeOpenXml.DataValidation;
using System;
using System.Data;
using System.Diagnostics;
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

            if (isAdmin)
            {
                //If any project has multiple entries in PersonProjectMapping(example: same project assigned to 2 persons OR same person assigned twice), then that project row gets duplicated.
                var query = @"
                    SELECT DISTINCT
                        prj.*, 
                        c.CustomerName,
                        p.Name AS PersonName,
                        p.Email AS PersonEmail,
                        r.RoleName AS PersonRole
                    FROM Projects prj
                    LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                    LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
                    LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
                    LEFT JOIN Roles r ON ppm.RoleID = r.RoleID
                    WHERE prj.IsDeleted = 0";

                var projectlist = await _connection.QueryAsync<Project>(query);

                return projectlist;
            }

            //Filter projects based on logged in Email and role (PersonProjectMapping)
            var queryForUser = @"
                SELECT DISTINCT
                    prj.*, 
                    c.CustomerName,
                    p.Name AS PersonName,
                    p.Email AS PersonEmail,
                    r.RoleName AS PersonRole
                FROM Projects prj
                INNER JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
                INNER JOIN Persons p ON ppm.PersonID = p.PersonID
                INNER JOIN Roles r ON ppm.RoleID = r.RoleID
                LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                WHERE prj.IsDeleted = 0
                  AND p.Email = @Email
                  AND r.RoleName = @Role";

            var projects = await _connection.QueryAsync<Project>(queryForUser,
                new { Email = loggedInEmail, Role = role });

            return projects;
        }
 
        public async Task<Project?> GetProjectById(Guid id, string loggedInEmail, string role)
        {
            var query = @"
                    SELECT DISTINCT
                        prj.*, 
                        c.CustomerName,
                        p.Name AS PersonName,
                        p.Email AS PersonEmail,
                        r.RoleName AS PersonRole
                    FROM Projects prj
                    LEFT JOIN Customers c ON prj.CustomerID = c.CustomerID
                    LEFT JOIN PersonProjectMapping ppm ON prj.ProjectID = ppm.ProjectID
                    LEFT JOIN Persons p ON ppm.PersonID = p.PersonID
                    LEFT JOIN Roles r ON ppm.RoleID = r.RoleID
                    WHERE prj.ProjectID = @Id AND prj.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Project>(query, new { Id = id, Email = loggedInEmail });
        }

    }
}
