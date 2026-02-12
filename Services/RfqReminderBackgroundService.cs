using Dapper;
using Microsoft.Data.SqlClient;
using System;
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
            var timeZoneId = OperatingSystem.IsWindows()
                ? "W. Europe Standard Time"
                : "Europe/Amsterdam";

            var amsNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, timeZoneId);

            var dateHoursMinsOnly = new DateTime(
                amsNow.Year,
                amsNow.Month,
                amsNow.Day,
                amsNow.Hour,
                amsNow.Minute,
                0
            );

            using var scope = _scopeFactory.CreateScope();
            var reminderRepo = scope.ServiceProvider.GetRequiredService<IRfqReminder>();
            var subRepo = scope.ServiceProvider.GetRequiredService<ISubcontractors>();
            var emailRepo = scope.ServiceProvider.GetRequiredService<IEmail>();

            var reminders = await reminderRepo.GetPendingReminders(dateHoursMinsOnly);

            _logger.LogInformation(
                "Reminder triggered at {Time}. Found {Count} reminders.",
                amsNow.ToString("HH:mm"),
                reminders.Count()
            );

            foreach (var r in reminders)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                // ✅ Stop immediately if admin disabled reminders mid-run
                if (!await IsReminderEnabled(stoppingToken))
                {
                    _logger.LogInformation("Reminders disabled mid-run. Stopping reminder processing.");
                    break;
                }

                try
                {
                    var reminderSet = r.Reminder;
                    if (reminderSet == null)
                        continue;

                    var hasQuote = await reminderRepo.HasUploadedQuoteAsync(reminderSet.RfqID, reminderSet.SubcontractorID);
                    if (hasQuote)
                    {
                        await reminderRepo.MarkAllSchedulesSentForSubAtTime(
                            reminderSet.SubcontractorID,
                            r.ReminderDateTime,
                            amsNow
                        );
                        continue;
                    }

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
                    await reminderRepo.MarkAllSchedulesSentForSubAtTime(
                        reminderSet.SubcontractorID,
                        r.ReminderDateTime,
                        amsNow
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
                const string sql = @"SELECT TOP 1 IsEnable FROM dbo.RfqGlobalReminder;";
                using var conn = new SqlConnection(_connectionString);
                var chk = await conn.ExecuteScalarAsync<bool>(sql);
                return chk; // ✅ FIX
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
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
