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
    // Recording lifecycle tracking — mirrors what Node.js writes to
    // dbo.RecordingLedger directly.  Call these endpoints instead of
    // writing to the DB directly when RemoteDataService is active.
    //
    // Endpoints
    //   POST  api/recording/start               – create ledger entry
    //   POST  api/recording/finalize            – mark as ready
    //   GET   api/recording/room/{room_code}    – list recordings for room
    // ─────────────────────────────────────────────────────────────────────

    [RoutePrefix("api/recording")]
    [JwtAuthorize]
    public class RecordingApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST  api/recording/start
        // Called immediately when host clicks "Start Recording".
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("start")]
        public IHttpActionResult Start([FromBody] StartRecordingRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@session_id", SqlDbType.NVarChar, 100) { Value = model.session_id.Trim() },
                    new SqlParameter("@room_code",  SqlDbType.NVarChar, 32)  { Value = model.room_code.Trim() },
                    new SqlParameter("@client_id",  SqlDbType.Int)           { Value = model.client_id },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_start_recording", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Content(HttpStatusCode.Created, ApiResponseModel.Success(msg, new
                        {
                            recording_id = row["recording_id"],
                            room_id      = row["room_id"],
                            session_id   = row["session_id"]?.ToString(),
                        }));

                    case "already_exists":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "ALREADY_EXISTS"));

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
        // POST  api/recording/finalize
        // Called after FFmpeg processing completes on the Node.js side.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("finalize")]
        public IHttpActionResult Finalize([FromBody] FinalizeRecordingRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@session_id",       SqlDbType.NVarChar, 100) { Value = model.session_id.Trim() },
                    new SqlParameter("@file_path",        SqlDbType.NVarChar, 500) { Value = model.file_path.Trim() },
                    new SqlParameter("@chunk_count",      SqlDbType.Int)           { Value = model.chunk_count },
                    new SqlParameter("@duration_seconds", SqlDbType.Int)           { Value = model.duration_seconds },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_finalize_recording", parameters);

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
        // GET  api/recording/room/{room_code}
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("room/{room_code}")]
        public IHttpActionResult GetByRoom(string room_code)
        {
            if (string.IsNullOrWhiteSpace(room_code))
                return BadRequest("room_code is required.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code", SqlDbType.NVarChar, 32) { Value = room_code.Trim() },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_get_recordings", parameters);

                var recordings = new List<object>();
                if (result != null)
                {
                    foreach (DataRow row in result.Rows)
                    {
                        recordings.Add(new
                        {
                            recording_id     = row["recording_id"],
                            room_id          = row["room_id"],
                            session_id       = row["session_id"]?.ToString(),
                            status           = row["status"]?.ToString(),
                            file_path        = row["file_path"] == DBNull.Value ? null : row["file_path"]?.ToString(),
                            chunk_count      = row["chunk_count"],
                            duration_seconds = row["duration_seconds"] == DBNull.Value ? (object)null : row["duration_seconds"],
                            created_at       = row["created_at"]?.ToString(),
                            completed_at     = row["completed_at"] == DBNull.Value ? null : row["completed_at"]?.ToString(),
                        });
                    }
                }

                return Ok(ApiResponseModel.Success("Recordings fetched.", recordings));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }
    }
}
