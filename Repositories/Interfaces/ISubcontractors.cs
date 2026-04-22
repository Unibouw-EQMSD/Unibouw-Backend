using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface ISubcontractors
    {
        Task<IEnumerable<dynamic>> GetAllSubcontractor(bool onlyActive = false);
        Task<dynamic?> GetSubcontractorById(Guid id);
        Task<int> UpdateSubcontractorIsActive(Guid id, bool isActive, string modifiedBy);
        Task<bool> CreateSubcontractorWithMappings(Subcontractor subcontractor, string language);

        Task<Subcontractor> GetSubcontractorRemindersSent(Guid id);
        Task<int> UpdateSubcontractorRemindersSent(Guid id, int reminderSent);
        Task<int> DeleteSubcontractor(Guid id);
    }
}
