using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

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
                    SELECT RfqResponseStatusID 
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
    RfqResponseStatusID,
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
                        SET RfqResponseStatusID = @RespID,
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


        public async Task<object?> GetProjectSummaryAsync(
      Guid rfqId,
      Guid? subcontractorId = null,
      List<Guid>? workItemIds = null,
      Guid? workItemId = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                if (!subcontractorId.HasValue)
                    throw new Exception("Missing subcontractorId.");

                if ((workItemIds == null || !workItemIds.Any()) && workItemId.HasValue)
                    workItemIds = new List<Guid> { workItemId.Value };

                // 1. Project
                const string projectSql = @"
SELECT 
    p.*,
    c.CustomerName,
    ppm.PersonID,
    per.Name  AS PersonName,
    per.Email AS PersonEmail,
    role.RoleName AS PersonRole
FROM Projects p
INNER JOIN Rfq r ON r.ProjectID = p.ProjectID
LEFT JOIN Customers c ON c.CustomerID = p.CustomerID
LEFT JOIN PersonProjectMapping ppm ON ppm.ProjectID = p.ProjectID
LEFT JOIN Persons per ON per.PersonID = ppm.PersonID
LEFT JOIN Roles role ON role.RoleID = ppm.RoleID
WHERE r.RfqID = @RfqID";
                var project = await conn.QueryFirstOrDefaultAsync<Project>(projectSql, new { RfqID = rfqId }, tran);

                if (project == null)
                {
                    tran.Rollback();
                    return null;
                }

                // 2. RFQ
                const string rfqSql = @"
SELECT 
    RfqID,
    RfqNumber,
    DueDate,
    GlobalDueDate,
    SentDate
FROM Rfq
WHERE RfqID = @RfqID";
                var rfq = await conn.QueryFirstOrDefaultAsync<Rfq>(rfqSql, new { RfqID = rfqId }, tran);

                // 3. Subcontractor-specific DueDate
                const string subDueSql = @"
SELECT TOP 1 DueDate
FROM dbo.RfqSubcontractorMapping
WHERE RfqID = @RfqID
  AND SubcontractorID = @SubcontractorID
ORDER BY CreatedOn DESC";
                var subcontractorDueDate = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                    subDueSql,
                    new { RfqID = rfqId, SubcontractorID = subcontractorId.Value },
                    tran
                );

                // Override DueDate with subcontractor-specific one if found
                if (rfq != null && subcontractorDueDate.HasValue)
                    rfq.DueDate = subcontractorDueDate.Value;

                // 4. Work Items
                string workItemSql = @"
SELECT 
    w.WorkItemID,
    w.Name,
    w.Number
