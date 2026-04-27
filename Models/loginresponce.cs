using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class LoginRequestModel
    {
        [Required(ErrorMessage = "Username is required.")]
        public string user_name { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        public string password { get; set; }
    }
}