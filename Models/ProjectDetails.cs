namespace UnibouwAPI.Models
{
    public class ProjectDetails
    {
        public Guid ProjectID { get; set; }
        public string ProjectName { get; set; } = "";
        public string Number { get; set; } = "";
        public string Company { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime CompletionDate { get; set; }
        public string? SharepointURL { get; set; }
    }
}
