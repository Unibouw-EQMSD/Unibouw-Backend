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
    }
}
