using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRFQConversationMessage
    {
        Task<RFQConversationMessage> AddRFQConversationMessageAsync(RFQConversationMessage message);
        Task<IEnumerable<RFQConversationMessage>> GetMessagesByProjectAndSubcontractorAsync(Guid projectId,Guid subcontractorId);

        Task<RfqLogConversation> AddLogConversationAsync(RfqLogConversation logConversation);
        Task<IEnumerable<RfqLogConversation>> GetLogConversationsByProjectIdAsync(Guid projectId);

        Task<List<ConversationMessageDto>> GetConversationAsync(Guid projectId, Guid rfqId, Guid subcontractorId);

        Task<RFQConversationMessageAttachment> AddAttachmentAsync(RFQConversationMessageAttachment attachment);

        Task<RFQConversationMessage> ReplyToConversationAsync(
    Guid SubcontractorMessageID,
    string messageText,
    string subject,
    string pmEmail,
    List<IFormFile>? files = null);
    }
}
