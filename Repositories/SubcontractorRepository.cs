using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class SubcontractorRepository : ISubcontractor
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public SubcontractorRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Subcontractor>> GetAllAsync()
        {
            return await _connection.QueryAsync<Subcontractor>("SELECT * FROM Subcontractor");
        }

        public async Task<Subcontractor?> GetByIdAsync(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<Subcontractor>("SELECT * FROM Subcontractor WHERE ID = @Id", new { Id = id });
        }
    }
}
