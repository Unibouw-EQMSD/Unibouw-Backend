using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfq
    {
        Task<IEnumerable<Rfq>> GetAllRfq();
        Task<Rfq?> GetRfqById(Guid id);
        Task<IEnumerable<Rfq>> GetRfqByProjectId(Guid projectId);
        Task<(string WorkItemNames, int SubCount)> GetWorkItemInfoByRfqId(Guid rfqId);
        Task<bool> UpdateRfqDueDate(Guid rfqId, DateTime dueDate, string modifiedBy);
        Task<Guid> CreateRfqAsync(Rfq rfq);
        Task InsertRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds);
        Task<bool> UpdateRfqAsync(Rfq rfq);
        Task UpdateRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds);
        Task UpdateRfqSubcontractorsAsync(Guid rfqId, List<Guid> subcontractorIds);
        Task EnsureGlobalSubcontractorWorkItemMapping(Guid workItemId, Guid subcontractorId);
        Task<IEnumerable<dynamic>> GetRfqSubcontractorDueDatesAsync(Guid rfqId);
    }
}
