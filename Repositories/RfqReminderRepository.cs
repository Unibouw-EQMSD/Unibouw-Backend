using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Controllers;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqReminderRepository : IRfqReminder
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ILogger<RfqReminderController> _logger;

        public RfqReminderRepository(IConfiguration configuration, ILogger<RfqReminderController> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<RfqReminder>> GetAllRfqReminder()
        {
            var query = @"SELECT * FROM RfqReminder";

            return await _connection.QueryAsync<RfqReminder>(query);
         }

        public async Task CreateOrUpdateRfqReminder(RfqReminder model, List<DateTime> reminderDateTimes, string updatedBy)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var tx = connection.BeginTransaction();

            try
            {
                // 1️⃣ Check if Reminder exists
                var reminderId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    @"SELECT RfqReminderID
              FROM dbo.RfqReminder
              WHERE RfqID = @RfqID
                AND SubcontractorID = @SubcontractorID",
                    model,
                    tx
                );

                // 2️⃣ Insert or Update RfqReminder
                if (reminderId == null)
                {
                    reminderId = Guid.NewGuid();

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.RfqReminder
                  (
                      RfqReminderID,
                      RfqID,
                      SubcontractorID,
                      DueDate,
                      ReminderEmailBody,
                      UpdatedBy,
                      UpdatedAt
                  )
                  VALUES
                  (
                      @RfqReminderID,
                      @RfqID,
                      @SubcontractorID,
                      @DueDate,
                      @ReminderEmailBody,
                      @UpdatedBy,
                      SYSDATETIME()
                  )",
                        new
                        {
                            RfqReminderID = reminderId,
                            model.RfqID,
                            model.SubcontractorID,
                            model.DueDate,
                            model.ReminderEmailBody,
                            UpdatedBy = updatedBy
                        },
                        tx
                    );
                }
                else
                {
                    await connection.ExecuteAsync(
                        @"UPDATE dbo.RfqReminder
                  SET
                      DueDate = @DueDate,
                      ReminderEmailBody = @ReminderEmailBody,
                      UpdatedBy = @UpdatedBy,
                      UpdatedAt = SYSDATETIME()
                  WHERE RfqReminderID = @RfqReminderID",
                        new
                        {
                            RfqReminderID = reminderId,
                            model.DueDate,
                            model.ReminderEmailBody,
                            UpdatedBy = updatedBy
                        },
                        tx
                    );
                }

                // 3️⃣ Insert or Update schedules (avoid duplicates)
                foreach (var reminderDateTime in reminderDateTimes)
                {
                    await connection.ExecuteAsync(
                        @"
                        IF EXISTS
                        (
                            SELECT 1
                            FROM dbo.RfqReminderSchedule
                            WHERE RfqReminderID = @RfqReminderID
                              AND ReminderDateTime = @ReminderDateTime
                        )
                        BEGIN
                            -- Update existing reminder (reset sent status)
                            UPDATE dbo.RfqReminderSchedule
                            SET SentAt = NULL
                            WHERE RfqReminderID = @RfqReminderID
                              AND ReminderDateTime = @ReminderDateTime
                        END
                        ELSE
                        BEGIN
                            -- Insert new reminder
                            INSERT INTO dbo.RfqReminderSchedule
                            (
                                RfqReminderScheduleID,
                                RfqReminderID,
                                ReminderDateTime,
                                SentAt
                            )
                            VALUES
                            (
                                NEWID(),
                                @RfqReminderID,
                                @ReminderDateTime,
                                NULL
                            )
                        END",
                        new
                        {
                            RfqReminderID = reminderId,
                            ReminderDateTime = reminderDateTime
                        },
                        tx
                    );
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        //--------------------- Auto trigger Reminder ------------------------------------
        public async Task<IEnumerable<RfqReminderSchedule>> GetPendingReminders(DateTime currentDateTime)
        {
            try
            {
                const string sql = @"
SELECT
    rs.RfqReminderScheduleID,
    rs.RfqReminderID,
    rs.ReminderDateTime,
    rs.SentAt,
    r.RfqReminderID,
    r.RfqID,
    r.SubcontractorID,
    r.DueDate,
    r.ReminderEmailBody,
    r.UpdatedBy,
    r.UpdatedAt
FROM dbo.RfqReminderSchedule rs
INNER JOIN dbo.RfqReminder r
    ON rs.RfqReminderID = r.RfqReminderID
WHERE rs.ReminderDateTime = @CurrentDateTime
  AND rs.SentAt IS NULL
  AND EXISTS (SELECT 1 FROM dbo.RfqGlobalReminder WHERE IsEnable = 1);";

                using var conn = new SqlConnection(_connectionString);

                var result = await conn.QueryAsync<
                    RfqReminderSchedule,
                    RfqReminder,
                    RfqReminderSchedule>(
                    sql,
                    (schedule, reminder) =>
                    {
                        schedule.Reminder = reminder;
                        return schedule;
                    },
                    new { CurrentDateTime = currentDateTime },
                    splitOn: "RfqReminderID"
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching pending reminders. CurrentDateTime: {CurrentDateTime}",
                    currentDateTime);
                throw;
            }
        }


        public async Task MarkReminderSent(Guid reminderId, DateTime sentDateTime)
        {
            const string sql = @"
                UPDATE dbo.RfqReminderSchedule
                SET SentAt = @sentDateTime
                WHERE RfqReminderScheduleID = @reminderId
                ";

            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new
            {
                reminderId,
                sentDateTime
            });
        }


    }
}
