using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class ResetPasswordRequestModel
    {
        [Required(ErrorMessage = "Reset token is required.")]
        public string token { get; set; }

        [Required(ErrorMessage = "New password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string new_password { get; set; }

        [Required(ErrorMessage = "Confirm password is required.")]
        [Compare("new_password", ErrorMessage = "Passwords do not match.")]
        public string confirm_password { get; set; }
    }
}
