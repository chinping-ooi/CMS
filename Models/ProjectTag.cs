using System.ComponentModel.DataAnnotations;
using CMS.Enum;

namespace CMS.Models
{
    public class ProjectTag
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }
        public string Name { get; set; }
        public string? Color { get; set; }
        public Status Status { get; set; } = Status.Active;
        public DateTime CreatedAt { get; set; }
        public ICollection<TaskItemTag> TaskTags { get; set; } = new List<TaskItemTag>();
    }
}
