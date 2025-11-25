using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfqResponse
    {
        //-------RfqResponseDocuments
        Task<IEnumerable<RfqResponseDocument>> GetAllRfqResponseDocuments();
        Task<RfqResponseDocument?> GetRfqResponseDocumentsById(Guid id);


        //-------RfqSubcontractorResponse
        Task<IEnumerable<RfqSubcontractorResponse>> GetAllRfqSubcontractorResponse();
        Task<RfqSubcontractorResponse?> GetRfqSubcontractorResponseById(Guid id);


        //-------RfqSubcontractorWorkItemResponse
        Task<IEnumerable<RfqSubcontractorWorkItemResponse>> GetAllRfqSubcontractorWorkItemResponse();
        Task<RfqSubcontractorWorkItemResponse?> GetRfqSubcontractorWorkItemResponseById(Guid id);

        // For GET button click responses
        Task<bool> SaveResponseAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, string status);
        Task<object?> GetProjectSummaryAsync(Guid rfqId, List<Guid>? workItemIds = null);

        Task<bool> UploadQuoteAsync(Guid rfqId, Guid subcontractorId, IFormFile file);
        Task<object?> GetRfqResponsesByProjectAsync(Guid projectId);
        Task<object?> GetRfqResponsesByProjectSubcontractorAsync(Guid projectId);

        Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId);
        Task<(byte[] FileBytes, string FileName)?> GetQuoteAsync(Guid rfqId, Guid subcontractorId);

    }
}
