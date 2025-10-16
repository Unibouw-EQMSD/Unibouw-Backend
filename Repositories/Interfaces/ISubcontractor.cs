using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface ISubcontractor
    {
        Task<IEnumerable<Subcontractor>> GetAllAsync();
        Task<Subcontractor?> GetByIdAsync(Guid id);
    }
}
