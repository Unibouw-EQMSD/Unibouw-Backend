using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IProjectDocuments
    {
        Task<IEnumerable<ProjectDocumentDto>> GetProjectDocumentsAsync(Guid projectId);
        Task<IEnumerable<ProjectDocumentDto>> GetRfqDocumentsAsync(Guid rfqId);

        Task<UploadProjectDocResult> UploadAndLinkAsync(Guid projectId, Guid rfqId, string originalFileName, string contentType, byte[] bytes, string createdBy);
        Task LinkExistingDocsAsync(Guid rfqId, IEnumerable<Guid> projectDocumentIds, string linkedBy);

        Task DeleteProjectDocumentAsync(Guid projectDocumentId, string deletedBy); // Admin only; silent unlink
        Task<byte[]?> DownloadProjectDocumentAsync(Guid projectDocumentId); // optional
    }
}