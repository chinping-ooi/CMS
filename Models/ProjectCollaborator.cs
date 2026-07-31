using CMS.Enum;

namespace CMS.Models
{
    public class ProjectCollaborator
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; }
        public Guid UserId { get; set; }
        public User User { get; set; }
        public string Role { get; set; }
        public Status Status { get; set; } = Status.Active;
        public DateTime JoinedAt { get; set; }
    }
}
