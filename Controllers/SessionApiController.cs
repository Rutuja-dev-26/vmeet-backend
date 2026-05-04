using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Web.Http;
using VMeetTool.Filters;
using VMeetTool.Helpers;
using VMeetTool.Models;

namespace VMeetTool.Controllers
{
    // ─────────────────────────────────────────────────────────────────────
    // Session tracking — records who joined / left / ended each call.
    // Operates on dbo.Rooms and dbo.Participants (Node.js-schema tables).
    //
    // Endpoints
    //   POST  api/session/join          – participant joins room
    //   POST  api/session/leave         – participant leaves room
    //   POST  api/session/end           – host ends the call
    //   GET   api/session/{room_code}   – room + participants history
    // ─────────────────────────────────────────────────────────────────────

    [RoutePrefix("api/session")]
    [JwtAuthorize]
    public class SessionApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST  api/session/join
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("join")]
        public IHttpActionResult Join([FromBody] JoinSessionRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code",        SqlDbType.NVarChar, 32)  { Value = model.room_code.Trim() },
                    new SqlParameter("@user_id",          SqlDbType.NVarChar, 100) { Value = model.user_id.Trim() },
                    new SqlParameter("@display_name",     SqlDbType.NVarChar, 150) { Value = model.display_name.Trim() },
                    new SqlParameter("@role",             SqlDbType.NVarChar, 20)  { Value = model.role.Trim() },
                    new SqlParameter("@client_id",        SqlDbType.Int)           { Value = model.client_id },
                    new SqlParameter("@max_participants", SqlDbType.Int)           { Value = model.max_participants },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_join_session", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                if (status == "success")
                    return Content(HttpStatusCode.Created, ApiResponseModel.Success(msg, new
                    {
                        room_id        = row["room_id"],
                        participant_id = row["participant_id"],
                    }));

                return Content(HttpStatusCode.BadRequest,
                               ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST  api/session/leave
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("leave")]
        public IHttpActionResult Leave([FromBody] LeaveSessionRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code", SqlDbType.NVarChar, 32)  { Value = model.room_code.Trim() },
                    new SqlParameter("@user_id",   SqlDbType.NVarChar, 100) { Value = model.user_id.Trim() },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_leave_session", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, null));

                    case "not_found":
                        return Content(HttpStatusCode.NotFound,
                                       ApiResponseModel.Failure(msg, "NOT_FOUND"));

                    default:
                        return Content(HttpStatusCode.BadRequest,
                                       ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
                }
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST  api/session/end
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("end")]
        public IHttpActionResult End([FromBody] EndSessionRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code", SqlDbType.NVarChar, 32) { Value = model.room_code.Trim() },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_end_session", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, new { room_id = row["room_id"] }));

                    case "not_found":
                        return Content(HttpStatusCode.NotFound,
                                       ApiResponseModel.Failure(msg, "NOT_FOUND"));

                    default:
                        return Content(HttpStatusCode.BadRequest,
                                       ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
                }
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET  api/session/{room_code}
        // Returns session history (all rooms) + participants for the
        // most recent session.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("{room_code}")]
        public IHttpActionResult GetSession(string room_code)
        {
            if (string.IsNullOrWhiteSpace(room_code))
                return BadRequest("room_code is required.");

            try
            {
                // ── Step 1: fetch room sessions ───────────────────────
                var roomParams = new[]
                {
                    new SqlParameter("@room_code", SqlDbType.NVarChar, 32) { Value = room_code.Trim() },
                };

                DataTable roomResult = DbHelper.ExecuteStoredProcedure("dbo.sp_get_session_room", roomParams);

                var sessions = new List<object>();
                int latestRoomId = 0;

                if (roomResult != null)
                {
                    foreach (DataRow row in roomResult.Rows)
                    {
                        int rid = Convert.ToInt32(row["room_id"]);
                        if (latestRoomId == 0) latestRoomId = rid;

                        sessions.Add(new
                        {
                            room_id          = rid,
                            room_code        = row["room_code"]?.ToString(),
                            status           = row["status"]?.ToString(),
                            max_participants  = row["max_participants"],
                            created_at       = row["created_at"]?.ToString(),
                            ended_at         = row["ended_at"] == DBNull.Value ? null : row["ended_at"]?.ToString(),
                        });
                    }
                }

                // ── Step 2: participants for the most recent session ──
                var participants = new List<object>();
                if (latestRoomId > 0)
                {
                    var pParams = new[]
                    {
                        new SqlParameter("@room_id", SqlDbType.Int) { Value = latestRoomId },
                    };

                    DataTable pResult = DbHelper.ExecuteStoredProcedure("dbo.sp_get_session_participants", pParams);

                    if (pResult != null)
                    {
                        foreach (DataRow row in pResult.Rows)
                        {
                            participants.Add(new
                            {
                                participant_id = row["participant_id"],
                                user_id        = row["user_id"]?.ToString(),
                                display_name   = row["display_name"]?.ToString(),
                                role           = row["role"]?.ToString(),
                                joined_at      = row["joined_at"]?.ToString(),
                                left_at        = row["left_at"] == DBNull.Value ? null : row["left_at"]?.ToString(),
                            });
                        }
                    }
                }

                return Ok(ApiResponseModel.Success("Session data fetched.", new
                {
                    sessions,
                    latest_participants = participants,
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }
    }

    // Inline model — only used by this controller
    public class EndSessionRequestModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string room_code { get; set; }
    }
}
