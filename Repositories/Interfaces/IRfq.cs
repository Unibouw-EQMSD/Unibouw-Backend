using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfq
    {
        Task<IEnumerable<Rfq>> GetAllRfq();
        Task<Rfq?> GetRfqById(Guid id);
        Task<Rfq?> GetRfqByProjectId(Guid projectId);
    }
}
