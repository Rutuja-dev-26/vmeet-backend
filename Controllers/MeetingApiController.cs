using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Http;
using VMeetTool.Filters;
using VMeetTool.Helpers;
using VMeetTool.Models;

namespace VMeetTool.Controllers
{
    [RoutePrefix("api/meeting")]
    [JwtAuthorize]
    public class MeetingApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST api/meeting/create
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("create")]
        public IHttpActionResult CreateMeeting([FromBody] CreateMeetingRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            DateTime startTime, endTime;
            try
            {
                startTime = model.ParsedStartTime;
                endTime = model.ParsedEndTime;
            }
            catch
            {
                return BadRequest("Invalid date format. Use dd-MM-yyyy HH:mm e.g. '25-04-2026 10:00'");
            }

            if (startTime >= endTime)
                return BadRequest("end_time must be greater than start_time.");

            try
            {
                string meetingCode = GenerateMeetingCode();

                var parameters = new[]
                {
                    new SqlParameter("@host_user_id", SqlDbType.Int)          { Value = model.host_user_id },
                    new SqlParameter("@title",        SqlDbType.VarChar, 200) { Value = model.title.Trim() },
                    new SqlParameter("@description",  SqlDbType.VarChar, 500) { Value = (object)model.description?.Trim() ?? DBNull.Value },
                    new SqlParameter("@meeting_code", SqlDbType.VarChar,  20) { Value = meetingCode },
                    new SqlParameter("@start_time",   SqlDbType.DateTime)     { Value = startTime },
                    new SqlParameter("@end_time",     SqlDbType.DateTime)     { Value = endTime }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_create_meeting", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Meeting creation failed: no response from database."));

                return Ok(ApiResponseModel.Success(result.Rows[0]["message"]?.ToString(), new
                {
                    meeting_code = meetingCode,
                    title = model.title.Trim(),
                    host_user_id = model.host_user_id,
                    start_time = startTime.ToString("dd-MM-yyyy HH:mm"),
                    end_time = endTime.ToString("dd-MM-yyyy HH:mm")
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE api/meeting/delete/{meeting_id}
        // ─────────────────────────────────────────────────────────────
        [HttpDelete]
        [Route("delete/{meeting_id:int}")]
        public IHttpActionResult DeleteMeeting(int meeting_id)
        {
            if (meeting_id <= 0)
                return BadRequest("Invalid meeting_id.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@meeting_id", SqlDbType.Int) { Value = meeting_id }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_delete_meeting", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Delete failed: no response from database."));

                return Ok(ApiResponseModel.Success(result.Rows[0]["message"]?.ToString(), new
                {
                    meeting_id = meeting_id
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET api/meeting/details/{meeting_code}
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("details/{meeting_code}")]
        public IHttpActionResult GetMeetingDetails(string meeting_code)
        {
            if (string.IsNullOrWhiteSpace(meeting_code))
                return BadRequest("meeting_code is required.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@meeting_code", SqlDbType.VarChar, 20) { Value = meeting_code.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_get_meeting_details", parameters);

                if (result == null || result.Rows.Count == 0)
                    return NotFound();

                var row = result.Rows[0];
                var meeting = new
                {
                    meeting_id = row["meeting_id"],
                    host_user_id = row["host_user_id"],
                    title = row["title"],
                    description = row["description"],
                    meeting_code = row["meeting_code"],
                    start_time = Convert.ToDateTime(row["start_time"]).ToString("dd-MM-yyyy HH:mm"),
                    end_time = Convert.ToDateTime(row["end_time"]).ToString("dd-MM-yyyy HH:mm")
                };

                return Ok(ApiResponseModel.Success("Meeting details fetched.", meeting));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // POST api/meeting/join
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("join")]
        public IHttpActionResult JoinMeeting([FromBody] JoinMeetingRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);


            try
            {
                var parameters = new[]
   {
    new SqlParameter("@user_id",      SqlDbType.Int)         { Value = model.user_id.Value },
    new SqlParameter("@meeting_code", SqlDbType.VarChar, 20) { Value = model.meeting_code.Trim() }
};

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_join_meeting", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Join failed: no response from database."));

                string spMessage = result.Rows[0]["message"]?.ToString();

                if (spMessage == "Invalid Meeting Code")
                    return Content(System.Net.HttpStatusCode.BadRequest,
                                   ApiResponseModel.Failure(spMessage));

                return Ok(ApiResponseModel.Success(spMessage, new
                {
                    user_id = model.user_id,
                    meeting_code = model.meeting_code.Trim()
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        private static string GenerateMeetingCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rng = new Random();
            var code = new char[8];
            for (int i = 0; i < code.Length; i++)
                code[i] = chars[rng.Next(chars.Length)];
            return "VMT-" + new string(code);
        }
    }
}