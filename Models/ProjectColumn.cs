using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class ProjectColumn
    {
        public Guid Id { get; set; }


        public Guid ProjectId { get; set; }

        public Project? Project { get; set; }


        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;


        public int Position { get; set; }


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


        public ICollection<TaskItem> Tasks { get; set; }
            = new List<TaskItem>();
    }
}
