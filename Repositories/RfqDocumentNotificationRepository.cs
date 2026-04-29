using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace UnibouwAPI.Repositories
{
    public class RfqDocNotifyRow
    {
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }
        public string Email { get; set; }

        public Guid WorkItemID { get; set; }
        public string WorkItemNumber { get; set; }
        public string WorkItemName { get; set; }

        public string RfqResponseStatusName { get; set; }

        public string GraphMessageId { get; set; }
        public string PmMailbox { get; set; }
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



        public async Task<List<Guid>> GetPreviousSentRfqsMissingDocumentAsync(
      Guid projectId,
      Guid currentRfqId,
      Guid projectDocumentId)
        {
            const string sql = @"
SELECT r.RfqID
FROM dbo.Rfq r
JOIN dbo.ProjectDocument d
    ON d.ProjectDocumentID = @ProjectDocumentID

WHERE r.ProjectID = @ProjectID
  AND r.RfqID <> @CurrentRfqID
  AND r.Status = 'Sent'
  AND r.IsDeleted = 0

  -- 🚨 CRITICAL FIX: ONLY RFQs SENT BEFORE DOCUMENT WAS ADDED
  AND r.SentDate < d.CreatedOn

  -- RFQ must NOT already have document
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.RfqDocumentLink l
        WHERE l.RfqID = r.RfqID
          AND l.ProjectDocumentID = @ProjectDocumentID
  );
";

            using var con = new SqlConnection(_connectionString);

            var rfqs = await con.QueryAsync<Guid>(sql, new
            {
                ProjectID = projectId,
                CurrentRfqID = currentRfqId,
                ProjectDocumentID = projectDocumentId
            });

            return rfqs.ToList();
        }

        public async Task<IEnumerable<RfqDocNotifyRow>> GetEligibleSubcontractorsForRfqAsync(Guid rfqId)
        {
            const string sql = @"
WITH LatestResponsePerWorkItem AS (
    SELECT 
        resp.RfqID,
        resp.SubcontractorID,
        resp.WorkItemID,
        resp.RfqResponseStatusID,
        ROW_NUMBER() OVER (
            PARTITION BY resp.RfqID, resp.SubcontractorID, resp.WorkItemID
            ORDER BY resp.ModifiedOn DESC, resp.CreatedOn DESC
        ) AS rn
    FROM dbo.RfqSubcontractorResponse resp
),
FilteredResponses AS (
    SELECT 
        lr.RfqID,
        lr.SubcontractorID,
        lr.WorkItemID,
        rs.RfqResponseStatusName
    FROM LatestResponsePerWorkItem lr
    LEFT JOIN dbo.RfqResponseStatus rs
        ON rs.RfqResponseStatusID = lr.RfqResponseStatusID
    WHERE lr.rn = 1
)

SELECT 
    rsm.RfqID,
    rsm.SubcontractorID,
    s.Email,
    wi.WorkItemID,
    wi.Number AS WorkItemNumber,
    wi.Name   AS WorkItemName,
    fr.RfqResponseStatusName,
    a.GraphMessageId,
    a.PmMailbox

FROM dbo.RfqSubcontractorMapping rsm

INNER JOIN dbo.Subcontractors s
    ON s.SubcontractorID = rsm.SubcontractorID

INNER JOIN dbo.RfqWorkItemMapping rwim
    ON rwim.RfqID = rsm.RfqID

INNER JOIN dbo.WorkItems wi
    ON wi.WorkItemID = rwim.WorkItemID
    AND wi.IsDeleted = 0

LEFT JOIN FilteredResponses fr
    ON fr.RfqID = rsm.RfqID
   AND fr.SubcontractorID = rsm.SubcontractorID
   AND fr.WorkItemID = wi.WorkItemID

OUTER APPLY (
    SELECT TOP 1 PmMailbox, GraphMessageId
    FROM dbo.OutboundRfqEmailAnchor a
    WHERE a.RfqId = rsm.RfqID
      AND a.SubcontractorId = rsm.SubcontractorID
      AND a.IsActive = 1
    ORDER BY a.SentUtc DESC
) a

WHERE rsm.RfqID = @RfqID
  AND fr.RfqResponseStatusName IN (
        'Interested',
        'Maybe Later',
        'Viewed',
        'Not Responded'
  );
";

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