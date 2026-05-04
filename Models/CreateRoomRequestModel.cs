using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class CreateRoomRequestModel
    {
        [Required(ErrorMessage = "user_id is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "user_id must be a positive integer.")]
        public int user_id { get; set; }

        [Required(ErrorMessage = "room_name is required.")]
        [StringLength(200, MinimumLength = 3, ErrorMessage = "room_name must be between 3 and 200 characters.")]
        public string room_name { get; set; }
    }
}
