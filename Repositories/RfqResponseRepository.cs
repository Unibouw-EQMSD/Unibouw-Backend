using Dapper;
using iText.Layout.Element;
using Microsoft.Data.SqlClient;
using System;
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


        //---------------------------------------------------------

        private async Task<Guid> GetResponseIdAsync(SqlConnection connection, SqlTransaction tx, string status)
        {
            var id = await connection.ExecuteScalarAsync<Guid?>(@"
        SELECT RfqResponseID 
        FROM RfqResponseStatus
        WHERE RfqResponseStatusName = @Status",
                new { Status = status }, tx);

            if (id == null)
                throw new Exception($"Status '{status}' not found in RfqResponseStatus table.");

            return id.Value;
        }

        private async Task<bool> ValidateSubcontractorAsync(SqlConnection connection, SqlTransaction transaction, Guid subcontractorId)
        {
            var query = "SELECT COUNT(1) FROM Subcontractors WHERE SubcontractorID = @SubcontractorID";
            var count = await connection.ExecuteScalarAsync<int>(query, new { SubcontractorID = subcontractorId }, transaction);
            return count > 0;
        }

        private async Task<bool> ValidateRfqAsync(SqlConnection connection, SqlTransaction transaction, Guid rfqId)
        {
            var query = "SELECT COUNT(1) FROM Rfq WHERE RfqID = @RfqID";
            var count = await connection.ExecuteScalarAsync<int>(query, new { RfqID = rfqId }, transaction);
            return count > 0;
        }

        // ✅ GET (Simple link click, no workItem/file)
        public async Task<bool> SaveResponseAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, string status)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Validate RFQ + subcontractor
                if (!await ValidateRfqAsync(connection, transaction, rfqId))
                    throw new Exception($"RFQ {rfqId} does not exist.");

                if (!await ValidateSubcontractorAsync(connection, transaction, subcontractorId))
                    throw new Exception($"Subcontractor {subcontractorId} does not exist.");

                // Ensure mappings
                await connection.ExecuteAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM RfqSubcontractorMapping
                WHERE RfqID = @RfqID AND SubcontractorID = @SubID
            )
            INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
            VALUES (@RfqID, @SubID);
        ", new { RfqID = rfqId, SubID = subcontractorId }, transaction);

                await connection.ExecuteAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM RfqWorkItemMapping
                WHERE RfqID = @RfqID AND WorkItemID = @WorkItemID
            )
            INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
            VALUES (@RfqID, @WorkItemID);
        ", new { RfqID = rfqId, WorkItemID = workItemId }, transaction);

                // Map status → RfqResponseID
                var responseId = await GetResponseIdAsync(connection, transaction, status);

                // Check if response already exists for this RFQ & Subcontractor
                var existing = await connection.QuerySingleOrDefaultAsync<Guid?>(@"
            SELECT RfqSubcontractorResponseID
            FROM RfqSubcontractorResponse
            WHERE RfqID = @RfqID AND SubcontractorID = @SubID
        ", new { RfqID = rfqId, SubID = subcontractorId }, transaction);

                Guid responseRecordId;

                if (existing == null)
                {
                    // INSERT new record
                    responseRecordId = Guid.NewGuid();

                    await connection.ExecuteAsync(@"
                INSERT INTO RfqSubcontractorResponse
                (RfqSubcontractorResponseID, RfqID, SubcontractorID, RfqResponseID, CreatedOn)
                VALUES (@ID, @RfqID, @SubID, @RespID, GETUTCDATE());
            ", new
                    {
                        ID = responseRecordId,
                        RfqID = rfqId,
                        SubID = subcontractorId,
                        RespID = responseId
                    }, transaction);

                    await connection.ExecuteAsync(@"
                INSERT INTO RfqSubcontractorWorkItemResponse
                (RfqSubcontractorWorkItemResponseID, RfqSubcontractorResponseID, RfqID, WorkItemID, IsReviewed, CreatedOn)
                VALUES (NEWID(), @RespID, @RfqID, @WorkItemID, 0, GETUTCDATE());
            ", new
                    {
                        RespID = responseRecordId,
                        RfqID = rfqId,
                        WorkItemID = workItemId
                    }, transaction);
                }
                else
                {
                    // UPDATE existing record
                    responseRecordId = existing.Value;

                    await connection.ExecuteAsync(@"
                UPDATE RfqSubcontractorResponse
                SET RfqResponseID = @RespID,
                    ModifiedOn = GETUTCDATE()
                WHERE RfqSubcontractorResponseID = @ID
            ", new
                    {
                        ID = responseRecordId,
                        RespID = responseId
                    }, transaction);
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<object?> GetProjectSummaryAsync(Guid rfqId, List<Guid>? workItemIds = null)
        {
            const string projectSql = @"
SELECT 
    p.ProjectID, p.Name, p.Number, p.Company, p.SharepointURL, p.StartDate, p.CompletionDate
FROM Projects p
INNER JOIN Rfq r ON r.ProjectID = p.ProjectID
WHERE r.RfqID = @RfqID";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // 🧩 Fetch project details
            var project = await conn.QueryFirstOrDefaultAsync<Project>(
                projectSql,
                new { RfqID = rfqId }
            );

            if (project == null)
                return null;

            // 🧩 Fetch related work items from RfqWorkItemMapping
            const string workItemSql = @"
SELECT 
    w.WorkItemID,
    w.Name,
    w.Description
FROM WorkItems w
INNER JOIN RfqWorkItemMapping rwim ON rwim.WorkItemID = w.WorkItemID
WHERE rwim.RfqID = @RfqID";

            var workItems = (await conn.QueryAsync<WorkItem>(
                workItemSql,
                new { RfqID = rfqId }
            )).ToList();

            // ✅ Return combined summary
            return new
            {
                Project = project,
                WorkItems = workItems
            };
        }

        public async Task<List<RfqResponseDocument>> GetPreviousSubmissionsAsync(Guid rfqId, Guid subcontractorId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var submissions = await conn.QueryAsync<RfqResponseDocument>(@"
        SELECT *
        FROM RfqResponseDocuments
        WHERE RfqID = @RfqID
          AND SubcontractorID = @SubID
          AND (IsDeleted IS NULL OR IsDeleted = 0)
        ORDER BY UploadedOn DESC
    ", new { RfqID = rfqId, SubID = subcontractorId });

            return submissions.ToList();
        }


        public async Task<bool> UploadQuoteAsync(Guid rfqId, Guid subcontractorId, IFormFile file, decimal totalAmount, string comment)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var query = @"
UPDATE RfqSubcontractorResponse
SET 
    TotalQuoteAmount = @amount,
    Comment = @comment,
    ModifiedOn = GETDATE(),
    SubmissionCount = SubmissionCount + 1
WHERE RfqID = @rfqId AND SubcontractorID = @subId;
";

                var result = await conn.ExecuteAsync(query, new
                {
                    amount = totalAmount,
                    comment,
                    rfqId,
                    subId = subcontractorId
                });

                return result > 0;
            }
        }


        public async Task<object?> GetRfqResponsesByProjectAsync(Guid projectId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"
WITH LatestResponse AS (
    SELECT 
        r.*,
        ROW_NUMBER() OVER (
            PARTITION BY r.RfqID, r.SubcontractorID 
            ORDER BY r.ModifiedOn DESC
        ) AS rn
    FROM RfqSubcontractorResponse r
)
SELECT 
    wi.WorkItemID,
    wi.Name AS WorkItemName,
    rfq.RfqID,
    rfq.RfqNumber,               -- ✅ Add this
    rfq.DueDate AS DueDate, 
    s.SubcontractorID,
    s.Name AS SubcontractorName,
    ISNULL(s.Rating,0) AS Rating,
    rs.RfqResponseStatusName AS StatusName,
    lr.CreatedOn AS ResponseDate,
    CASE WHEN doc.RfqResponseDocumentID IS NULL THEN 0 ELSE 1 END AS HasDocument
FROM Projects p
INNER JOIN Rfq rfq ON rfq.ProjectID = p.ProjectID
INNER JOIN RfqWorkItemMapping wim ON wim.RfqID = rfq.RfqID
INNER JOIN WorkItems wi ON wi.WorkItemID = wim.WorkItemID
LEFT JOIN LatestResponse lr
    ON lr.RfqID = rfq.RfqID AND lr.rn = 1  
LEFT JOIN Subcontractors s 
    ON s.SubcontractorID = lr.SubcontractorID
LEFT JOIN RfqResponseStatus rs 
    ON rs.RfqResponseID = lr.RfqResponseID
LEFT JOIN RfqResponseDocuments doc
    ON doc.RfqID = rfq.RfqID AND doc.SubcontractorID = s.SubcontractorID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name, rfq.RfqID, s.Name;

";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return null;

            var result = rows
                .GroupBy(r => new { r.WorkItemID, r.WorkItemName, r.RfqID })
                .Select(g =>
                {
                    var subcontractors = g.Select(r =>
                    {
                        string status = r.StatusName ?? "Not Responded";

                        // ⭐ ONLY THIS LINE CHANGED
                        bool responded =
                            status == "Interested" ||
                            status == "Maybe Later" ||
                            status == "Not Interested";
                        bool interested = status == "Interested";
                        bool maybeLater = status == "Maybe Later";
                        bool notInterested = status == "Not Interested";

                        // ⭐ Viewed should be TRUE for all statuses that imply they saw the RFQ
                        bool viewed =
                            status == "Viewed" ||
                            interested ||
                            maybeLater ||
                            notInterested ||
                            responded;
                        return new
                        {
                            subcontractorId = r.SubcontractorID,
                            name = r.SubcontractorName,
                            rating = r.Rating,
                            date = r.ResponseDate?.ToString("dd/MM/yyyy") ?? "—",
                            rfqId = g.Key.RfqID.ToString(),

                            responded,
                            interested,
                            maybeLater,
                            viewed,
                            notInterested,

                            quote = "—",
                            dueDate = r.DueDate?.ToString("dd/MM/yyyy") ?? "—",
                            actions = new[] { "pdf", "chat" }
                        };
                    }).ToList();

                    return new
                    {
                        workItemId = g.Key.WorkItemID,
                        workItemName = g.Key.WorkItemName,
                        rfqId = g.Key.RfqID.ToString(),
                        subcontractors = subcontractors,
                        rfqNumber = g.First().RfqNumber,
                    };
                }).ToList();

            return result;
        }

        public async Task<object?> GetRfqResponsesByProjectSubcontractorAsync(Guid projectId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"
WITH LatestResponse AS (
    SELECT 
        r.*,
        ROW_NUMBER() OVER (
            PARTITION BY r.RfqID, r.SubcontractorID 
            ORDER BY r.ModifiedOn DESC
        ) AS rn
    FROM RfqSubcontractorResponse r
)
SELECT 
    wi.WorkItemID,
    wi.Name AS WorkItemName,
    rfq.RfqID,
    s.SubcontractorID,
    s.Name AS SubcontractorName,
    ISNULL(s.Rating,0) AS Rating,
    rs.RfqResponseStatusName AS StatusName,
    lr.CreatedOn AS ResponseDate,
    CASE WHEN doc.RfqResponseDocumentID IS NULL THEN 0 ELSE 1 END AS HasDocument
FROM Projects p
INNER JOIN Rfq rfq ON rfq.ProjectID = p.ProjectID
INNER JOIN RfqWorkItemMapping wim ON wim.RfqID = rfq.RfqID
INNER JOIN WorkItems wi ON wi.WorkItemID = wim.WorkItemID
LEFT JOIN LatestResponse lr
    ON lr.RfqID = rfq.RfqID AND lr.rn = 1  
LEFT JOIN Subcontractors s 
    ON s.SubcontractorID = lr.SubcontractorID
LEFT JOIN RfqResponseStatus rs 
    ON rs.RfqResponseID = lr.RfqResponseID
LEFT JOIN RfqResponseDocuments doc
    ON doc.RfqID = rfq.RfqID AND doc.SubcontractorID = s.SubcontractorID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name;

";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any())
                return null;

            // Flattened list — no grouping by subcontractor
            var result = rows.Select(r =>
            {
                string status = r.StatusName ?? "Not Responded";

                bool responded =
                           status == "Interested" ||
                           status == "Maybe Later" ||
                           status == "Not Interested";
                bool interested = status == "Interested";
                bool maybeLater = status == "Maybe Later";
                bool notInterested = status == "Not Interested";

                // ⭐ Viewed should be TRUE for all statuses that imply they saw the RFQ
                bool viewed =
                    status == "Viewed" ||
                    interested ||
                    maybeLater ||
                    notInterested ||
                    responded;

                return new
                {
                    workItemId = r.WorkItemID,
                    workItemName = r.WorkItemName,
                    rfqId = r.RfqID.ToString(),
                    rfqNumber = r.RfqNumber,
                    subcontractorId = r.SubcontractorID,
                    subcontractorName = r.SubcontractorName,
                    responded,
                    interested,
                    maybeLater,
                    viewed,
                    notInterested,
                    date = r.ResponseDate?.ToString("dd/MM/yyyy") ?? "—"
                };
            }).ToList();

            return result;
        }



        public async Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                // Ensure mapping exists
                await conn.ExecuteAsync(@"
            IF NOT EXISTS(
                SELECT 1 FROM RfqSubcontractorMapping
                WHERE RfqID = @RfqID AND SubcontractorID = @SubID
            )
            INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
            VALUES (@RfqID, @SubID);
        ", new { RfqID = rfqId, SubID = subcontractorId }, tx);

                await conn.ExecuteAsync(@"
            IF NOT EXISTS(
                SELECT 1 FROM RfqWorkItemMapping
                WHERE RfqID = @RfqID AND WorkItemID = @WorkItemID
            )
            INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
            VALUES (@RfqID, @WorkItemID);
        ", new { RfqID = rfqId, WorkItemID = workItemId }, tx);

                // Get "Viewed" ID
                var viewedId = await GetResponseIdAsync(conn, tx, "Viewed");

                // Insert into RfqSubcontractorResponse only if NOT exists already for same RFQ/Sub
                var existing = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM RfqSubcontractorResponse
            WHERE RfqID = @RfqID AND SubcontractorID = @SubID
        ", new { RfqID = rfqId, SubID = subcontractorId }, tx);

                if (existing == 0)
                {
                    await conn.ExecuteAsync(@"
                INSERT INTO RfqSubcontractorResponse
                (RfqSubcontractorResponseID, RfqID, SubcontractorID, RfqResponseID, CreatedOn)
                VALUES (NEWID(), @RfqID, @SubID, @ViewedID, GETUTCDATE());
            ", new { RfqID = rfqId, SubID = subcontractorId, ViewedID = viewedId }, tx);
                }

                tx.Commit();
                return true;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<decimal?> GetTotalQuoteAmountAsync(Guid rfqId, Guid subcontractorId)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var cmd = new SqlCommand(@"
            SELECT TotalQuoteAmount
            FROM RfqSubcontractorResponse
            WHERE RfqID = @RfqID AND SubcontractorID = @SubID
        ", conn);

                cmd.Parameters.AddWithValue("@RfqID", rfqId);
                cmd.Parameters.AddWithValue("@SubID", subcontractorId);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDecimal(result);

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }


        public async Task<bool> DeleteQuoteFile(Guid rfqId, Guid subcontractorId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
        DELETE FROM RfqResponseDocuments
        WHERE RfqID = @RfqID AND SubcontractorID = @SubID";

            var rows = await conn.ExecuteAsync(sql, new
            {
                RfqID = rfqId,
                SubID = subcontractorId
            });

            return rows > 0;
        }


    }
}
