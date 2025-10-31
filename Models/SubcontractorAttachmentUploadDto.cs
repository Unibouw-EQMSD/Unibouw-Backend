using System.ComponentModel.DataAnnotations;

namespace UnibouwAPI.Models
{
    public class SubcontractorAttachmentUploadDto
    {
        [Required]
        public Guid SubcontractorID { get; set; }

        [Required]
        public IFormFileCollection? Files { get; set; }
    }
}
