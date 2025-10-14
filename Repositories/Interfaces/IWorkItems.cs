using UnibouwAPI.Models;

public interface IWorkItems
{
    Task<IEnumerable<WorkItem>> GetAllAsync();
    Task<WorkItem?> GetByIdAsync(Guid id);
    Task<int> CreateAsync(WorkItem workItem);
    Task<int> UpdateAsync(WorkItem workItem);
    Task<int> UpdateIsActiveAsync(Guid id, bool isActive, string modifiedBy);
    Task<int> UpdateDescriptionAsync(Guid id, string description, string modifiedBy);
    Task<int> DeleteAsync(Guid id);
}
