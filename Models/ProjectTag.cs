using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class ProjectTag
    {
        public Guid Id { get; set; }


        public Guid ProjectId { get; set; }

        public Project? Project { get; set; }


        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;


        public string? Color { get; set; }


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


        public ICollection<TaskItemTag> TaskTags { get; set; }
            = new List<TaskItemTag>();
    }
}
