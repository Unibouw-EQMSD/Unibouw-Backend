using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IProjects
    {
        Task<IEnumerable<Project>> GetAllProject(string loggedInEmail, string role);
        Task<Project?> GetProjectById(Guid id, string loggedInEmail, string role);
    }
}
