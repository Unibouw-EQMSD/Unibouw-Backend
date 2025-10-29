using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IProjects
    {
        Task<IEnumerable<Project>> GetAllProject();
        Task<Project?> GetProjectById(Guid id);
    }
}
