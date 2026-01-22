using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using UnibouwAPI.Helpers;
using UnibouwAPI.Models;
using UnibouwAPI.Repositories.Interfaces;

namespace UnibouwAPI.Repositories
{
    public class RfqReminderSetRepository : IRfqReminderSet
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
     
        public RfqReminderSetRepository(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("UnibouwDbConnection");
            if (string.IsNullOrEmpty(_connectionString))
                throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        private IDbConnection _connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<RfqReminderSet>> GetAllRfqReminderSet()
        {
            var query = @"SELECT * FROM RfqReminderSet";

            return await _connection.QueryAsync<RfqReminderSet>(query);
         }

        public async Task CreateOrUpdateRfqReminderSet(RfqReminderSet model, List<DateTime> reminderDateTimes, string updatedBy)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var tx = connection.BeginTransaction();

            try
            {
                // 1️⃣ Check if ReminderSet exists
                var reminderSetId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    @"SELECT RfqReminderSetID
              FROM dbo.RfqReminderSet
              WHERE RfqID = @RfqID
                AND SubcontractorID = @SubcontractorID",
                    model,
                    tx
                );

                // 2️⃣ Insert or Update RfqReminderSet
                if (reminderSetId == null)
                {
                    reminderSetId = Guid.NewGuid();

                    await connection.ExecuteAsync(
                        @"INSERT INTO dbo.RfqReminderSet
                  (
                      RfqReminderSetID,
                      RfqID,
                      SubcontractorID,
                      DueDate,
                      ReminderEmailBody,
                      UpdatedBy,
                      UpdatedAt
                  )
                  VALUES
                  (
                      @RfqReminderSetID,
                      @RfqID,
                      @SubcontractorID,
                      @DueDate,
                      @ReminderEmailBody,
                      @UpdatedBy,
                      SYSDATETIME()
                  )",
                        new
                        {
                            RfqReminderSetID = reminderSetId,
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
                        @"UPDATE dbo.RfqReminderSet
                  SET
                      DueDate = @DueDate,
                      ReminderEmailBody = @ReminderEmailBody,
                      UpdatedBy = @UpdatedBy,
                      UpdatedAt = SYSDATETIME()
                  WHERE RfqReminderSetID = @RfqReminderSetID",
                        new
                        {
                            RfqReminderSetID = reminderSetId,
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
                            FROM dbo.RfqReminderSetSchedule
                            WHERE RfqReminderSetID = @RfqReminderSetID
                              AND ReminderDateTime = @ReminderDateTime
                        )
                        BEGIN
                            -- Update existing reminder (reset sent status)
                            UPDATE dbo.RfqReminderSetSchedule
                            SET SentAt = NULL
                            WHERE RfqReminderSetID = @RfqReminderSetID
                              AND ReminderDateTime = @ReminderDateTime
                        END
                        ELSE
                        BEGIN
                            -- Insert new reminder
                            INSERT INTO dbo.RfqReminderSetSchedule
                            (
                                RfqReminderSetScheduleID,
                                RfqReminderSetID,
                                ReminderDateTime,
                                SentAt
                            )
                            VALUES
                            (
                                NEWID(),
                                @RfqReminderSetID,
                                @ReminderDateTime,
                                NULL
                            )
                        END",
                        new
                        {
                            RfqReminderSetID = reminderSetId,
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

    }
}
