using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class TaskItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public Guid ProjectId { get; set; }
        public Project Project { get; set; }
        public Guid ColumnId { get; set; }
        public ProjectColumn Column { get; set; }
        public string? BoardType { get; set; }
        public Guid? AssignedUserId { get; set; }
        public User? AssignedUser { get; set; }
        public ICollection<TaskAssignee> Assignees { get; set; } = new List<TaskAssignee>();
        public string Priority { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Category { get; set; }
        public ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
        public ICollection<TaskChecklistItem> Checklist { get; set; } = new List<TaskChecklistItem>();
        public ICollection<TaskItemTag> Tags { get; set; } = new List<TaskItemTag>();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
