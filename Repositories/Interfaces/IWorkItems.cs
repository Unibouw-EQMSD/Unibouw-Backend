using UnibouwAPI.Models;

public interface IWorkItems
{
    Task<IEnumerable<WorkItems>> GetAllAsync();
    Task<WorkItems?> GetByIdAsync(Guid id);
    Task<int> CreateAsync(WorkItems workItem);
    Task<int> UpdateAsync(WorkItems workItem);
    Task<int> UpdateIsActiveAsync(Guid id, bool isActive, string modifiedBy);
    Task<int> DeleteAsync(Guid id);
}
