using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace UnibouwAPI.Repositories
{
    public class RfqDocNotifyRow
    {
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public string? Email { get; set; }

        // status name (string)
        public string? RfqResponseStatusName { get; set; }

        // email thread anchor from OutboundRfqEmailAnchor
        public string? GraphMessageId { get; set; }
        public string? PmMailbox { get; set; }
    }
    public class RfqDocumentNotificationRepository
    {
        private readonly string _connectionString;
        public RfqDocumentNotificationRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("UnibouwDbConnection")
                ?? throw new InvalidOperationException("UnibouwDbConnection missing");
        }
        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<Guid>> GetPriorSentRfqsMissingDocAsync(Guid projectId, Guid projectDocumentId, Guid excludeRfqId)
        {
            const string sql = @"
SELECT r.RfqID
FROM Rfq r
WHERE r.ProjectID = @ProjectID
  AND r.IsDeleted = 0
  AND r.Status = 'Sent'
  AND r.RfqID <> @ExcludeRfqID
  AND NOT EXISTS (
      SELECT 1 FROM RfqDocumentLink l
      WHERE l.RfqID = r.RfqID AND l.ProjectDocumentID = @ProjectDocumentID
  );";
            using var con = Connection;
            return await con.QueryAsync<Guid>(sql, new { ProjectID = projectId, ProjectDocumentID = projectDocumentId, ExcludeRfqID = excludeRfqId });
        }

        public async Task<IEnumerable<RfqDocNotifyRow>> GetEligibleSubcontractorsForRfqAsync(Guid rfqId)
        {
            const string sql = @"
SELECT
    rsm.RfqID,
    rsm.SubcontractorID,
    s.Email,
    rs.RfqResponseStatusName,
    a.GraphMessageId,
    a.PmMailbox
FROM dbo.RfqSubcontractorMapping rsm
INNER JOIN dbo.Subcontractors s
    ON s.SubcontractorID = rsm.SubcontractorID

-- pick ONE response row per subcontractor (latest)
OUTER APPLY (
    SELECT TOP 1 resp.RfqResponseStatusID
    FROM dbo.RfqSubcontractorResponse resp
    WHERE resp.RfqID = rsm.RfqID
      AND resp.SubcontractorID = rsm.SubcontractorID
    ORDER BY resp.ModifiedOn DESC, resp.CreatedOn DESC
) resp1
LEFT JOIN dbo.RfqResponseStatus rs
    ON rs.RfqResponseStatusID = resp1.RfqResponseStatusID

-- pick ONE anchor row per subcontractor (latest)
OUTER APPLY (
    SELECT TOP 1 PmMailbox, GraphMessageId
    FROM dbo.OutboundRfqEmailAnchor a
    WHERE a.RfqId = rsm.RfqID
      AND a.SubcontractorId = rsm.SubcontractorID
      AND a.IsActive = 1
    ORDER BY a.SentUtc DESC
) a
WHERE rsm.RfqID = @RfqID
  AND (
        rs.RfqResponseStatusName IN ('Maybe Later','Interested','Viewed','Not Responded')
        OR resp1.RfqResponseStatusID IS NULL
      )
  AND ISNULL(rs.RfqResponseStatusName,'') <> 'Not Interested';";

            using var con = Connection;
            return await con.QueryAsync<RfqDocNotifyRow>(sql, new { RfqID = rfqId });
        }
        public async Task<bool> WasNotificationSentAsync(Guid rfqId, Guid projectDocumentId, Guid subcontractorId)
        {
            const string sql = @"SELECT COUNT(1) FROM RfqDocumentNotificationLog WHERE RfqID=@RfqID AND ProjectDocumentID=@ProjectDocumentID AND SubcontractorID=@SubcontractorID;";
            using var con = Connection;
            var c = await con.ExecuteScalarAsync<int>(sql, new { RfqID = rfqId, ProjectDocumentID = projectDocumentId, SubcontractorID = subcontractorId });
            return c > 0;
        }

        public async Task LogNotificationAsync(Guid rfqId, Guid projectDocumentId, Guid subcontractorId)
        {
            const string sql = @"
INSERT INTO RfqDocumentNotificationLog (LogID, RfqID, ProjectDocumentID, SubcontractorID, SentOn)
VALUES (NEWID(), @RfqID, @ProjectDocumentID, @SubcontractorID, SYSUTCDATETIME());";
            using var con = Connection;
            await con.ExecuteAsync(sql, new { RfqID = rfqId, ProjectDocumentID = projectDocumentId, SubcontractorID = subcontractorId });
        }
    }
}