using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class LeaveSessionRequestModel
    {
        [Required(ErrorMessage = "room_code is required.")]
        [StringLength(32, MinimumLength = 1)]
        public string room_code { get; set; }

        [Required(ErrorMessage = "user_id is required.")]
        [StringLength(100, MinimumLength = 1)]
        public string user_id { get; set; }
    }
}
