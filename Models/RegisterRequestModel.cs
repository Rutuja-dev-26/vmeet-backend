using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class RegisterRequestModel
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(255)]
        public string user_fullname { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        [StringLength(255)]
        public string user_mail_id { get; set; }

        [Required(ErrorMessage = "Contact details is required.")]
        [StringLength(10, MinimumLength = 10, ErrorMessage = "Contact number must be exactly 10 digits.")]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Contact number must be exactly 10 numeric digits.")]
        public string contact_details { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string password { get; set; }
    }
}