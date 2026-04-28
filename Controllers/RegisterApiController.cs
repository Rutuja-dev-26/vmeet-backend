using System;
using System.Configuration;
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
                    new SqlParameter("@user_name", SqlDbType.VarChar, 100) { Value = model.user_name.Trim() },
                    new SqlParameter("@user_fullname", SqlDbType.VarChar, 255) { Value = model.user_fullname.Trim() },
                    new SqlParameter("@user_mail_id", SqlDbType.VarChar, 255) { Value = model.user_mail_id.Trim().ToLower() },
                    new SqlParameter("@password", SqlDbType.VarBinary, -1) { Value = passwordHash }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("sp_register_user", parameters);

                if (result != null && result.Rows.Count > 0)
                {
                    DataRow row = result.Rows[0];
                    string spMessage = row["message"]?.ToString();
                    int newUserId = Convert.ToInt32(row["user_id"]);

                    return Ok(ApiResponseModel.Success(spMessage, new { user_id = newUserId }));
                }

                return InternalServerError(new Exception("Registration failed"));
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

                    // ✅ Read 'status' column, not 'message'
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

        // ✅ FORGOT PASSWORD API
        [HttpPost]
        [Route("forgot-password")]
        public IHttpActionResult ForgotPassword([FromBody] ForgotPasswordRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_mail_id", SqlDbType.VarChar, 255) { Value = model.user_mail_id.Trim().ToLower() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_forgot_password", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Unexpected error processing request."));

                DataRow row    = result.Rows[0];
                string status  = row["status"]?.ToString();
                string message = row["message"]?.ToString();

                if (status == "not_found")
                    return Content(System.Net.HttpStatusCode.NotFound, ApiResponseModel.Failure(message));

                // Token generated by SQL — read it back to build the reset link
                string token        = row["reset_token"]?.ToString();
                string fullName     = row["user_fullname"]?.ToString() ?? "";
                string frontendBase = ConfigurationManager.AppSettings["FrontendBaseUrl"]?.TrimEnd('/');
                string resetLink    = $"{frontendBase}/reset-password?token={Uri.EscapeDataString(token)}";

                EmailHelper.SendPasswordResetEmail(model.user_mail_id.Trim().ToLower(), fullName, resetLink);

                return Ok(ApiResponseModel.Success("Password reset link has been sent to your email address."));
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        // ✅ RESET PASSWORD API
        [HttpPost]
        [Route("reset-password")]
        public IHttpActionResult ResetPassword([FromBody] ResetPasswordRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                byte[] newPasswordHash = HashPassword(model.new_password);

                var parameters = new[]
                {
                    new SqlParameter("@token",        SqlDbType.VarChar,    256) { Value = model.token.Trim() },
                    new SqlParameter("@new_password", SqlDbType.VarBinary,   -1) { Value = newPasswordHash }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_reset_password", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Unexpected error processing request."));

                DataRow row    = result.Rows[0];
                string status  = row["status"]?.ToString();
                string message = row["message"]?.ToString();

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(message));
                    case "expired":
                        return Content(System.Net.HttpStatusCode.Gone, ApiResponseModel.Failure(message));
                    case "used":
                        return Content(System.Net.HttpStatusCode.Conflict, ApiResponseModel.Failure(message));
                    default: // "invalid"
                        return Content(System.Net.HttpStatusCode.BadRequest, ApiResponseModel.Failure(message));
                }
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