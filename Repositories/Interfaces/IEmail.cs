using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IEmail
    {
        Task<bool> SendRfqEmailAsync(EmailRequest request);
        Task<bool> SendReminderEmailAsync(Guid subcontractorId, string email, string name, Guid rfqId);
    }
}
