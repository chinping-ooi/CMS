namespace CMS.Models
{
    public class ProjectCollaborator
    {
        public Guid ProjectId { get; set; }

        public Project Project { get; set; } = null!;


        public Guid UserId { get; set; }

        public User User { get; set; } = null!;


        public string Role { get; set; } = "member";


        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
