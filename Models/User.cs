using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public required string FullName { get; set; }
        public required string Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        public required DateTime CreatedAt { get; set; }
        public ICollection<ProjectCollaborator> Projects { get; set; }
            = new List<ProjectCollaborator>();
    }
}
