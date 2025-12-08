namespace UnibouwAPI.Models
{
    public class ReminderRequest
    {
        public Guid SubcontractorId { get; set; }
        public Guid RfqID { get; set; }
        public string EmailBody { get; set; }   
    }
}
