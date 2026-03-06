using UnibouwAPI.Services.Interfaces;

namespace UnibouwAPI.Workers
{
    public class RfqMailPollingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<RfqMailPollingWorker> _logger;

        public RfqMailPollingWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RfqMailPollingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogWarning("RFQ Poller tick (UTC): {Time}", DateTime.UtcNow);

            _logger.LogInformation("RFQ Poller tick at {TimeUtc}", DateTime.UtcNow);
            var interval = int.TryParse(_config["MailPolling:IntervalMinutes"], out var m) ? m : 2;

            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IRfqMailPollingService>();

                try
                {
                    Console.WriteLine("[WORKER] calling PollOnceAsync...");
                    _logger.LogInformation("PollOnceAsync started");
                    await svc.PollOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Polling cycle failed");
                }

                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
            }
        }
    }
}