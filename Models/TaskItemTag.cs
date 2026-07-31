using CMS.Enum;

namespace CMS.Models
{
    public class TaskItemTag
    {
        public Guid TaskId { get; set; }
        public TaskItem Task { get; set; }
        public Guid TagId { get; set; }
        public ProjectTag Tag { get; set; }
        public Status Status { get; set; } = Status.Active;
    }
}
