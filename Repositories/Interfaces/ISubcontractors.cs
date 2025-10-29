using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface ISubcontractors
    {
        Task<IEnumerable<Subcontractor>> GetAllSubcontractor();
        Task<Subcontractor?> GetSubcontractorById(Guid id);
        Task<int> UpdateSubcontractorIsActive(Guid id, bool isActive, string modifiedBy);

    }
}
