namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfqEmailIngestionRepository
    {
        Task<List<string>> GetAllPersonMailboxesAsync();

        Task<DateTime> GetOrCreateCursorUtcAsync(string pmMailbox);
        Task UpdateCursorUtcAsync(string pmMailbox, DateTime utc);
        Task UpdateCursorRunAsync(string pmMailbox, string? error);

        Task InsertOutboundAnchorAsync(Guid projectId, Guid subcontractorId, Guid? rfqId,
            string pmMailbox, string? conversationId, string? internetMessageId, string? graphMessageId,
            DateTime sentUtc, string? subject);

        Task<bool> AnchorExistsAsync(Guid projectId, Guid subcontractorId, string pmMailbox, string conversationId);

        Task<bool> IsAlreadyIngestedAsync(string graphMessageId, string folder);
        Task MarkIngestedAsync(string pmMailbox, string graphMessageId, string folder, DateTime receivedUtc);

        Task InsertPmSentToConversationAsync(Guid projectId, Guid subcontractorId, string subject, string message, DateTime sentUtc, string pmMailbox);
        Task InsertInboundToConversationAsync(
    Guid projectId,
    Guid subcontractorId,
    string subject,
    string message,
    DateTime receivedAms,
    string fromEmail);
        Task InsertInboundToLogConversationAsync(Guid projectId, Guid subcontractorId, string subject,
            string message, DateTime receivedUtc, string fromEmail);
    }
}