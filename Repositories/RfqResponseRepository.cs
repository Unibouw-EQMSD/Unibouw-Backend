using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqResponseRepository : IRfqResponseRepository
    {
        private readonly string _connectionString;

        public RfqResponseRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("UnibouwDbConnection")
                                ?? throw new InvalidOperationException("Connection string missing");
        }

        private async Task<Guid> GetResponseIdAsync(SqlConnection connection, SqlTransaction transaction, string status)
        {
            var query = @"SELECT RfqResponseID 
                          FROM RfqResponseStatus 
                          WHERE RfqResponseStatusName = @Status";

            var responseId = await connection.QueryFirstOrDefaultAsync<Guid?>(query, new { Status = status }, transaction);
            return responseId ?? Guid.Parse("279E5265-5952-46FC-BF7B-53A1A3292865");
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

        // ✅ POST (Form/File submission)
        //    public async Task<bool> SaveResponseWithOptionalFileAsync(
        //Guid rfqId,
        //Guid subcontractorId,
        //Guid workItemId,
        //string status,
        //string? fileName,
        //byte[]? fileBytes)
        //    {
        //        try
        //        {
        //            using var conn = new SqlConnection(_connectionString);
        //            await conn.OpenAsync();
        //            using var transaction = conn.BeginTransaction();

        //            // 🔹 Validate RFQ & Subcontractor
        //            if (!await ValidateRfqAsync(conn, transaction, rfqId))
        //                throw new Exception($"RFQ {rfqId} does not exist.");

        //            if (!await ValidateSubcontractorAsync(conn, transaction, subcontractorId))
        //                throw new Exception($"Subcontractor {subcontractorId} does not exist.");

        //            // 1️⃣ Ensure RFQ-Subcontractor mapping exists
        //            var checkMappingCmd = new SqlCommand(@"
        //        IF NOT EXISTS (SELECT 1 FROM RfqSubcontractorMapping 
        //                       WHERE RfqID = @rfqId AND SubcontractorID = @subId)
        //        INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID, CreatedOn)
        //        VALUES (@rfqId, @subId, GETUTCDATE())",
        //                conn, transaction);
        //            checkMappingCmd.Parameters.AddWithValue("@rfqId", rfqId);
        //            checkMappingCmd.Parameters.AddWithValue("@subId", subcontractorId);
        //            await checkMappingCmd.ExecuteNonQueryAsync();

        //            // 2️⃣ Ensure RFQ-WorkItem mapping exists
        //            var checkWorkItemCmd = new SqlCommand(@"
        //        IF NOT EXISTS (SELECT 1 FROM RfqWorkItemMapping 
        //                       WHERE RfqID = @rfqId AND WorkItemID = @workItemId)
        //        INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID, CreatedOn)
        //        VALUES (@rfqId, @workItemId, GETUTCDATE())",
        //                conn, transaction);
        //            checkWorkItemCmd.Parameters.AddWithValue("@rfqId", rfqId);
        //            checkWorkItemCmd.Parameters.AddWithValue("@workItemId", workItemId);
        //            await checkWorkItemCmd.ExecuteNonQueryAsync();

        //            // 3️⃣ Map status → RfqResponseID
        //            var responseId = await GetResponseIdAsync(conn, transaction, status);

        //            // 4️⃣ Insert into RfqSubcontractorResponse
        //            var rfqSubcontractorResponseId = Guid.NewGuid();
        //            var insertSubResponse = new SqlCommand(@"
        //        INSERT INTO RfqSubcontractorResponse
        //            (RfqSubcontractorResponseID, RfqID, SubcontractorID, RfqResponseID, CreatedOn)
        //        VALUES (@id, @rfqId, @subId, @respId, GETUTCDATE())",
        //                conn, transaction);
        //            insertSubResponse.Parameters.AddWithValue("@id", rfqSubcontractorResponseId);
        //            insertSubResponse.Parameters.AddWithValue("@rfqId", rfqId);
        //            insertSubResponse.Parameters.AddWithValue("@subId", subcontractorId);
        //            insertSubResponse.Parameters.AddWithValue("@respId", responseId);
        //            await insertSubResponse.ExecuteNonQueryAsync();

        //            // 5️⃣ Insert into RfqSubcontractorWorkItemResponse
        //            var workItemResponseId = Guid.NewGuid(); // Correct WorkItemResponse ID
        //            var insertWorkItemResponse = new SqlCommand(@"
        //        INSERT INTO RfqSubcontractorWorkItemResponse
        //            (RfqSubcontractorWorkItemResponseID, RfqSubcontractorResponseID, RfqID, WorkItemID, 
        //             IsReviewed, CreatedOn)
        //        VALUES (@wid, @respId, @rfqId, @workItemId, 0, GETUTCDATE())",
        //                conn, transaction);
        //            insertWorkItemResponse.Parameters.AddWithValue("@wid", workItemResponseId);
        //            insertWorkItemResponse.Parameters.AddWithValue("@respId", rfqSubcontractorResponseId);
        //            insertWorkItemResponse.Parameters.AddWithValue("@rfqId", rfqId);
        //            insertWorkItemResponse.Parameters.AddWithValue("@workItemId", workItemId);
        //            await insertWorkItemResponse.ExecuteNonQueryAsync();

        //            // 6️⃣ Optionally insert document
        //            if (fileBytes != null && !string.IsNullOrEmpty(fileName))
        //            {
        //                var insertDocCmd = new SqlCommand(@"
        //            INSERT INTO RfqResponseDocuments
        //                (RfqResponseDocumentID, RfqSubcontractorWorkItemResponseID, RfqID, SubcontractorID, WorkItemID, 
        //                 FileName, FileData, UploadedOn)
        //            VALUES (@docId, @workItemRespId, @rfqId, @subId, @workItemId, @fileName, @fileData, GETUTCDATE())",
        //                    conn, transaction);

        //                insertDocCmd.Parameters.AddWithValue("@docId", Guid.NewGuid());
        //                insertDocCmd.Parameters.AddWithValue("@workItemRespId", workItemResponseId);
        //                insertDocCmd.Parameters.AddWithValue("@rfqId", rfqId);
        //                insertDocCmd.Parameters.AddWithValue("@subId", subcontractorId);
        //                insertDocCmd.Parameters.AddWithValue("@workItemId", workItemId);
        //                insertDocCmd.Parameters.AddWithValue("@fileName", fileName);
        //                insertDocCmd.Parameters.AddWithValue("@fileData", fileBytes);

        //                await insertDocCmd.ExecuteNonQueryAsync();
        //            }

        //            transaction.Commit();
        //            return true;
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine($"❌ Error saving response: {ex}");
        //            return false;
        //        }
        //    }
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



        // ✅ GET (Simple link click, no workItem/file)
        public async Task<bool> SaveResponseAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, string status)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (!await ValidateRfqAsync(connection, transaction, rfqId))
                    throw new Exception($"RFQ {rfqId} does not exist in database.");

                if (!await ValidateSubcontractorAsync(connection, transaction, subcontractorId))
                    throw new Exception($"Subcontractor {subcontractorId} does not exist in database.");

                // ✅ Ensure RfqSubcontractorMapping exists
                var mappingExists = await connection.QueryFirstOrDefaultAsync<int>(
                    @"SELECT 1 FROM RfqSubcontractorMapping 
              WHERE RfqID = @RfqID AND SubcontractorID = @SubcontractorID",
                    new { RfqID = rfqId, SubcontractorID = subcontractorId }, transaction);

                if (mappingExists == 0)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO RfqSubcontractorMapping (RfqID, SubcontractorID)
                  VALUES (@RfqID, @SubcontractorID)",
                        new { RfqID = rfqId, SubcontractorID = subcontractorId }, transaction);
                }

                // ✅ Ensure RfqWorkItemMapping exists (without CreatedOn)
                var workItemExists = await connection.QueryFirstOrDefaultAsync<int>(
                    @"SELECT 1 FROM RfqWorkItemMapping
              WHERE RfqID=@RfqID AND WorkItemID=@WorkItemID",
                    new { RfqID = rfqId, WorkItemID = workItemId }, transaction);

                if (workItemExists == 0)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO RfqWorkItemMapping (RfqID, WorkItemID)
                  VALUES (@RfqID, @WorkItemID)",
                        new { RfqID = rfqId, WorkItemID = workItemId }, transaction);
                }

                // ✅ Map status to RfqResponseID
                var responseId = await GetResponseIdAsync(connection, transaction, status);

                // ✅ Insert into RfqSubcontractorResponse
                var responseGuid = Guid.NewGuid();
                await connection.ExecuteAsync(
                    @"INSERT INTO RfqSubcontractorResponse 
              (RfqSubcontractorResponseID, RfqID, SubcontractorID, RfqResponseID, CreatedOn)
              VALUES (@RfqSubcontractorResponseID, @RfqID, @SubcontractorID, @RfqResponseID, GETUTCDATE())",
                    new
                    {
                        RfqSubcontractorResponseID = responseGuid,
                        RfqID = rfqId,
                        SubcontractorID = subcontractorId,
                        RfqResponseID = responseId
                    }, transaction);

                // ✅ Insert into RfqSubcontractorWorkItemResponse
                await connection.ExecuteAsync(
                    @"INSERT INTO RfqSubcontractorWorkItemResponse
              (RfqSubcontractorWorkItemResponseID, RfqSubcontractorResponseID, RfqID, WorkItemID, IsReviewed, CreatedOn)
              VALUES (@RfqSubcontractorWorkItemResponseID, @RfqSubcontractorResponseID, @RfqID, @WorkItemID, 0, GETUTCDATE())",
                    new
                    {
                        RfqSubcontractorWorkItemResponseID = Guid.NewGuid(),
                        RfqSubcontractorResponseID = responseGuid,
                        RfqID = rfqId,
                        WorkItemID = workItemId
                    }, transaction);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> UploadQuoteAsync(Guid rfqId, Guid subcontractorId, IFormFile file)
        {
            try
            {
                // 1️⃣ Save file to folder
                var folderPath = Path.Combine("Uploads", "RFQQuotes", rfqId.ToString());
                Directory.CreateDirectory(folderPath);

                var filePath = Path.Combine(folderPath, file.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 2️⃣ Read file data for DB
                byte[] fileBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await file.CopyToAsync(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                // 3️⃣ Insert into DB
                using (var connection = new SqlConnection(_connectionString))
                using (var command = new SqlCommand(@"
            INSERT INTO [dbo].[RfqResponseDocuments]
            (RfqResponseDocumentID, RfqID, SubcontractorID, FileName, FileData, UploadedOn)
            VALUES (@RfqResponseDocumentID, @RfqID, @SubcontractorID, @FileName, @FileData, @UploadedOn);
        ", connection))
                {
                    command.Parameters.AddWithValue("@RfqResponseDocumentID", Guid.NewGuid());
                    command.Parameters.AddWithValue("@RfqID", rfqId);
                    command.Parameters.AddWithValue("@SubcontractorID", subcontractorId);
                    command.Parameters.AddWithValue("@FileName", file.FileName);
                    command.Parameters.AddWithValue("@FileData", fileBytes);
                    command.Parameters.AddWithValue("@UploadedOn", DateTime.UtcNow);

                    await connection.OpenAsync();
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving uploaded quote: " + ex.Message);
                return false;
            }
        }

        public async Task<object?> GetRfqResponsesByProjectAsync(Guid projectId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"
SELECT 
    wi.WorkItemID,
    wi.Name AS WorkItemName,
    rfq.RfqID,
    s.SubcontractorID,
    s.Name AS SubcontractorName,
    ISNULL(s.Rating,0) AS Rating,
    rs.RfqResponseStatusName AS StatusName,
    r.CreatedOn AS ResponseDate
FROM Projects p
INNER JOIN Rfq rfq ON rfq.ProjectID = p.ProjectID
INNER JOIN RfqWorkItemMapping wim ON wim.RfqID = rfq.RfqID
INNER JOIN WorkItems wi ON wi.WorkItemID = wim.WorkItemID
LEFT JOIN RfqSubcontractorWorkItemResponse wr 
    ON wr.WorkItemID = wi.WorkItemID AND wr.RfqID = rfq.RfqID
LEFT JOIN RfqSubcontractorResponse r 
    ON r.RfqSubcontractorResponseID = wr.RfqSubcontractorResponseID
LEFT JOIN RfqResponseStatus rs 
    ON rs.RfqResponseID = r.RfqResponseID
LEFT JOIN Subcontractors s 
    ON s.SubcontractorID = r.SubcontractorID
WHERE p.ProjectID = @ProjectID
ORDER BY wi.Name, rfq.RfqID, s.Name;";

            var rows = (await conn.QueryAsync(sql, new { ProjectID = projectId })).ToList();
            if (!rows.Any()) return null;
            var result = rows
                .GroupBy(r => new { r.WorkItemID, r.WorkItemName, r.RfqID })
                .Select(g =>
                {
                    var subcontractors = g.Select(r =>
                    {
                        string status = r.StatusName ?? "Not Responded";

                        bool interested = status == "Interested";
                        bool viewed = status == "Viewed" || interested;
                        bool responded = status != "Not Responded";

                        return new
                        {
                            subcontractorId = r.SubcontractorID,
                            name = r.SubcontractorName,
                            rating = r.Rating,
                            date = r.ResponseDate?.ToString("dd/MM/yyyy") ?? "—",
                            rfqId = g.Key.RfqID.ToString(),

                            interested = interested,
                            viewed = viewed,
                            responded = responded,

                            quote = "—",
                            actions = new[] { "pdf", "chat" }
                        };
                    }).ToList();

                    return new
                    {
                        workItemId = g.Key.WorkItemID,
                        workItemName = g.Key.WorkItemName,
                        rfqId = g.Key.RfqID.ToString(),
                        subcontractors = subcontractors
                    };
                }).ToList();

            return result;
        }
        public async Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId)
        {
            using var conn = new SqlConnection(_connectionString);

            // Step 1: Fetch the status ID for "Viewed"
            var viewedStatusId = await conn.ExecuteScalarAsync<Guid?>(@"
        SELECT RfqResponseID
        FROM RfqResponseStatus
        WHERE RfqResponseStatusName = 'Viewed';
    ");

            if (viewedStatusId == null)
                throw new Exception("Viewed status not found in database.");

            // Step 2: Since RfqResponseStatus is a master table and cannot be updated,
            // we simply return true and let you use the ID in frontend/backend logic.
            return true;
        }

    }
}
