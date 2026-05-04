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
    // Snapshot tracking — saves metadata entries after the image file
    // has been stored on disk by the Node.js server.
    //
    // Endpoints
    //   POST  api/snapshot                      – create snapshot entry
    //   GET   api/snapshot/room/{room_code}     – list snapshots for room
    // ─────────────────────────────────────────────────────────────────────

    [RoutePrefix("api/snapshot")]
    [JwtAuthorize]
    public class SnapshotApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST  api/snapshot
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("")]
        public IHttpActionResult Create([FromBody] CreateSnapshotRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code",   SqlDbType.NVarChar, 32)   { Value = model.room_code.Trim() },
                    new SqlParameter("@captured_by", SqlDbType.NVarChar, 100)  { Value = model.captured_by.Trim() },
                    new SqlParameter("@file_path",   SqlDbType.NVarChar, 500)  { Value = model.file_path.Trim() },
                    new SqlParameter("@metadata",    SqlDbType.NVarChar, -1)   { Value = (object)model.metadata ?? DBNull.Value },
                    new SqlParameter("@category_id", SqlDbType.Int)            { Value = (object)model.category_id ?? DBNull.Value },
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_create_snapshot", parameters);

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
                            snapshot_id = row["snapshot_id"],
                            room_id     = row["room_id"],
                            created_at  = row["created_at"]?.ToString(),
                        }));

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
        // GET  api/snapshot/room/{room_code}
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

                DataTable result = DbHelper.ExecuteStoredProcedure("dbo.sp_get_snapshots", parameters);

                var snapshots = new List<object>();
                if (result != null)
                {
                    foreach (DataRow row in result.Rows)
                    {
                        snapshots.Add(new
                        {
                            snapshot_id = row["snapshot_id"],
                            room_id     = row["room_id"],
                            category_id = row["category_id"] == DBNull.Value ? (object)null : row["category_id"],
                            captured_by = row["captured_by"]?.ToString(),
                            file_path   = row["file_path"]?.ToString(),
                            metadata    = row["metadata"] == DBNull.Value ? null : row["metadata"]?.ToString(),
                            created_at  = row["created_at"]?.ToString(),
                        });
                    }
                }

                return Ok(ApiResponseModel.Success("Snapshots fetched.", snapshots));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }
    }
}
