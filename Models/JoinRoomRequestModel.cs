using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class JoinRoomRequestModel
    {
        [Required(ErrorMessage = "user_id is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "user_id must be greater than 0.")]
        public int? user_id { get; set; }

        [Required(ErrorMessage = "participant_name is required.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "participant_name must be between 2 and 200 characters.")]
        public string participant_name { get; set; }

        [Required(ErrorMessage = "room_code is required.")]
        [StringLength(50, ErrorMessage = "room_code cannot exceed 50 characters.")]
        public string room_code { get; set; }
    }
}