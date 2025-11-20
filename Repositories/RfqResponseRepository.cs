using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

namespace UnibouwAPI.Repositories
{
    public class RfqResponseRepository : IRfqResponse
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public RfqResponseRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);


        //---RfqResponseDocument
        public async Task<IEnumerable<RfqResponseDocument>> GetAllRfqResponseDocuments()
        {
            return await _connection.QueryAsync<RfqResponseDocument>("SELECT * FROM RfqResponseDocuments");
        }

        public async Task<RfqResponseDocument?> GetRfqResponseDocumentsById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<RfqResponseDocument>("SELECT * FROM RfqResponseDocuments WHERE RfqResponseDocumentID = @Id", new { Id = id });
        }


        //---RfqSubcontractorResponse
        public async Task<IEnumerable<RfqSubcontractorResponse>> GetAllRfqSubcontractorResponse()
        {
            return await _connection.QueryAsync<RfqSubcontractorResponse>("SELECT * FROM RfqSubcontractorResponse");
        }

        public async Task<RfqSubcontractorResponse?> GetRfqSubcontractorResponseById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<RfqSubcontractorResponse>("SELECT * FROM RfqSubcontractorResponse WHERE RfqSubcontractorResponseID = @Id", new { Id = id });
        }


        //---RfqSubcontractorWorkItemResponse
        public async Task<IEnumerable<RfqSubcontractorWorkItemResponse>> GetAllRfqSubcontractorWorkItemResponse()
        {
            return await _connection.QueryAsync<RfqSubcontractorWorkItemResponse>("SELECT * FROM RfqSubcontractorWorkItemResponse");
        }

        public async Task<RfqSubcontractorWorkItemResponse?> GetRfqSubcontractorWorkItemResponseById(Guid id)
        {
            return await _connection.QueryFirstOrDefaultAsync<RfqSubcontractorWorkItemResponse>("SELECT * FROM RfqSubcontractorWorkItemResponse WHERE RfqSubcontractorWorkItemResponseID = @Id", new { Id = id });
        }

        //-----------------------------------------

        //------
    }
}
