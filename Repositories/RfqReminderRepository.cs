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
;WITH Pending AS (
    SELECT
        rs.RfqReminderScheduleID,
        rs.RfqReminderID,
        rs.ReminderDateTime,
        rs.SentAt,
        r.RfqReminderID AS SplitRfqReminderID,
        r.RfqID,
        r.SubcontractorID,
        r.DueDate,
        r.ReminderEmailBody,
        r.UpdatedBy,
        r.UpdatedAt,
        r.ReminderType,
        ROW_NUMBER() OVER (
            PARTITION BY r.SubcontractorID, rs.ReminderDateTime
            ORDER BY r.DueDate ASC, r.UpdatedAt DESC
        ) AS rn
    FROM dbo.RfqReminderSchedule rs
    INNER JOIN dbo.RfqReminder r ON rs.RfqReminderID = r.RfqReminderID
    WHERE rs.ReminderDateTime = @CurrentDateTime
      AND rs.SentAt IS NULL
      AND (
            r.ReminderType = 'custom'
            OR EXISTS (SELECT 1 FROM dbo.RfqGlobalReminder WHERE IsEnable = 1)
          )
)
SELECT
    rs.RfqReminderScheduleID,
    rs.RfqReminderID,
    rs.ReminderDateTime,
    rs.SentAt,
    r.RfqReminderID AS SplitRfqReminderID,
    r.RfqID,
    r.SubcontractorID,
    r.DueDate,
    r.ReminderEmailBody,
    r.UpdatedBy,
    r.UpdatedAt,
    r.ReminderType
