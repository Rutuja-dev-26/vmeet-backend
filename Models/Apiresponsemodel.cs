namespace VMeetTool.Models
{
    public class ApiResponseModel
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }

        public static ApiResponseModel Success(string message, object data = null)
        {
            return new ApiResponseModel { success = true, message = message, data = data };
        }

        public static ApiResponseModel Failure(string message)
        {
            return new ApiResponseModel { success = false, message = message, data = null };
        }
    }
}