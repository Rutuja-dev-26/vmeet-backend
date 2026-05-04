using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class StartRecordingRequestModel
    {
        [Required(ErrorMessage = "session_id is required.")]
        [StringLength(100, MinimumLength = 1)]
        public string session_id { get; set; }

        [Required(ErrorMessage = "room_code is required.")]
        [StringLength(32, MinimumLength = 1)]
        public string room_code { get; set; }

        // Defaults to 1 if not supplied
        public int client_id { get; set; } = 1;
    }
}
