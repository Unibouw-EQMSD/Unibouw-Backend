using UnibouwAPI.Models;

namespace UnibouwAPI.Repositories.Interfaces
{
    public interface IRfqReminderSet
    {
        Task<IEnumerable<RfqReminderSet>> GetAllRfqReminderSet();
        Task CreateOrUpdateRfqReminderSet(RfqReminderSet model, List<DateTime> reminderDateTimes, string updatedBy);

        //--------------------- Auto trigger Reminder ------------------------------------
        Task<IEnumerable<RfqReminderSetSchedule>> GetPendingReminders(DateTime currentDateTime);
        Task MarkReminderSent(Guid reminderSetId, DateTime sentDateTime);
    }
}