FROM WorkItems w
INNER JOIN RfqWorkItemMapping rwim ON rwim.WorkItemID = w.WorkItemID
WHERE rwim.RfqID = @RfqID";
                if (workItemIds != null && workItemIds.Any())
                    workItemSql += " AND w.WorkItemID IN @WorkItemIds";

                var workItems = (await conn.QueryAsync<WorkItem>(
                    workItemSql,
                    new { RfqID = rfqId, WorkItemIds = workItemIds },
                    tran
                )).ToList();

                tran.Commit();

                return new
                {
                    Project = project,
                    Rfq = rfq, // <-- .DueDate is now individualized
                    WorkItems = workItems
                };
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }
        public async Task<List<dynamic>> GetPreviousSubmissionsAsync(
     Guid rfqId,
     Guid subcontractorId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var submissions = await conn.QueryAsync(@"
        SELECT
            r.RfqResponseDocumentID,
            r.RfqID,
            r.SubcontractorID,
            r.FileName,
            r.UploadedOn,
            s.Name AS SubcontractorName
        FROM RfqResponseDocuments r
        INNER JOIN Subcontractors s
            ON s.SubcontractorID = r.SubcontractorID
        WHERE r.RfqID = @RfqID
          AND r.SubcontractorID = @SubID
          AND (r.IsDeleted IS NULL OR r.IsDeleted = 0)
        ORDER BY r.UploadedOn DESC",
                new { RfqID = rfqId, SubID = subcontractorId });

            return submissions.ToList();
        }

        public async Task<bool> UploadQuoteAsync(
            Guid rfqId,
            Guid subcontractorId,
            Guid workItemId,
            IFormFile file,
            decimal totalAmount,
            string comment)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // 1️⃣ Validate RFQ ↔ Subcontractor mapping
            var mappingExists = await conn.ExecuteScalarAsync<int>(@"
        SELECT COUNT(1)
        FROM RfqSubcontractorMapping
        WHERE RfqID = @rfqId
          AND SubcontractorID = @subId;",
                new
                {
                    rfqId,
                    subId = subcontractorId
                });

            if (mappingExists == 0)
                throw new Exception("Subcontractor is not mapped to this RFQ.");

            // 2️⃣ Read file into byte array
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }
            var documentId = Guid.NewGuid();
            // 3️⃣ Insert quote document
            await conn.ExecuteAsync(@"
                INSERT INTO RfqResponseDocuments
                (
                    RfqResponseDocumentID,
                    RfqID,
                    SubcontractorID,
                    FileName,
                    FileData,
                    UploadedOn,
                    IsDeleted
                )
                VALUES
                (
                    @documentId,
                    @rfqId,
                    @subId,
                    @fileName,
                    @fileData,
                    GETDATE(),
                    0
                );",
                new
                {
                    documentId,
                    rfqId,
                    subId = subcontractorId,
                    fileName = file.FileName,
                    fileData = fileBytes
                });

            // 4️⃣ 🔥 INCREMENT QuoteReceived FOR THIS RFQ
            await conn.ExecuteAsync(@"
        UPDATE Rfq
        SET QuoteReceived = ISNULL(QuoteReceived, 0) + 1
        WHERE RfqID = @rfqId
          AND IsDeleted = 0;",
                new { rfqId });

            // 5️⃣ Update subcontractor response (ONLY this work item)
            await conn.ExecuteAsync(@"
        UPDATE RfqSubcontractorResponse
        SET
            TotalQuoteAmount = @amount,
            FileComment = @fileComment,
            ModifiedOn = GETDATE(),
            SubmissionCount = ISNULL(SubmissionCount, 0) + 1
        WHERE RfqID = @rfqId
          AND SubcontractorID = @subId
          AND WorkItemID = @workItemId;",
                new
                {
                    amount = totalAmount,
                    fileComment = comment,
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
                        SELECT
                            RfqID,
                            SubcontractorID,
                            MAX(RfqResponseDocumentID) AS DocumentId
                        FROM RfqResponseDocuments
                        WHERE IsDeleted = 0
                        GROUP BY RfqID, SubcontractorID
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
                    df.DocumentId AS DocumentId,

                    CASE WHEN df.DocumentId IS NULL THEN 0 ELSE 1 END AS HasDocument
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
                    LEFT JOIN RfqResponseStatus rs ON rs.RfqResponseStatusID = lr.RfqResponseStatusID
                    LEFT JOIN DocumentFlag df
                        ON df.RfqID = rfq.RfqID
                       AND df.SubcontractorID = s.SubcontractorID
                    WHERE p.ProjectID = @ProjectID
                    ORDER BY wi.Name, s.Name;
                    ";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return new List<object>();

            var result = rows
                .GroupBy(r => new
                {
                    WorkItemID = (Guid)r.WorkItemID,
                    WorkItemName = (string)r.WorkItemName,
                    RfqID = (Guid)r.RfqID,
                    RfqNumber = (string)r.RfqNumber
                })
                .Select(g => new
                {
                    workItemId = g.Key.WorkItemID,
                    workItemName = g.Key.WorkItemName,
                    rfqId = g.Key.RfqID.ToString(),
                    rfqNumber = g.Key.RfqNumber,

                    subcontractors = g.Select(row =>
                    {
                        DateTime? responseDate = row.ResponseDate as DateTime?;
                        DateTime rfqCreatedDate = (DateTime)row.RfqCreatedDate;

                        decimal? quoteAmount = row.TotalQuoteAmount as decimal?;
                        bool viewed = row.Viewed != null && row.Viewed == true;

                        string status = row.StatusName != null
                            ? row.StatusName.ToString()
                            : "Not Responded";

                        bool hasResponse =
                            responseDate.HasValue ||
                            row.HasDocument == 1 ||
                            quoteAmount.HasValue;

                        DateTime finalDate = responseDate ?? rfqCreatedDate;

                        return new
                        {
                            subcontractorId = (Guid)row.SubcontractorID,
                            name = (string)row.SubcontractorName,
                            rating = (int)row.Rating,
                            documentId = row.DocumentId,
                            date = finalDate.ToString("dd-MM-yyyy"),
                            rfqId = g.Key.RfqID.ToString(),

                            responded = hasResponse,
                            interested = hasResponse && status == "Interested",
                            maybeLater = hasResponse && status == "Maybe Later",
                            notInterested = hasResponse && status == "Not Interested",

                            viewed = viewed,

                            quote = quoteAmount?.ToString() ?? "—",
                            dueDate = row.DueDate != null
                                ? ((DateTime)row.DueDate).ToString("dd-MM-yyyy")
                                : "—",

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
LEFT JOIN RfqResponseStatus rs ON rs.RfqResponseStatusID = lr.RfqResponseStatusID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name, s.Name;
";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return new List<object>();

            return rows.Select(r =>
            {
                DateTime? responseDate = r.ResponseDate as DateTime?;
                DateTime rfqCreatedDate = (DateTime)r.RfqCreatedDate;

                string status = r.StatusName != null
                    ? r.StatusName.ToString()
                    : "Not Responded";

                bool hasResponse = responseDate.HasValue;
                DateTime finalDate = responseDate ?? rfqCreatedDate;

                return new
                {
                    workItemId = (Guid)r.WorkItemID,
                    workItemName = (string)r.WorkItemName,
                    rfqId = ((Guid)r.RfqID).ToString(),
                    rfqNumber = (string)r.RfqNumber,

                    subcontractorId = (Guid)r.SubcontractorID,
                    subcontractorName = (string)r.SubcontractorName,

                    responded = hasResponse,
                    interested = hasResponse && status == "Interested",
                    maybeLater = hasResponse && status == "Maybe Later",
                    notInterested = hasResponse && status == "Not Interested",

                    viewed = r.Viewed != null && r.Viewed == true,
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
                                RfqResponseStatusID,
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
            FROM dbo.RfqLogConversation
            WHERE ProjectID = @ProjectID

            UNION ALL

            SELECT SubcontractorID, MessageDateTime
            FROM dbo.RfqConversationMessage
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
