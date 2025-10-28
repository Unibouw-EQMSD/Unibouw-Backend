﻿namespace UnibouwAPI.Models
{
    public class RfqSubcontractorMapping
    {
        public Guid RfqID { get; set; }
        public Guid SubcontractorID { get; set; }

        // Navigation
        public Rfq? Rfq { get; set; }
        public Subcontractor? Subcontractor { get; set; }
    }
}
