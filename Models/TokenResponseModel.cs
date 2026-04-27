namespace VMeetTool.Models
{
    public class TokenResponseModel
    {
        public string token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }  
        public int user_id { get; set; }
        public string user_name { get; set; }
        public string user_fullname { get; set; }
        public string user_mail_id { get; set; }
    }
}