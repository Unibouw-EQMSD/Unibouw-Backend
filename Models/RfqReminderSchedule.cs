namespace UnibouwAPI.Models
{
    public class RfqReminderSchedule
    {
        public Guid RfqReminderScheduleID { get; set; }

        public Guid RfqReminderID { get; set; }

        public DateTime ReminderDateTime { get; set; }

        public DateTime? SentAt { get; set; }

        // Navigation (not a table column)
        public RfqReminder Reminder { get; set; }
    }
}
