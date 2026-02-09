
namespace UnibouwAPI.Models
{
    public class RfqGlobalReminder
    {
        public Guid RfqGlobalReminderID { get; set; }

        public string ReminderSequence { get; set; }

        public string ReminderTime { get; set; }

        public string? ReminderEmailBody { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsEnable { get; set; }
    }
}
