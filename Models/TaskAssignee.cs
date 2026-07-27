namespace CMS.Models;

public class TaskAssignee
{
    public Guid TaskId { get; set; }
    public TaskItem? Task { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime AssignedAt { get; set; }
}
