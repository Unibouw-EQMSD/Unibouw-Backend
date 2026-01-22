namespace UnibouwAPI.Models
{
    public class RfqReminderSet
    {
        public Guid RfqReminderSetID { get; set; }
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }

        public DateTime? DueDate { get; set; }

        public string? ReminderEmailBody { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Incoming from frontend only
        public string[]? ReminderDates { get; set; }
        public string? ReminderTime { get; set; }
    }
}
