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