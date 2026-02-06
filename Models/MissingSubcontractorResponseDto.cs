namespace UnibouwAPI.Models
{
    public class MissingSubcontractorResponseDto
    {
        public List<Subcontractor> MissingSubcontractorsDetails { get; set; }
        public List<Rfq> RfqDetails { get; set; }
        public List<RfqSubcontractorResponse> QuoteDetails { get; set; }
    }
}
