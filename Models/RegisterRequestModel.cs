using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class RegisterRequestModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(100)]
        public string user_name { get; set; }

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(255)]
        public string user_fullname { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        [StringLength(255)]
        public string user_mail_id { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string password { get; set; }
    }
}