using System.ComponentModel.DataAnnotations;
using CMS.Enum;

namespace CMS.Models
{
    public class ProjectColumn
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }
        public string Name { get; set; }
        public int Position { get; set; }
        public int Status { get; set; } = 1;
        public DateTime CreatedAt { get; set; }
        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