FROM dbo.RfqReminderSchedule rs
INNER JOIN dbo.RfqReminder r ON rs.RfqReminderID = r.RfqReminderID
WHERE rs.ReminderDateTime = @CurrentDateTime
  AND rs.SentAt IS NULL
  AND (
        r.ReminderType = 'custom'
        OR EXISTS (SELECT 1 FROM dbo.RfqGlobalReminder WHERE IsEnable = 1)
      );";
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

        public async Task MarkScheduleSentForSubAtTime(Guid rfqId, Guid subcontractorId, DateTime reminderDateTime, DateTime sentAt)
        {
            const string sql = @"
UPDATE rs
SET SentAt = @SentAt
FROM dbo.RfqReminderSchedule rs
INNER JOIN dbo.RfqReminder r
    ON r.RfqReminderID = rs.RfqReminderID
WHERE r.SubcontractorID = @SubcontractorID
  AND r.RfqID = @RfqID
  AND rs.ReminderDateTime = @ReminderDateTime
  AND rs.SentAt IS NULL;";
            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(sql, new
            {
                SubcontractorID = subcontractorId,
                RfqID = rfqId,
                ReminderDateTime = reminderDateTime,
                SentAt = sentAt
            });
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



        public async Task<AutoScheduleGenerateResult> GenerateAutoSchedulesFromGlobalConfigAsync(string updatedBy)
        {
            // 1) Get global config
            const string globalSql = @"
SELECT TOP 1
    ReminderSequence,
    ReminderTime,
    ReminderEmailBody,
    IsEnable
FROM dbo.RfqGlobalReminder;";

            // 2) Eligible RFQ + Subcontractor (Sent RFQ, subcontractor due date exists, not expired, no quote uploaded)
            const string eligibleSql = @"
SELECT DISTINCT
    rsm.RfqID,
    rsm.SubcontractorID,
    rfq.CreatedOn,
    rsm.DueDate AS DueDate
FROM dbo.RfqSubcontractorMapping rsm
INNER JOIN dbo.Rfq rfq
    ON rfq.RfqID = rsm.RfqID
WHERE rfq.Status = 'Sent'
  AND rfq.IsDeleted = 0
  AND rsm.DueDate IS NOT NULL
  AND rsm.DueDate > SYSDATETIME()
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.RfqResponseDocuments d
      WHERE d.RfqID = rsm.RfqID
        AND d.SubcontractorID = rsm.SubcontractorID
        AND d.IsDeleted = 0
  );";

            // 3) Cleanup (remove old pending schedules so only the newly configured times fire)
            const string getReminderIdSql = @"
SELECT RfqReminderID
FROM dbo.RfqReminder
WHERE RfqID = @RfqID AND SubcontractorID = @SubcontractorID;";

            const string deletePendingSchedulesSql = @"
DELETE rs
FROM dbo.RfqReminderSchedule rs
WHERE rs.RfqReminderID = @RfqReminderID
  AND rs.SentAt IS NULL;";

            using var conn = new SqlConnection(_connectionString);

            var config = await conn.QueryFirstOrDefaultAsync<GlobalReminderConfigRow>(globalSql);

            if (config == null || !config.IsEnable)
                return new AutoScheduleGenerateResult { TotalEligible = 0, TotalSchedulesCreatedOrReset = 0 };

            if (string.IsNullOrWhiteSpace(config.ReminderSequence) || string.IsNullOrWhiteSpace(config.ReminderTime))
                return new AutoScheduleGenerateResult { TotalEligible = 0, TotalSchedulesCreatedOrReset = 0 };

            // Parse offsets like "-1,-2,-3"
            var offsets = config.ReminderSequence
                .Split(',')
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();

            if (!offsets.Any())
                return new AutoScheduleGenerateResult { TotalEligible = 0, TotalSchedulesCreatedOrReset = 0 };

            if (!TimeSpan.TryParse(config.ReminderTime, out var reminderTime))
                return new AutoScheduleGenerateResult { TotalEligible = 0, TotalSchedulesCreatedOrReset = 0 };

            var eligible = (await conn.QueryAsync<EligibleReminderRow>(eligibleSql)).ToList();

            var totalSchedules = 0;

            // 4) Generate schedules for each eligible pair
            foreach (var e in eligible)
            {
                var createdDate = e.CreatedOn.Date;
                var dueDate = e.DueDate.Date;

                // due - offsets => reminder dates (must be >= createdDate AND < dueDate)
                var reminderDateTimes = offsets
                    .Select(off => dueDate.AddDays(off))
                    .Where(d => d >= createdDate && d < dueDate)
                    .Select(d => d.Add(reminderTime))
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                if (!reminderDateTimes.Any())
                    continue;

                // ✅ Delete old pending schedules for this RFQ+Sub before inserting new ones
                var existingReminderId = await conn.ExecuteScalarAsync<Guid?>(
                    getReminderIdSql,
                    new { RfqID = e.RfqID, SubcontractorID = e.SubcontractorID }
                );

                if (existingReminderId.HasValue)
                {
                    await conn.ExecuteAsync(
                        deletePendingSchedulesSql,
                        new { RfqReminderID = existingReminderId.Value }
                    );
                }

                var model = new RfqReminder
                {
                    RfqID = e.RfqID,
                    SubcontractorID = e.SubcontractorID,
                    DueDate = e.DueDate,
                    ReminderEmailBody = config.ReminderEmailBody ?? string.Empty,

                    // kept for compatibility with your existing model (even though CreateOrUpdate uses reminderDateTimes)
                    ReminderDates = reminderDateTimes.Select(d => d.ToString("yyyy-MM-dd")).ToArray(),
                    ReminderTime = config.ReminderTime
                };

                // Reuse existing upsert logic (creates/updates RfqReminder and inserts schedules)
                await CreateOrUpdateRfqReminder(model, reminderDateTimes, updatedBy);

                totalSchedules += reminderDateTimes.Count;
            }

            return new AutoScheduleGenerateResult
            {
                TotalEligible = eligible.Count,
                TotalSchedulesCreatedOrReset = totalSchedules
            };
        }

        public async Task<bool> HasUploadedQuoteAsync(Guid rfqId, Guid subcontractorId)
        {
            const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.RfqResponseDocuments
    WHERE RfqID = @RfqID
      AND SubcontractorID = @SubcontractorID
      AND IsDeleted = 0
) THEN 1 ELSE 0 END;";

            using var conn = new SqlConnection(_connectionString);
            return await conn.ExecuteScalarAsync<bool>(sql, new { RfqID = rfqId, SubcontractorID = subcontractorId });
        }
    }
}

    



internal class GlobalReminderConfigRow
{
    public string? ReminderSequence { get; set; }
    public string? ReminderTime { get; set; }
    public string? ReminderEmailBody { get; set; }
    public bool IsEnable { get; set; }
}

internal class EligibleReminderRow
{
    public Guid RfqID { get; set; }
    public Guid SubcontractorID { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime DueDate { get; set; }
}