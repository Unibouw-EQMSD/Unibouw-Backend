using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using Microsoft.Data.SqlClient;

namespace UnibouwAPI.Repositories
{
    public class ProjectDocumentsRepository : IProjectDocuments
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public ProjectDocumentsRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("UnibouwDbConnection")
                ?? throw new InvalidOperationException("Connection string 'UnibouwDbConnection' is not configured.");
        }

        private SqlConnection Connection => new SqlConnection(_connectionString);
        public async Task<IEnumerable<ProjectDocumentDto>> GetProjectDocumentsAsync(Guid projectId)
        {
            const string sql = @"
SELECT ProjectDocumentID, ProjectID, FileName, OriginalFileName, ContentType, SizeBytes, StoragePath, ChecksumSha256, CreatedOn, CreatedBy, IsDeleted
FROM ProjectDocument
WHERE ProjectID = @ProjectID AND IsDeleted = 0
ORDER BY CreatedOn DESC;";
            using var con = Connection;
            return await con.QueryAsync<ProjectDocumentDto>(sql, new { ProjectID = projectId });
        }

        public async Task<IEnumerable<ProjectDocumentDto>> GetRfqDocumentsAsync(Guid rfqId)
        {
            const string sql = @"
SELECT d.ProjectDocumentID, d.ProjectID, d.FileName, d.OriginalFileName, d.ContentType, d.SizeBytes, d.StoragePath, d.ChecksumSha256, d.CreatedOn, d.CreatedBy, d.IsDeleted
FROM RfqDocumentLink l
INNER JOIN ProjectDocument d ON d.ProjectDocumentID = l.ProjectDocumentID
WHERE l.RfqID = @RfqID AND d.IsDeleted = 0
ORDER BY l.LinkedOn DESC;";
            using var con = Connection;
            return await con.QueryAsync<ProjectDocumentDto>(sql, new { RfqID = rfqId });
        }

        public async Task<UploadProjectDocResult> UploadAndLinkAsync(
     Guid projectId,
     Guid rfqId,
     string originalFileName,
     string contentType,
     byte[] bytes,
     string createdBy)
        {
            if (projectId == Guid.Empty) throw new ArgumentException("ProjectID is required");
            if (rfqId == Guid.Empty) throw new ArgumentException("RFQID is required");
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("File bytes missing");

            var basePath = _configuration["DocumentStorage:BasePath"];
            if (string.IsNullOrWhiteSpace(basePath))
                throw new InvalidOperationException("DocumentStorage:BasePath missing");

            var checksum = DocumentStorage.ComputeSha256(bytes);

            using var con = Connection;
            await con.OpenAsync();

            using var tx = con.BeginTransaction();

            try
            {
                // 1) find duplicate within project
                const string findSql = @"
SELECT TOP 1 ProjectDocumentID, ProjectID, FileName, OriginalFileName, ContentType, SizeBytes, StoragePath, ChecksumSha256, CreatedOn, CreatedBy, IsDeleted
FROM ProjectDocument
WHERE ProjectID = @ProjectID AND ChecksumSha256 = @Checksum AND IsDeleted = 0;";

                var existing = await con.QueryFirstOrDefaultAsync<ProjectDocumentDto>(
                    findSql,
                    new { ProjectID = projectId, Checksum = checksum },
                    tx);

                bool isNew = existing == null;
                ProjectDocumentDto doc;

                if (!isNew)
                {
                    doc = existing!;
                }
                else
                {
                    var docId = Guid.NewGuid();

                    // ⚠️ moved OUTSIDE DB dependency but still inside flow
                    var storagePath = await DocumentStorage.SaveToDiskAsync(
                        basePath, projectId, docId, originalFileName, bytes);

                    doc = new ProjectDocumentDto
                    {
                        ProjectDocumentID = docId,
                        ProjectID = projectId,
                        FileName = Path.GetFileName(originalFileName),
                        OriginalFileName = Path.GetFileName(originalFileName),
                        ContentType = contentType,
                        SizeBytes = bytes.LongLength,
                        StoragePath = storagePath,
                        ChecksumSha256 = checksum,
                        CreatedBy = createdBy,
                        CreatedOn = DateTime.UtcNow,
                        IsDeleted = false
                    };

                    const string insertDoc = @"
INSERT INTO ProjectDocument
(ProjectDocumentID, ProjectID, FileName, OriginalFileName, ContentType, SizeBytes, StoragePath, ChecksumSha256, CreatedOn, CreatedBy, IsDeleted)
VALUES
(@ProjectDocumentID, @ProjectID, @FileName, @OriginalFileName, @ContentType, @SizeBytes, @StoragePath, @ChecksumSha256, SYSUTCDATETIME(), @CreatedBy, 0);";

                    await con.ExecuteAsync(insertDoc, doc, tx);
                }

                // 2) ALWAYS ensure RFQ link exists (even for duplicates)
                const string linkSql = @"
IF NOT EXISTS (
    SELECT 1 FROM RfqDocumentLink
    WHERE RfqID = @RfqID AND ProjectDocumentID = @ProjectDocumentID
)
BEGIN
    INSERT INTO RfqDocumentLink (RfqID, ProjectDocumentID, LinkedOn, LinkedBy)
    VALUES (@RfqID, @ProjectDocumentID, SYSUTCDATETIME(), @LinkedBy);
END";

                await con.ExecuteAsync(
                    linkSql,
                    new
                    {
                        RfqID = rfqId,
                        ProjectDocumentID = doc.ProjectDocumentID,
                        LinkedBy = createdBy
                    },
                    tx);

                tx.Commit();

                return new UploadProjectDocResult
                {
                    Document = doc,
                    IsNewForProject = isNew
                };
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        public async Task LinkExistingDocsAsync(Guid rfqId, IEnumerable<Guid> projectDocumentIds, string linkedBy)
        {
            if (rfqId == Guid.Empty) throw new ArgumentException("RFQID is required");
            var ids = (projectDocumentIds ?? Enumerable.Empty<Guid>()).Where(x => x != Guid.Empty).Distinct().ToList();
            if (!ids.Any()) return;

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM RfqDocumentLink WHERE RfqID = @RfqID AND ProjectDocumentID = @ProjectDocumentID)
BEGIN
    INSERT INTO RfqDocumentLink (RfqID, ProjectDocumentID, LinkedOn, LinkedBy)
    VALUES (@RfqID, @ProjectDocumentID, SYSUTCDATETIME(), @LinkedBy);
END";

            using var con = Connection;
            await con.OpenAsync();
            using var tx = con.BeginTransaction();
            foreach (var id in ids)
            {
                await con.ExecuteAsync(sql, new { RfqID = rfqId, ProjectDocumentID = id, LinkedBy = linkedBy }, tx);
            }
            tx.Commit();
        }



        public async Task DeleteProjectDocumentAsync(Guid projectDocumentId, string deletedBy)
        {
            if (projectDocumentId == Guid.Empty) throw new ArgumentException("ProjectDocumentID is required");

            const string softDelete = @"
UPDATE ProjectDocument
SET IsDeleted = 1, DeletedOn = SYSUTCDATETIME(), DeletedBy = @DeletedBy
WHERE ProjectDocumentID = @ProjectDocumentID AND IsDeleted = 0;";

            const string unlink = @"
DELETE FROM RfqDocumentLink WHERE ProjectDocumentID = @ProjectDocumentID;";

            using var con = Connection;
            await con.OpenAsync();
            using var tx = con.BeginTransaction();

            await con.ExecuteAsync(softDelete, new { ProjectDocumentID = projectDocumentId, DeletedBy = deletedBy }, tx);
            await con.ExecuteAsync(unlink, new { ProjectDocumentID = projectDocumentId }, tx);

            // IMPORTANT: no notifications here (AC12)
            tx.Commit();
        }

        public async Task<(byte[] FileBytes, string FileName, string ContentType)?> DownloadProjectDocumentAsync(Guid projectDocumentId)
        {
            const string sql = @"
SELECT FileName, ContentType, StoragePath
FROM ProjectDocument
WHERE ProjectDocumentID = @ProjectDocumentID AND IsDeleted = 0;";
            using var con = Connection;
            var doc = await con.QueryFirstOrDefaultAsync<(string FileName, string ContentType, string StoragePath)>(
                sql, new { ProjectDocumentID = projectDocumentId });
            if (doc == default || string.IsNullOrWhiteSpace(doc.StoragePath) || !File.Exists(doc.StoragePath))
                return null;

            var fileBytes = await File.ReadAllBytesAsync(doc.StoragePath);
            return (fileBytes, doc.FileName ?? "document.pdf", doc.ContentType ?? "application/octet-stream");
        }
    }
}