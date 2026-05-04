using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class FinalizeRecordingRequestModel
    {
        [Required(ErrorMessage = "session_id is required.")]
        [StringLength(100, MinimumLength = 1)]
        public string session_id { get; set; }

        [Required(ErrorMessage = "file_path is required.")]
        [StringLength(500, MinimumLength = 1)]
        public string file_path { get; set; }

        [Range(0, int.MaxValue)]
        public int chunk_count { get; set; }

        [Range(0, int.MaxValue)]
        public int duration_seconds { get; set; }
    }
}
