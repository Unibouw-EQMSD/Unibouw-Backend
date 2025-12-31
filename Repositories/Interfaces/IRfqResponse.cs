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
        Task<List<SubcontractorLatestMessageDto>> GetSubcontractorsByLatestMessageAsync(Guid projectId);

        // For GET button click responses
        Task<bool> SaveResponseAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, string status);
        Task<object?> GetProjectSummaryAsync(Guid rfqId, Guid? subcontractorId = null, List<Guid>? workItemIds = null, Guid? workItemId = null);
        Task<List<RfqResponseDocument>> GetPreviousSubmissionsAsync(Guid rfqId, Guid subcontractorId);
        Task<bool> UploadQuoteAsync(Guid rfqId,Guid subcontractorId,Guid workItemId, IFormFile file,decimal totalAmount,string comment);
        Task<object?> GetRfqResponsesByProjectAsync(Guid projectId);
        Task<object?> GetRfqResponsesByProjectSubcontractorAsync(Guid projectId);

        Task<bool> MarkRfqViewedAsync(Guid rfqId, Guid subcontractorId, Guid workItemId);
        Task<decimal?> GetTotalQuoteAmountAsync(Guid rfqId, Guid subcontractorId, Guid workItemId);
        Task<bool> DeleteQuoteFile(Guid rfqId, Guid subcontractorId);

    }
}
