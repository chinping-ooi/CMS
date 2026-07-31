using System.ComponentModel.DataAnnotations;
using CMS.Enum;

namespace CMS.Models;

public class TaskChecklistItem
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public TaskItem? Task { get; set; }
    public string Label { get; set; }
    public bool IsCompleted { get; set; }
    public Status Status { get; set; } = Status.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
