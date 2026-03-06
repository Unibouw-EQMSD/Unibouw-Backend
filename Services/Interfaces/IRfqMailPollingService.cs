namespace UnibouwAPI.Services.Interfaces
{
    public interface IRfqMailPollingService
    {
        Task PollOnceAsync(CancellationToken ct);
    }
}