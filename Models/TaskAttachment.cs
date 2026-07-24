using System.ComponentModel.DataAnnotations;

namespace CMS.Models
{
    public class TaskAttachment
    {
        public Guid Id { get; set; }
        public Guid TaskId { get; set; }
        public required TaskItem Task { get; set; }
        public required string FileName { get; set; }
        public required string FilePath { get; set; }
        public string? FileType { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}
