using System.ComponentModel.DataAnnotations;

namespace VMeetTool.Models
{
    public class LoginRequestModel
    {
        [Required(ErrorMessage = "Identifier (username or email) is required.")]
        public string identifier { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        public string password { get; set; }
    }

    public class RefreshTokenRequestModel
    {
        [Required(ErrorMessage = "refreshToken is required.")]
        public string refreshToken { get; set; }
    }

    public class VerifyUserRequestModel
    {
        [Required(ErrorMessage = "Identifier is required.")]
        public string identifier { get; set; }
    }

    public class ResetPasswordByIdRequestModel
    {
        [Required(ErrorMessage = "userId is required.")]
        public string userId { get; set; }

        [Required(ErrorMessage = "newPassword is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string newPassword { get; set; }
    }

    public class ResetPasswordByTokenRequestModel
    {
        [Required(ErrorMessage = "token is required.")]
        public string token { get; set; }

        [Required(ErrorMessage = "newPassword is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string newPassword { get; set; }
    }
}