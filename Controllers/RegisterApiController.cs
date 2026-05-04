using System;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using VMeetTool.Filters;
using VMeetTool.Helpers;
using VMeetTool.Models;

namespace VMeetTool.Controllers
{
    // ─────────────────────────────────────────────────────────────────────
    // Design contract
    // ─────────────────────────────────────────────────────────────────────
    // Every stored procedure returns a result-set whose first row always
    // contains at minimum:
    //     status   VARCHAR  – snake_case constant  (e.g. 'success', 'not_found')
    //     message  VARCHAR  – human-readable text forwarded straight to client
    //
    // This controller is a thin HTTP adapter:
    //   1. Deserialise request body / route values
    //   2. Hash password where needed
    //   3. Call stored procedure
    //   4. Read status  →  map to HTTP status code
    //   5. Return ApiResponseModel with the SP's own message
    //
    // ALL business rules, validations, and error messages live in the SPs.
    // To change any error text or logic, update the SP — not this file.
    // ─────────────────────────────────────────────────────────────────────

    [RoutePrefix("api")]
    public class RegisterApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST api/register
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("register")]
        public IHttpActionResult Register([FromBody] RegisterRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_fullname",   SqlDbType.VarChar,   255) { Value = model.fullName.Trim() },
                    new SqlParameter("@user_mail_id",    SqlDbType.VarChar,   255) { Value = model.email.Trim().ToLower() },
                    new SqlParameter("@contact_details", SqlDbType.VarChar,    20) { Value = (model.phone ?? "").Trim() },
                    new SqlParameter("@password",        SqlDbType.VarBinary,  -1) { Value = HashPassword(model.password) }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_register_user", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        int    uid   = Convert.ToInt32(row["user_id"]);
                        string email = model.email.Trim().ToLower();
                        string fname = model.fullName.Trim();
                        string uname = email.Split('@')[0];
                        return Ok(ApiResponseModel.Success(msg, BuildAuthResponse(uid, uname, email, fname, (model.phone ?? "").Trim())));

                    case "email_exists":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "EMAIL_EXISTS"));

                    case "phone_exists":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "PHONE_EXISTS"));

                    default:
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "DUPLICATE"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/login
        // SP detects email vs. phone internally — no logic here.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequestModel model)
        {
            if (model == null || !ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_name", SqlDbType.VarChar,   255) { Value = model.identifier.Trim() },
                    new SqlParameter("@password",   SqlDbType.VarBinary,  -1) { Value = HashPassword(model.password) }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_login_user", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        int    userId = Convert.ToInt32(row["user_id"]);
                        string uname  = row["user_name"]?.ToString()     ?? "";
                        string full   = row["user_fullname"]?.ToString() ?? "";
                        string email  = row["user_mail_id"]?.ToString()  ?? "";
                        return Ok(ApiResponseModel.Success(msg, BuildAuthResponse(userId, uname, email, full)));

                    case "account_inactive":
                        return Content(HttpStatusCode.Forbidden,
                                       ApiResponseModel.Failure(msg, "ACCOUNT_INACTIVE"));

                    default: // invalid_credentials + anything unexpected
                        return Content(HttpStatusCode.Unauthorized,
                                       ApiResponseModel.Failure(msg, "INVALID_CREDENTIALS"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET api/me  (JWT protected)
        // No DB call needed — user info is already in the JWT claims.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("me")]
        [JwtAuthorize]
        public IHttpActionResult Me()
        {
            try
            {
                var identity = User.Identity as ClaimsIdentity;
                if (identity == null)
                    return Unauthorized();

                return Ok(ApiResponseModel.Success("User fetched", new UserResponseModel
                {
                    userId     = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
                    username   = identity.FindFirst(ClaimTypes.Name)?.Value
                               ?? identity.FindFirst("unique_name")?.Value ?? "",
                    email      = identity.FindFirst(ClaimTypes.Email)?.Value    ?? "",
                    fullName   = identity.FindFirst("full_name")?.Value         ?? "",
                    phone      = "",
                    isVerified = true,
                    createdAt  = ""
                }));
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/logout  (stateless JWT — client discards token)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("logout")]
        public IHttpActionResult Logout()
        {
            return Ok(ApiResponseModel.Success("Logged out successfully."));
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/verify-user
        // SP returns status row for every outcome.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("verify-user")]
        public IHttpActionResult VerifyUser([FromBody] VerifyUserRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@identifier", SqlDbType.VarChar, 255) { Value = model.identifier.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_verify_user", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, new VerifyUserResponse
                        {
                            userId      = row["user_id"]?.ToString()          ?? "",
                            fullName    = row["user_fullname"]?.ToString()    ?? "",
                            maskedEmail = MaskEmail(row["user_mail_id"]?.ToString()    ?? ""),
                            maskedPhone = MaskPhone(row["contact_details"]?.ToString() ?? "")
                        }));

                    case "not_found":
                        return Content(HttpStatusCode.NotFound,
                                       ApiResponseModel.Failure(msg, "NOT_FOUND"));

                    default:
                        return Content(HttpStatusCode.BadRequest,
                                       ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/refresh-token
        // JWT validation in C# (stateless — no DB round-trip needed).
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("refresh-token")]
        public IHttpActionResult RefreshToken([FromBody] RefreshTokenRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var principal = JwtHelper.ValidateToken(model.refreshToken);
                if (principal == null)
                    return Content(HttpStatusCode.Unauthorized,
                                   ApiResponseModel.Failure("Invalid or expired refresh token.", "TOKEN_EXPIRED"));

                var identity = principal.Identity as ClaimsIdentity;
                int    userId   = int.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                string userName = identity.FindFirst(ClaimTypes.Name)?.Value
                                ?? identity.FindFirst("unique_name")?.Value ?? "";
                string email    = identity.FindFirst(ClaimTypes.Email)?.Value   ?? "";
                string fullName = identity.FindFirst("full_name")?.Value        ?? "";

                return Ok(ApiResponseModel.Success("Token refreshed",
                          BuildAuthResponse(userId, userName, email, fullName)));
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/reset-password  (userId-based — profile/settings)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("reset-password")]
        public IHttpActionResult ResetPassword([FromBody] ResetPasswordByIdRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                int userId;
                if (!int.TryParse(model.userId, out userId) || userId <= 0)
                    return BadRequest("Invalid userId.");

                var parameters = new[]
                {
                    new SqlParameter("@user_id",      SqlDbType.Int)          { Value = userId },
                    new SqlParameter("@new_password", SqlDbType.VarBinary, -1) { Value = HashPassword(model.newPassword) }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_reset_password_by_id", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":   return Ok(ApiResponseModel.Success(msg));
                    case "not_found": return Content(HttpStatusCode.NotFound, ApiResponseModel.Failure(msg, "NOT_FOUND"));
                    default:          return Content(HttpStatusCode.BadRequest, ApiResponseModel.Failure(msg, "ERROR"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/reset-password-token  (email link flow)
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("reset-password-token")]
        public IHttpActionResult ResetPasswordByToken([FromBody] ResetPasswordByTokenRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@token",        SqlDbType.VarChar,   256) { Value = model.token.Trim() },
                    new SqlParameter("@new_password", SqlDbType.VarBinary,  -1) { Value = HashPassword(model.newPassword) }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_reset_password", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success": return Ok(ApiResponseModel.Success(msg));
                    case "expired": return Content(HttpStatusCode.Gone,     ApiResponseModel.Failure(msg, "TOKEN_EXPIRED"));
                    case "used":    return Content(HttpStatusCode.Conflict,  ApiResponseModel.Failure(msg, "TOKEN_USED"));
                    default:        return Content(HttpStatusCode.BadRequest, ApiResponseModel.Failure(msg, "TOKEN_INVALID"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/forgot-password
        // ─────────────────────────────────────────────────────────────
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
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        string token        = row["reset_token"]?.ToString() ?? "";
                        string fullName     = row["user_fullname"]?.ToString() ?? "";
                        string frontendBase = System.Configuration.ConfigurationManager
                                               .AppSettings["FrontendBaseUrl"]?.TrimEnd('/');
                        string resetLink    = $"{frontendBase}/reset-password?token={Uri.EscapeDataString(token)}";

                        EmailHelper.SendPasswordResetEmail(model.user_mail_id.Trim().ToLower(), fullName, resetLink);
                        return Ok(ApiResponseModel.Success("Password reset link has been sent to your email address."));

                    case "not_found":
                        return Content(HttpStatusCode.NotFound, ApiResponseModel.Failure(msg, "NOT_FOUND"));

                    default:
                        return Content(HttpStatusCode.BadRequest, ApiResponseModel.Failure(msg, "ERROR"));
                }
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // Shared helpers — no business logic, only infrastructure
        // ─────────────────────────────────────────────────────────────

        private static byte[] HashPassword(string plainText)
        {
            using (var sha256 = SHA256.Create())
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(plainText));
        }

        /// <summary>Builds a full auth response (token pair + user object).</summary>
        private static AuthResponseModel BuildAuthResponse(
            int userId, string userName, string email, string fullName, string phone = "")
        {
            return new AuthResponseModel
            {
                accessToken          = JwtHelper.GenerateToken(userId, userName, email, fullName),
                refreshToken         = JwtHelper.GenerateRefreshToken(userId, userName, email, fullName),
                accessTokenExpiresAt = JwtHelper.AccessTokenExpiresAt.ToString("o"),
                user = new UserResponseModel
                {
                    userId     = userId.ToString(),
                    username   = userName,
                    email      = email,
                    phone      = phone,
                    fullName   = fullName,
                    isVerified = true,
                    createdAt  = ""
                }
            };
        }

        /// <summary>Shows first 2 chars then masks up to the @ symbol: jo***@example.com</summary>
        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return "";
            int at = email.IndexOf('@');
            if (at <= 1) return email;
            int visible = Math.Min(2, at);
            return email.Substring(0, visible)
                 + new string('*', at - visible)
                 + email.Substring(at);
        }

        /// <summary>Shows only the last 4 digits: ******3210</summary>
        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return phone;
            return new string('*', phone.Length - 4) + phone.Substring(phone.Length - 4);
        }
    }
}
