using UnibouwAPI.Services;
using UnibouwAPI.Helpers;

namespace UnibouwAPI.BackgroundServices
{
    public class DwhSyncBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DwhSyncBackgroundService> _logger;

        public DwhSyncBackgroundService(IServiceScopeFactory scopeFactory,ILogger<DwhSyncBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay;

                try
                {
                    delay = GetDelayUntilNext730AMAmsterdam();
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dwhService = scope.ServiceProvider.GetRequiredService<DwhTransferService>();
                    var result = await dwhService.SyncAllAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ DWH Sync failed.");
                    
                }
            }
        }


        private static TimeSpan GetDelayUntilNext730AMAmsterdam()
        {
            var amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

            var nextRun = new DateTime(
                amsterdamNow.Year,
                amsterdamNow.Month,
                amsterdamNow.Day,
                7, 30, 0
            );

            if (amsterdamNow >= nextRun)
                nextRun = nextRun.AddDays(1);

            return nextRun - amsterdamNow;
        }

        //For testing purpose
/* private static TimeSpan GetDelayUntilNext730AMAmsterdam()
        {
            var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            // Run after 2 minutes (testing only)
            var nextRun = istNow.AddMinutes(2);

            return nextRun - istNow;
        }*/

    }
}
