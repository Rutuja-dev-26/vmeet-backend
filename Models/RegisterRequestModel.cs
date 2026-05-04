using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class RegisterRequestModel
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(255)]
        public string fullName { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        [StringLength(255)]
        public string email { get; set; }

        [Required(ErrorMessage = "Mobile number is required.")]
        [StringLength(20)]
        public string phone { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string password { get; set; }
    }
}
