using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class CreateSnapshotRequestModel
    {
        [Required(ErrorMessage = "room_code is required.")]
        [StringLength(32, MinimumLength = 1)]
        public string room_code { get; set; }

        [Required(ErrorMessage = "captured_by is required.")]
        [StringLength(100, MinimumLength = 1)]
        public string captured_by { get; set; }

        [Required(ErrorMessage = "file_path is required.")]
        [StringLength(500, MinimumLength = 1)]
        public string file_path { get; set; }

        // Optional JSON metadata string
        public string metadata { get; set; }

        // Optional category ID
        public int? category_id { get; set; }
    }
}
