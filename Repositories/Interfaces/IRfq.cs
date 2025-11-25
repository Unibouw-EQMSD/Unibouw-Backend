using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfq
    {
        Task<IEnumerable<Rfq>> GetAllRfq();
        Task<Rfq?> GetRfqById(Guid id);
        Task<Rfq?> GetRfqByProjectId(Guid projectId);
        Task<(string WorkItemName, int SubCount)> GetWorkItemInfoByRfqId(Guid rfqId);
        Task<bool> UpdateRfqDueDate(Guid rfqId, DateTime dueDate, string modifiedBy);
        Task<Guid> CreateRfqAsync(Rfq rfq);
        Task InsertRfqWorkItemsAsync(Guid rfqId, List<Guid> workItemIds);
    }
}
