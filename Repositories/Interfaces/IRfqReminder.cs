using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfqReminder
    {
        Task<IEnumerable<RfqReminder>> GetAllRfqReminder();
        Task CreateOrUpdateRfqReminder(RfqReminder model, List<DateTime> reminderDateTimes, string updatedBy);

        //--------------------- Auto trigger Reminder ------------------------------------
        Task<IEnumerable<RfqReminderSchedule>> GetPendingReminders(DateTime currentDateTime);
        Task MarkReminderSent(Guid reminderId, DateTime sentDateTime);
    }
}
