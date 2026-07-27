using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class Project
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ICollection<ProjectColumn> Columns { get; set; } = new List<ProjectColumn>();
        public ICollection<ProjectTag> Tags { get; set; } = new List<ProjectTag>();
        public ICollection<ProjectCollaborator> Collaborators { get; set; } = new List<ProjectCollaborator>();
        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
