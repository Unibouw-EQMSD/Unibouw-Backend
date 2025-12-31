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
                if (!await ValidateRfqAsync(connection, transaction, rfqId))
                    throw new Exception("Invalid RFQ");

                if (!await ValidateSubcontractorAsync(connection, transaction, subcontractorId))
                    throw new Exception("Invalid Subcontractor");

                await connection.ExecuteAsync(@"
                        IF NOT EXISTS (
                            SELECT 1 FROM RfqSubcontractorMapping
                            WHERE RfqID = @RfqID AND SubcontractorID = @SubID
                        )
                        INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
                        VALUES (@RfqID, @SubID);",
                new { RfqID = rfqId, SubID = subcontractorId }, transaction);

                await connection.ExecuteAsync(@"
                    IF NOT EXISTS (
                        SELECT 1 FROM RfqWorkItemMapping
                        WHERE RfqID = @RfqID AND WorkItemID = @WorkItemID
                    )
                    INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
                    VALUES (@RfqID, @WorkItemID);",
                new { RfqID = rfqId, WorkItemID = workItemId }, transaction);

                var responseId = await GetResponseIdAsync(connection, transaction, status);

                var existing = await connection.QuerySingleOrDefaultAsync<Guid?>(@"
                    SELECT RfqSubcontractorResponseID
                    FROM RfqSubcontractorResponse
                    WHERE RfqID = @RfqID
                      AND SubcontractorID = @SubID
                      AND WorkItemID = @WorkItemID",
                new
                {
                    RfqID = rfqId,
                    SubID = subcontractorId,
                    WorkItemID = workItemId
                }, transaction);

                if (existing == null)
                {
                    await connection.ExecuteAsync(@"
                            INSERT INTO RfqSubcontractorResponse
(
    RfqSubcontractorResponseID,
    RfqID,
    SubcontractorID,
    WorkItemID,
    RfqResponseID,
    CreatedOn
)
VALUES
(
    NEWID(),
    @RfqID,
    @SubID,
    @WorkItemID,
    @RespID,
    GETDATE()
);",
                    new
                    {
                        RfqID = rfqId,
                        SubID = subcontractorId,
                        WorkItemID = workItemId,
                        RespID = responseId
                    }, transaction);
                }
                else
                {
                    await connection.ExecuteAsync(@"
                        UPDATE RfqSubcontractorResponse
                        SET RfqResponseID = @RespID,
                            ModifiedOn = GETUTCDATE()
                        WHERE RfqSubcontractorResponseID = @ID;",
                    new
                    {
                        ID = existing.Value,
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


        public async Task<object?> GetProjectSummaryAsync(Guid rfqId, Guid? subcontractorId = null, List<Guid>? workItemIds = null, Guid? workItemId = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                if (!subcontractorId.HasValue)
                    throw new Exception("Missing subcontractorId.");

                if ((workItemIds == null || !workItemIds.Any()) && workItemId.HasValue)
                {
                    workItemIds = new List<Guid> { workItemId.Value };
                }

                /* ---------------- PROJECT ---------------- */
                const string projectSql = @"
    SELECT 
    p.ProjectID,
    p.Name,
    p.Number,
    p.Company,
    p.StartDate,
    p.CompletionDate,
    p.Status,
    p.SharepointURL,

    p.CustomerID,
    c.CustomerName,

    p.ProjectManagerID,
    pm.ProjectManagerName,
    pm.Email AS ProjectManagerEmail

FROM Projects p
INNER JOIN Rfq r 
    ON r.ProjectID = p.ProjectID

LEFT JOIN Customers c
    ON c.CustomerID = p.CustomerID

LEFT JOIN ProjectManagers pm
    ON pm.ProjectManagerID = p.ProjectManagerID

WHERE r.RfqID = @RfqID";

                var project = await conn.QueryFirstOrDefaultAsync<Project>(
                    projectSql,
                    new { RfqID = rfqId },
                    tran
                );

                if (project == null)
                {
                    tran.Rollback();
                    return null;
                }

                /* ---------------- RFQ (CRITICAL) ---------------- */
                const string rfqSql = @"
                    SELECT 
                        RfqID,
                        RfqNumber,
                        DueDate,
                        GlobalDueDate,
                        SentDate
                    FROM Rfq
                    WHERE RfqID = @RfqID";

                var rfq = await conn.QueryFirstOrDefaultAsync<Rfq>(
                    rfqSql,
                    new { RfqID = rfqId },
                    tran
                );

                /* ---------------- WORK ITEMS ---------------- */
                string workItemSql = @"
                    SELECT 
                        w.WorkItemID,
                        w.Name,
                        w.Number
                    FROM WorkItems w
                    INNER JOIN RfqWorkItemMapping rwim 
                        ON rwim.WorkItemID = w.WorkItemID
                    WHERE rwim.RfqID = @RfqID";

                if (workItemIds != null && workItemIds.Any())
                    workItemSql += " AND w.WorkItemID IN @WorkItemIds";

                var workItems = (await conn.QueryAsync<WorkItem>(
                    workItemSql,
                    new { RfqID = rfqId, WorkItemIds = workItemIds },
                    tran
                )).ToList();

                tran.Commit();

                /* ---------------- RETURN EVERYTHING ---------------- */
                return new
                {
                    Project = project,
                    Rfq = rfq,
                    WorkItems = workItems
                };
            }
            catch
            {
                tran.Rollback();
                throw;
            }
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
                ORDER BY UploadedOn DESC",
             new { RfqID = rfqId, SubID = subcontractorId });

            return submissions.ToList();
        }


        public async Task<bool> UploadQuoteAsync(
       Guid rfqId,
       Guid subcontractorId,
       Guid workItemId,   // ✅ ADD THIS
       IFormFile file,
       decimal totalAmount,
       string comment)
        {
            using var conn = new SqlConnection(_connectionString);

            // Validate mapping (RFQ + Subcontractor is fine)
            var mappingExists = await conn.ExecuteScalarAsync<int>(@"
        SELECT COUNT(1)
        FROM RfqSubcontractorMapping
        WHERE RfqID = @rfqId AND SubcontractorID = @subId;",
                new { rfqId, subId = subcontractorId });

            if (mappingExists == 0)
                throw new Exception("Subcontractor is not mapped to this RFQ.");

            // Read file
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            // Insert document
            await conn.ExecuteAsync(@"
        INSERT INTO RfqResponseDocuments
        (RfqID, SubcontractorID, FileName, FileData, UploadedOn, IsDeleted)
        VALUES
        (@rfqId, @subId, @fileName, @fileData, GETDATE(), 0)",
                new
                {
                    rfqId,
                    subId = subcontractorId,
                    fileName = file.FileName,
                    fileData = fileBytes
                });

            // ✅ CRITICAL FIX — UPDATE ONLY ONE WORK ITEM
            await conn.ExecuteAsync(@"
        UPDATE RfqSubcontractorResponse
        SET TotalQuoteAmount = @amount,
            Comment = @comment,
            ModifiedOn = GETDATE(),
            SubmissionCount = ISNULL(SubmissionCount, 0) + 1
        WHERE RfqID = @rfqId
          AND SubcontractorID = @subId
          AND WorkItemID = @workItemId;",
                new
                {
                    amount = totalAmount,
                    comment,
                    rfqId,
                    subId = subcontractorId,
                    workItemId
                });

            return true;
        }


        public async Task<object?> GetRfqResponsesByProjectAsync(Guid projectId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"
WITH LatestResponse AS (
    SELECT 
        r.*,
        ROW_NUMBER() OVER (
            PARTITION BY r.RfqID, r.SubcontractorID, r.WorkItemID
            ORDER BY ISNULL(r.ModifiedOn, r.CreatedOn) DESC
        ) AS rn
    FROM RfqSubcontractorResponse r
),
DocumentFlag AS (
    SELECT DISTINCT
        RfqID,
        SubcontractorID
    FROM RfqResponseDocuments
)
SELECT 
    wi.WorkItemID,
    wi.Name AS WorkItemName,
    rfq.RfqID,
    rfq.RfqNumber,
    rfq.CreatedOn AS RfqCreatedDate,
    rfq.DueDate,

    s.SubcontractorID,
    s.Name AS SubcontractorName,
    ISNULL(s.Rating,0) AS Rating,

    rs.RfqResponseStatusName AS StatusName,
    lr.CreatedOn AS ResponseDate,
    lr.Viewed,
    lr.TotalQuoteAmount,

    CASE WHEN df.SubcontractorID IS NULL THEN 0 ELSE 1 END AS HasDocument
FROM Projects p
INNER JOIN Rfq rfq ON rfq.ProjectID = p.ProjectID
INNER JOIN RfqWorkItemMapping wim ON wim.RfqID = rfq.RfqID
INNER JOIN WorkItems wi ON wi.WorkItemID = wim.WorkItemID
INNER JOIN RfqSubcontractorMapping rsm ON rsm.RfqID = rfq.RfqID
INNER JOIN Subcontractors s ON s.SubcontractorID = rsm.SubcontractorID
LEFT JOIN LatestResponse lr
    ON lr.RfqID = rfq.RfqID
   AND lr.SubcontractorID = s.SubcontractorID
   AND lr.WorkItemID = wi.WorkItemID
   AND lr.rn = 1
LEFT JOIN RfqResponseStatus rs ON rs.RfqResponseID = lr.RfqResponseID
LEFT JOIN DocumentFlag df
    ON df.RfqID = rfq.RfqID
   AND df.SubcontractorID = s.SubcontractorID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name, s.Name;
";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return null;

            var result = rows
                .GroupBy(r => new { r.WorkItemID, r.WorkItemName, r.RfqID, r.RfqNumber })
                .Select(g => new
                {
                    workItemId = g.Key.WorkItemID,
                    workItemName = g.Key.WorkItemName,
                    rfqId = g.Key.RfqID.ToString(),
                    rfqNumber = g.Key.RfqNumber,

                    subcontractors = g.Select(row =>
                    {
                        string status = row.StatusName ?? "Not Responded";

                        bool hasResponse =
                            row.ResponseDate != DateTime.MinValue ||
                            row.HasDocument == 1 ||
                            row.TotalQuoteAmount != null;

                        // ✅ SAFE DATE PICK
                        var finalDate =
                            row.ResponseDate != DateTime.MinValue
                                ? row.ResponseDate
                                : row.RfqCreatedDate;

                        return new
                        {
                            subcontractorId = row.SubcontractorID,
                            name = row.SubcontractorName,
                            rating = row.Rating,

                            date = finalDate.ToString("dd-MM-yyyy"),

                            rfqId = g.Key.RfqID.ToString(),

                            responded = hasResponse,
                            interested = hasResponse && status == "Interested",
                            maybeLater = hasResponse && status == "Maybe Later",
                            notInterested = hasResponse && status == "Not Interested",

                            viewed = row.Viewed == true,

                            quote = row.TotalQuoteAmount != null
                                ? row.TotalQuoteAmount.ToString()
                                : "—",

                            dueDate = row.DueDate?.ToString("dd-MM-yyyy") ?? "—",
                            actions = new[] { "pdf", "chat" }
                        };
                    }).ToList()
                })
                .ToList();

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
            PARTITION BY r.RfqID, r.SubcontractorID, r.WorkItemID
            ORDER BY ISNULL(r.ModifiedOn, r.CreatedOn) DESC
        ) AS rn
    FROM RfqSubcontractorResponse r
)
SELECT 
    wi.WorkItemID,
    wi.Name AS WorkItemName,
    rfq.RfqID,
    rfq.RfqNumber,
    rfq.CreatedOn AS RfqCreatedDate,

    s.SubcontractorID,
    s.Name AS SubcontractorName,
    rs.RfqResponseStatusName AS StatusName,
    lr.CreatedOn AS ResponseDate,
    lr.Viewed
