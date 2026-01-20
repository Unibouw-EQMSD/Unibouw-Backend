using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IEmail
    {
        //Task<bool> SendRfqEmailAsync(EmailRequest request);
        Task<List<EmailRequest>> SendRfqEmailAsync(EmailRequest request);

        Task<bool> SendReminderEmailAsync(
      Guid subcontractorId,
      string recipientEmail,
      string subcontractorName,
      Guid rfqId,
      string emailBody);

        // Task<bool> SendMailAsync(string toEmail, string subject, string body, string name);
        Task<bool> SendMailAsync(
    string toEmail,
    string subject,
    string body,
    string name,
    Guid? projectId,
    List<string>? attachmentFilePaths = null);
    }
}
