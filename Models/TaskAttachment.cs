using System.ComponentModel.DataAnnotations;
using CMS.Enum;

namespace CMS.Models
{
    public class TaskAttachment
    {
        public Guid Id { get; set; }
        public Guid TaskId { get; set; }
        public TaskItem Task { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string? FileType { get; set; }
        public long FileSize { get; set; }
        public Status Status { get; set; } = Status.Active;
        public DateTime UploadedAt { get; set; }
    }
}
