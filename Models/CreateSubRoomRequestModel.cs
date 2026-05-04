using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class CreateSubRoomRequestModel
    {
        [Required(ErrorMessage = "room_id is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "room_id must be a positive integer.")]
        public int room_id { get; set; }

        [Required(ErrorMessage = "user_id is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "user_id must be a positive integer.")]
        public int user_id { get; set; }

        [Required(ErrorMessage = "sub_name is required.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "sub_name must be between 1 and 200 characters.")]
        public string sub_name { get; set; }
    }
}
