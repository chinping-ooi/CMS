namespace CMS.Models
{
    public class TaskItemTag
    {
        public Guid TaskId { get; set; }

        public TaskItem Task { get; set; } = null!;


        public Guid TagId { get; set; }

        public ProjectTag Tag { get; set; } = null!;
    }
}
