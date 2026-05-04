namespace VMeetTool.Models
{
    public class ApiResponseModel
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public ErrorDetail error { get; set; }

        public class ErrorDetail
        {
            public string code { get; set; }
            public string message { get; set; }
        }

        public static ApiResponseModel Success(string message, object data = null)
        {
            return new ApiResponseModel { success = true, message = message, data = data };
        }

        public static ApiResponseModel Failure(string message, string code = "ERROR")
        {
            return new ApiResponseModel
            {
                success = false,
                message = message,
                error = new ErrorDetail { code = code, message = message }
            };
        }
    }

    public class AuthResponseModel
    {
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public string accessTokenExpiresAt { get; set; }
        public UserResponseModel user { get; set; }
    }

    public class UserResponseModel
    {
        public string userId { get; set; }
        public string username { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
        public string fullName { get; set; }
        public bool isVerified { get; set; }
        public string createdAt { get; set; }
    }

    public class VerifyUserResponse
    {
        public string userId { get; set; }
        public string fullName { get; set; }
        public string maskedEmail { get; set; }
        public string maskedPhone { get; set; }
    }
}