FROM Projects p
INNER JOIN Rfq rfq ON rfq.ProjectID = p.ProjectID
INNER JOIN RfqWorkItemMapping wim ON wim.RfqID = rfq.RfqID
INNER JOIN WorkItems wi ON wi.WorkItemID = wim.WorkItemID
INNER JOIN RfqSubcontractorMapping rsm ON rsm.RfqID = rfq.RfqID
INNER JOIN Subcontractors s ON s.SubcontractorID = rsm.SubcontractorID
LEFT JOIN LatestResponse lr
    ON lr.RfqID = rfq.RfqID
   AND lr.SubcontractorID = s.SubcontractorID
   AND lr.WorkItemID = wi.WorkItemID
   AND lr.rn = 1
LEFT JOIN RfqResponseStatus rs ON rs.RfqResponseID = lr.RfqResponseID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name, s.Name;
";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return null;

            return rows.Select(r =>
            {
                string status = r.StatusName ?? "Not Responded";
                bool hasResponse = r.ResponseDate != DateTime.MinValue;

                var finalDate =
                    r.ResponseDate != DateTime.MinValue
                        ? r.ResponseDate
                        : r.RfqCreatedDate;

                return new
                {
                    workItemId = r.WorkItemID,
                    workItemName = r.WorkItemName,
                    rfqId = r.RfqID.ToString(),
                    rfqNumber = r.RfqNumber,
                    subcontractorId = r.SubcontractorID,
                    subcontractorName = r.SubcontractorName,

                    responded = hasResponse,
                    interested = hasResponse && status == "Interested",
                    maybeLater = hasResponse && status == "Maybe Later",
                    notInterested = hasResponse && status == "Not Interested",

                    viewed = r.Viewed == true,

                    date = finalDate.ToString("dd/MM/yyyy")
                };
            }).ToList();
        }
        public async Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            try
            {
                var viewedId = await GetResponseIdAsync(conn, tx, "Viewed");

                var existing = await conn.QuerySingleOrDefaultAsync<Guid?>(@"
                    SELECT RfqSubcontractorResponseID
                    FROM RfqSubcontractorResponse
                    WHERE RfqID = @RfqID
                      AND SubcontractorID = @SubID
                      AND WorkItemID = @WorkItemID",
                    new
                    {
                        RfqID = rfqId,
                        SubID = subcontractorId,
                        WorkItemID = workItemId
                    }, tx);

                if (existing == null)
                {
                    await conn.ExecuteAsync(@"
                            INSERT INTO RfqSubcontractorResponse
                            (
                                RfqSubcontractorResponseID,
                                RfqID,
                                SubcontractorID,
                                WorkItemID,
                                RfqResponseID,
                                Viewed,
                                ViewedOn,
                                CreatedOn
                            )
                            VALUES
                            (
                                NEWID(),
                                @RfqID,
                                @SubID,
                                @WorkItemID,
                                @ViewedID,
                                1,
                                GETUTCDATE(),
                                GETUTCDATE()
                            );",
                        new
                        {
                            RfqID = rfqId,
                            SubID = subcontractorId,
                            WorkItemID = workItemId,
                            ViewedID = viewedId
                        }, tx);
                }
                else
                {
                    await conn.ExecuteAsync(@"
                            UPDATE RfqSubcontractorResponse
                            SET Viewed = 1,
                                ViewedOn = GETUTCDATE(),
                                ModifiedOn = GETUTCDATE()
                            WHERE RfqSubcontractorResponseID = @ID",
                        new { ID = existing.Value }, tx);
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


        public async Task<decimal?> GetTotalQuoteAmountAsync(Guid rfqId, Guid subcontractorId, Guid workItemId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
        SELECT TotalQuoteAmount
        FROM RfqSubcontractorResponse
        WHERE RfqID = @RfqID AND SubcontractorID = @SubID AND WorkItemID = @WorkItemID
    ", conn);

            cmd.Parameters.AddWithValue("@RfqID", rfqId);
            cmd.Parameters.AddWithValue("@SubID", subcontractorId);
            cmd.Parameters.AddWithValue("@WorkItemID", workItemId);

            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                return Convert.ToDecimal(result);

            return null;
        }

        public async Task<bool> DeleteQuoteFile(Guid rfqId, Guid subcontractorId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"DELETE FROM RfqResponseDocuments WHERE RfqID = @RfqID AND SubcontractorID = @SubID";

            var rows = await conn.ExecuteAsync(sql, new
            {
                RfqID = rfqId,
                SubID = subcontractorId
            });

            return rows > 0;
        }

        public async Task<List<SubcontractorLatestMessageDto>> GetSubcontractorsByLatestMessageAsync(Guid projectId)
        {
            const string sql = @"
        WITH AllMessages AS (
            SELECT SubcontractorID, MessageDateTime
            FROM dbo.LogConversation
            WHERE ProjectID = @ProjectID

            UNION ALL

            SELECT SubcontractorID, MessageDateTime
            FROM dbo.RFQConversationMessage
            WHERE ProjectID = @ProjectID
        ),
        LatestMessages AS (
            SELECT
                SubcontractorID,
                MAX(MessageDateTime) AS LatestMessageDateTime
            FROM AllMessages
            GROUP BY SubcontractorID
        )
        SELECT
            lm.SubcontractorID,
            s.Name AS SubcontractorName,
            lm.LatestMessageDateTime
        FROM LatestMessages lm
        INNER JOIN dbo.Subcontractors s
            ON s.SubcontractorID = lm.SubcontractorID
        ORDER BY lm.LatestMessageDateTime DESC;
    ";

            using var connection = new SqlConnection(_connectionString);

            var result = await connection.QueryAsync<SubcontractorLatestMessageDto>(
                sql,
                new { ProjectID = projectId }
            );

            return result.ToList();
        }


    }
}
