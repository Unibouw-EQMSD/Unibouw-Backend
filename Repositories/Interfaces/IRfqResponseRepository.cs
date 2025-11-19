using System;
using System.Threading.Tasks;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfqResponseRepository
    {
        // For POST form/file submissions
        //Task<bool> SaveResponseWithOptionalFileAsync(
        //    Guid rfqId,
        //    Guid subcontractorId,
        //    Guid workItemId,
        //    string status,
        //    string? fileName = null,
        //    byte[]? fileData = null);

        // For GET button click responses
        Task<bool> SaveResponseAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, string status);
        Task<object?> GetProjectSummaryAsync(Guid rfqId, List<Guid>? workItemIds = null);

        Task<bool> UploadQuoteAsync(Guid rfqId, Guid subcontractorId, IFormFile file);
        Task<object?> GetRfqResponsesByProjectAsync(Guid projectId);

        // NEWLY ADDED
        Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId);
        Task<(byte[] FileBytes, string FileName)?> GetQuoteAsync(Guid rfqId, Guid subcontractorId);



    }
}
