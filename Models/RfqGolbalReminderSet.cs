using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

namespace UnibouwAPI.Models
{
    public class RfqGolbalReminderSet
    {
        public Guid ID { get; set; }

        public string ReminderSequence { get; set; }

        public string ReminderTime { get; set; }

        public string? ReminderEmailBody { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public bool IsEnable { get; set; }
    }
}
