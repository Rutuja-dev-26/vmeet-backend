using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class JoinMeetingRequestModel
    {
        [Required(ErrorMessage = "user_id is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "user_id must be greater than 0.")]
        public int? user_id { get; set; }

        [Required(ErrorMessage = "meeting_code is required.")]
        [StringLength(20, ErrorMessage = "meeting_code cannot exceed 20 characters.")]
        [RegularExpression(@"^VMT-[A-Z0-9]{8}$", ErrorMessage = "Invalid meeting_code format. Expected format: VMT-XXXXXXXX")]
        public string meeting_code { get; set; }
    }
}