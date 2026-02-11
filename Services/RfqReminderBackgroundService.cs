using Dapper;
using Microsoft.Data.SqlClient;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace UnibouwAPI.Services
{
    public class RfqReminderBackgroundService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ILogger<RfqReminderBackgroundService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        DateTime amsterdamNow = DateTimeConvert.ToAmsterdamTime(DateTime.UtcNow);

        public RfqReminderBackgroundService(IConfiguration configuration, IServiceScopeFactory scopeFactory, ILogger<RfqReminderBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if reminder scheduler is enabled
                    if (await IsReminderEnabled(stoppingToken))
                    {
                        await ProcessReminders(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Expected during shutdown — do nothing
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reminder scheduler failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }


        private async Task ProcessReminders(CancellationToken stoppingToken)
        {
            /* var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow,
                "Indian Standard Time"
            );
            */

            /* var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                 amsterdamNow,
                 "Europe Standard Time"
             );*/


            var timeZoneId = OperatingSystem.IsWindows()
                ? "India Standard Time"
                : "Asia/Kolkata";

            var istNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, timeZoneId);

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

            var reminderRepo = scope.ServiceProvider.GetRequiredService<IRfqReminder>();
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
                    var reminderSet = r.Reminder;
                    if (reminderSet == null)
                        continue;

                    var sub = await subRepo.GetSubcontractorById(reminderSet.SubcontractorID);
                    if (sub == null)
                        continue;

                    await emailRepo.SendReminderEmailAsync(
                        reminderSet.SubcontractorID,
                        sub.Email,
                        sub.Name,
                        reminderSet.RfqID,
                        reminderSet.ReminderEmailBody
                    );

                    // ✅ Mark THIS schedule as sent
                    await reminderRepo.MarkReminderSent(
                        r.RfqReminderScheduleID,
                        istNow
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed sending reminder. ScheduleID={ScheduleId}",
                        r.RfqReminderScheduleID
                    );
                }
            }
        }

        private async Task<bool> IsReminderEnabled(CancellationToken stoppingToken)
        {
            try
            {
                const string sql = @"
                SELECT TOP 1 IsEnable
                FROM dbo.RfqGlobalReminder";

                using var conn = new SqlConnection(_connectionString);
                var chk = await conn.ExecuteScalarAsync<bool>(sql);
                return false;
                /*return await _connectionString.QueryAsync<bool>(sql);*/
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check reminder enable status from RfqGlobalReminder table.");
                return false;
            }
        }



    }
}
