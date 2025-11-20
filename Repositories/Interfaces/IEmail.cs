using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IEmail
    {
        Task<bool> SendRfqEmailAsync(EmailRequest request);
    }
}
