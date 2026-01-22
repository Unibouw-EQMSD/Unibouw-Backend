using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfq
    {
        Task<IEnumerable<Rfq>> GetAllRfq();

        Task<Rfq?> GetRfqById(Guid id);
        Task<IEnumerable<WorkItem>> GetRfqWorkItemsAsync(Guid rfqId);
        Task<IEnumerable<Rfq>> GetRfqByProjectId(Guid projectId);

        Task<(string WorkItemNames, int SubCount)> GetWorkItemInfoByRfqId(Guid rfqId);

        Task<bool> UpdateRfqDueDate(Guid rfqId, DateTime dueDate, string modifiedBy);

        Task<Guid> CreateRfqAsync(Rfq rfq, List<Guid> subcontractorIds);

        Task SaveRfqSubcontractorDueDateAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate);

        Task InsertRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds);

        Task<bool> UpdateRfqAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate);

        Task UpdateRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds);

        Task UpdateRfqSubcontractorsAsync(Guid rfqId, List<Guid> subcontractorIds);

        Task EnsureGlobalSubcontractorWorkItemMapping(Guid workItemId, Guid subcontractorId);

        Task<IEnumerable<dynamic>> GetRfqSubcontractorDueDatesAsync(Guid rfqId);

        Task<bool?> DeleteRfqAsync(Guid rfqId, string deletedBy);

        Task SaveSubcontractorWorkItemMappingAsync(Guid subcontractorId, Guid workItemId, string createdBy);

        Task<bool> SaveOrUpdateRfqSubcontractorMappingAsync(Guid rfqId, Guid subcontractorId, Guid workItemId, DateTime dueDate, string user);

        Task<bool> UpdateRfqSubcontractorDueDateAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate);

        Task<bool> UpdateRfqMainAsync(Rfq rfq);

        Task<bool> UpdateSubcontractorDueDateAsync(Guid rfqId, Guid subcontractorId, DateTime dueDate);

    }
}

