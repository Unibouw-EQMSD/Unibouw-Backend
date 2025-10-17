using System.ComponentModel.DataAnnotations.Schema;

namespace UnibouwAPI.Models.DWH
{
    public class WorkItemDatawareHouse
    {
        public int ID { get; set; }
        public string Type { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        [Column("WorkItemParent_ID")]
        public int? WorkItemParent_ID { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
