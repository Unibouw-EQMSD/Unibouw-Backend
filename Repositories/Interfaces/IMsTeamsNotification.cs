namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IMsTeamsNotification
    {
        Task SendTeamsMessageAsync(string message);
        Task SendRfqTeamsNotificationAsync(string rfqId, string client, string status);
    }
}
