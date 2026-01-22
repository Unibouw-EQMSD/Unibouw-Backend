using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Services
{
    public class RfqReminderBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RfqReminderBackgroundService> _logger;

        public RfqReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RfqReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessReminders(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reminder scheduler failed.");
                }

                // Runs every minute for accuracy
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }


       /* private async Task ProcessReminders(CancellationToken stoppingToken)
        {
            var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "India Standard Time");

            DateTime currentDateTime = DateTime.Now;    // current date + time

            DateTime dateHoursMinsOnly = new DateTime(
                currentDateTime.Year,
                currentDateTime.Month,
                currentDateTime.Day,
                currentDateTime.Hour,
                currentDateTime.Minute,
                0
            );

            using var scope = _scopeFactory.CreateScope();

            var reminderRepo = scope.ServiceProvider.GetRequiredService<IRfqReminderSet>();
            var subRepo = scope.ServiceProvider.GetRequiredService<ISubcontractors>();
            var emailRepo = scope.ServiceProvider.GetRequiredService<IEmail>();

            var reminders = await reminderRepo.GetPendingReminders(dateHoursMinsOnly);

            _logger.LogInformation("Reminder triggered at {Time}. Found {Count} reminders.", now.ToString("HH:mm"), reminders.Count());

            foreach (var r in reminders)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    var sub = await subRepo.GetSubcontractorById(r.SubcontractorID);
                    if (sub == null) continue;

                    await emailRepo.SendReminderEmailAsync(r.SubcontractorID, sub.EmailID, sub.Name, r.RfqID, r.ReminderEmailBody);

                    await reminderRepo.MarkReminderSent(r.RfqReminderSetID, currentDateTime);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed sending reminder. RfqReminderSetID={Id}", r.RfqReminderSetID);
                }
            }
        }*/

        private async Task ProcessReminders(CancellationToken stoppingToken)
        {
            var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "India Standard Time"
            );

            // Ignore seconds
            var dateHoursMinsOnly = new DateTime(
                istNow.Year,
                istNow.Month,
                istNow.Day,
                istNow.Hour,
                istNow.Minute,
                0
            );

            using var scope = _scopeFactory.CreateScope();

            var reminderRepo = scope.ServiceProvider.GetRequiredService<IRfqReminderSet>();
            var subRepo = scope.ServiceProvider.GetRequiredService<ISubcontractors>();
            var emailRepo = scope.ServiceProvider.GetRequiredService<IEmail>();

            var reminders = await reminderRepo.GetPendingReminders(dateHoursMinsOnly);

            _logger.LogInformation(
                "Reminder triggered at {Time}. Found {Count} reminders.",
                istNow.ToString("HH:mm"),
                reminders.Count()
            );

            foreach (var r in reminders)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    var reminderSet = r.ReminderSet;
                    if (reminderSet == null)
                        continue;

                    var sub = await subRepo.GetSubcontractorById(reminderSet.SubcontractorID);
                    if (sub == null)
                        continue;

                    await emailRepo.SendReminderEmailAsync(
                        reminderSet.SubcontractorID,
                        sub.EmailID,
                        sub.Name,
                        reminderSet.RfqID,
                        reminderSet.ReminderEmailBody
                    );

                    // ✅ Mark THIS schedule as sent
                    await reminderRepo.MarkReminderSent(
                        r.RfqReminderSetScheduleID,
                        istNow
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed sending reminder. ScheduleID={ScheduleId}",
                        r.RfqReminderSetScheduleID
                    );
                }
            }
        }

    }
}
