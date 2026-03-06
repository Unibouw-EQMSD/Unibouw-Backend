namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IInboundMailService
    {
        Task ProcessNotificationAsync(string notificationJson);
    }
}
