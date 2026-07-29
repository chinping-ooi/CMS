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
        public int Status { get; set; } = 1;
        public DateTime UploadedAt { get; set; }
    }
}
