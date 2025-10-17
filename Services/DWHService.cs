using Dapper;
using Microsoft.Data.SqlClient;
using UnibouwAPI.Models.DWH;

namespace UnibouwAPI.Services
{
    public class DWHService
    {
        private readonly string _connectionStringDWH;

        public DWHService(IConfiguration configuration)
        {
            _connectionStringDWH = configuration.GetConnectionString("DWHDb");
        }

        public async Task<IEnumerable<WorkItemDatawareHouse>> GetLatestWorkItems(DateTime? lastSync = null)
        {
            using var connectionDwh = new SqlConnection(_connectionStringDWH);
            await connectionDwh.OpenAsync();

            var sql = "SELECT * FROM WorkItems";

            if (lastSync.HasValue)
                sql += " WHERE updated_at > @LastSync";

            var dt = await connectionDwh.QueryAsync<WorkItemDatawareHouse>(sql, new { LastSync = lastSync });

            return dt;
        }
    }
}
