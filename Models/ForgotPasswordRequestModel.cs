using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class ForgotPasswordRequestModel
    {
        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string user_mail_id { get; set; }
    }
}
