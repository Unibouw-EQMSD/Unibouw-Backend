using System.Threading.Tasks;
using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IEmailRepository
    {
        Task<bool> SendRfqEmailAsync(EmailRequest request);
    }
}
