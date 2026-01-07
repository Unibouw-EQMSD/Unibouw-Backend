using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRFQConversationMessage
    {
        Task<RFQConversationMessage> AddRFQConversationMessageAsync(RFQConversationMessage message);
        Task<IEnumerable<RFQConversationMessage>> GetMessagesByProjectAndSubcontractorAsync(Guid projectId,Guid subcontractorId);

        Task<LogConversation> AddLogConversationAsync(LogConversation logConversation);
        Task<IEnumerable<LogConversation>> GetLogConversationsByProjectIdAsync(Guid projectId);

        Task<List<ConversationMessageDto>> GetConversationAsync(Guid projectId, Guid rfqId, Guid subcontractorId);

        Task<RFQConversationMessageAttachment> AddAttachmentAsync(RFQConversationMessageAttachment attachment);

        Task<RFQConversationMessage> ReplyToConversationAsync(Guid parentMessageId, string messageText, string subject, string pmEmail);

    }
}
