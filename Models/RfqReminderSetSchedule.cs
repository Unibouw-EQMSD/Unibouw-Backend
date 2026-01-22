namespace UnibouwAPI.Models
{
    public class RfqReminderSetSchedule
    {
        public Guid RfqReminderSetScheduleID { get; set; }

        public Guid RfqReminderSetID { get; set; }

        public DateTime ReminderDateTime { get; set; }

        public DateTime? SentAt { get; set; }
    }
}
