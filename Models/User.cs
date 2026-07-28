using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        public DateTime CreatedAt { get; set; }
        public ICollection<ProjectCollaborator> Projects { get; set; } = new List<ProjectCollaborator>();
    }
}
