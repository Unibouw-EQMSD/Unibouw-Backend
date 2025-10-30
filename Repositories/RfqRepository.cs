using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqRepository : IRfq
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public RfqRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Rfq>> GetAllRfq()
        {
            var query = @"
        SELECT 
            r.*, 
            c.CustomerName,
            p.Name AS ProjectName,
            rs.RfqResponseStatusName               
        FROM Rfq r
        LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
        LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
        LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
        WHERE r.IsDeleted = 0";

            return await _connection.QueryAsync<Rfq>(query);
        }

        public async Task<Rfq?> GetRfqById(Guid id)
        {
            var query = @"
        SELECT 
            r.*, 
            c.CustomerName,
            p.Name AS ProjectName,
            rs.RfqResponseStatusName               
        FROM Rfq r
        LEFT JOIN Customers c ON r.CustomerID = c.CustomerID
        LEFT JOIN Projects p ON r.ProjectID = p.ProjectID
        LEFT JOIN RfqResponseStatus rs ON r.RfqResponseID = rs.RfqResponseID
        WHERE r.RfqID = @Id AND r.IsDeleted = 0";

            return await _connection.QueryFirstOrDefaultAsync<Rfq>(query, new { Id = id });
        }

    }
}
