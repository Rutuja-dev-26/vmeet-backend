using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class JoinSessionRequestModel
    {
        [Required(ErrorMessage = "room_code is required.")]
        [StringLength(32, MinimumLength = 1)]
        public string room_code { get; set; }

        [Required(ErrorMessage = "user_id is required.")]
        [StringLength(100, MinimumLength = 1)]
        public string user_id { get; set; }

        [Required(ErrorMessage = "display_name is required.")]
        [StringLength(150, MinimumLength = 1)]
        public string display_name { get; set; }

        // 'host' or 'guest'
        [Required(ErrorMessage = "role is required.")]
        [RegularExpression("^(host|guest)$", ErrorMessage = "role must be 'host' or 'guest'.")]
        public string role { get; set; }

        // Defaults to 1 if not supplied
        public int client_id { get; set; } = 1;

        public int max_participants { get; set; } = 50;
    }
}
