using System;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using VMeetTool.Helpers;
using VMeetTool.Models;

namespace VMeetTool.Controllers
{
    [RoutePrefix("api")]
    public class RegisterApiController : ApiController
    {
        // ✅ REGISTER API
        [HttpPost]
        [Route("register")]
        public IHttpActionResult Register([FromBody] RegisterRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                byte[] passwordHash = HashPassword(model.password);

                var parameters = new[]
                {
                    new SqlParameter("@user_fullname",   SqlDbType.VarChar,   255) { Value = model.user_fullname.Trim() },
                    new SqlParameter("@user_mail_id",    SqlDbType.VarChar,   255) { Value = model.user_mail_id.Trim().ToLower() },
                    new SqlParameter("@contact_details", SqlDbType.VarChar,    20) { Value = model.contact_details.Trim() },
                    new SqlParameter("@password",        SqlDbType.VarBinary,  -1) { Value = passwordHash }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_register_user", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Registration failed: no response from database."));

                DataRow row = result.Rows[0];
                string status = row["status"]?.ToString();
                string spMessage = row["message"]?.ToString();

              
                if (status == "email_exists" || status == "phone_exists")
                    return Content(System.Net.HttpStatusCode.Conflict,
                                   ApiResponseModel.Failure(spMessage));

                if (status != "success")
                    return InternalServerError(new Exception(spMessage ?? "Registration failed."));

                int newUserId = Convert.ToInt32(row["user_id"]);
                string userName = model.user_mail_id.Trim().ToLower().Split('@')[0];

                string token = JwtHelper.GenerateToken(
                    newUserId,
                    userName,
                    model.user_mail_id.Trim().ToLower(),
                    model.user_fullname.Trim()
                );

                var tokenResponse = new TokenResponseModel
                {
                    token = token,
                    token_type = "Bearer",
                    expires_in = JwtHelper.ExpirySeconds,
                    user_id = newUserId,
                    user_fullname = model.user_fullname.Trim(),
                    user_mail_id = model.user_mail_id.Trim().ToLower()
                };

                return Ok(ApiResponseModel.Success(spMessage, tokenResponse));
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        // ✅ LOGIN API (NOW INSIDE CLASS)
        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequestModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.user_name) || string.IsNullOrEmpty(model.password))
                return BadRequest("Username and Password required");

            try
            {
                byte[] passwordHash = HashPassword(model.password);

                var parameters = new[]
                {
                    new SqlParameter("@user_name", SqlDbType.VarChar, 100) { Value = model.user_name },
                    new SqlParameter("@password", SqlDbType.VarBinary, -1) { Value = passwordHash }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("sp_login_user", parameters);

                if (result != null && result.Rows.Count > 0)
                {
                    var row = result.Rows[0];

                    string status = row["status"]?.ToString();
                    string message = row["message"]?.ToString();

                    if (status == "success")
                    {
                        int userId = Convert.ToInt32(row["user_id"]);
                        string fullName = row["user_fullname"]?.ToString() ?? "";
                        string email = row["user_mail_id"]?.ToString() ?? "";
                        string token = JwtHelper.GenerateToken(userId, model.user_name, email, fullName);

                        var tokenResponse = new TokenResponseModel
                        {
                            token = token,
                            token_type = "Bearer",
                            expires_in = JwtHelper.ExpirySeconds,
                            user_id = userId,
                            user_name = model.user_name,
                            user_fullname = fullName,
                            user_mail_id = email
                        };

                        return Ok(ApiResponseModel.Success("Login Successful", tokenResponse));
                    }
                    else
                    {
                        // ✅ Return the actual SP error message (e.g. "Invalid username or password.")
                        return Content(System.Net.HttpStatusCode.Unauthorized,
                                       ApiResponseModel.Failure(message));
                    }
                }

                return Unauthorized();

      
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        // ✅ COMMON HASH METHOD
        private static byte[] HashPassword(string plainTextPassword)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(plainTextPassword));
            }
        }
    }
}