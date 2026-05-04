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
    // Design contract  (identical to RegisterApiController / MeetingApiController)
    // ─────────────────────────────────────────────────────────────────────
    // Every stored procedure returns a result-set whose first row always
    // contains at minimum:
    //     status   VARCHAR  – snake_case constant  (e.g. 'success', 'not_found')
    //     message  VARCHAR  – human-readable text forwarded straight to client
    //
    // This controller is a thin HTTP adapter:
    //   1. Deserialise request body / route values
    //   2. Call stored procedure
    //   3. Read status  →  map to HTTP status code
    //   4. Return ApiResponseModel with the SP's own message
    //
    // ALL business rules, validations, and error messages live in the SPs.
    // To change any logic or error text, update the SP — not this file.
    // ─────────────────────────────────────────────────────────────────────

    [RoutePrefix("api/room")]
    [JwtAuthorize]
    public class RoomApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST  api/room/create
        // Creates the caller's single permanent room.
        // SP enforces: one room per user, unique room name.
        // SP generates the URL-safe slug (room_code) from room_name.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("create")]
        public IHttpActionResult CreateRoom([FromBody] CreateRoomRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_id",   SqlDbType.Int)          { Value = model.user_id },
                    new SqlParameter("@room_name", SqlDbType.VarChar, 200) { Value = model.room_name.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_create_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Room creation failed: no response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Content(HttpStatusCode.Created, ApiResponseModel.Success(msg, new
                        {
                            room_id      = row["room_id"],
                            room_name    = row["room_name"]?.ToString(),
                            room_code    = row["room_code"]?.ToString(),
                            created_date = row["created_date"]?.ToString()
                        }));

                    case "room_exists":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "ROOM_EXISTS"));

                    case "name_taken":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "NAME_TAKEN"));

                    default:
                        return Content(HttpStatusCode.BadRequest,
                                       ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
                }
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET  api/room/my/{user_id}
        // Returns the user's room and all its sub-rooms in one call.
        // Makes two SP calls: sp_get_my_room → sp_get_sub_rooms.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("my/{user_id:int}")]
        public IHttpActionResult GetMyRoom(int user_id)
        {
            if (user_id <= 0)
                return BadRequest("Invalid user_id.");

            try
            {
                // ── Step 1: fetch main room ───────────────────────────
                var roomParams = new[]
                {
                    new SqlParameter("@user_id", SqlDbType.Int) { Value = user_id }
                };

                DataTable roomResult = DbHelper.ExecuteStoredProcedure("vcadmin.sp_get_my_room", roomParams);

                if (roomResult == null || roomResult.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow roomRow    = roomResult.Rows[0];
                string  roomStatus = roomRow["status"]?.ToString();
                string  roomMsg    = roomRow["message"]?.ToString() ?? "";

                if (roomStatus == "not_found")
                    return Content(HttpStatusCode.NotFound, ApiResponseModel.Failure(roomMsg, "NOT_FOUND"));

                if (roomStatus != "success")
                    return Content(HttpStatusCode.BadRequest, ApiResponseModel.Failure(roomMsg, roomStatus?.ToUpper() ?? "ERROR"));

                int roomId = Convert.ToInt32(roomRow["room_id"]);

                // ── Step 2: fetch sub-rooms for this room ─────────────
                var subParams = new[]
                {
                    new SqlParameter("@room_id", SqlDbType.Int) { Value = roomId }
                };

                DataTable subResult = DbHelper.ExecuteStoredProcedure("vcadmin.sp_get_sub_rooms", subParams);

                var subRooms = new List<object>();
                if (subResult != null)
                {
                    foreach (DataRow sr in subResult.Rows)
                    {
                        subRooms.Add(new
                        {
                            sub_room_id  = sr["sub_room_id"],
                            room_id      = sr["room_id"],
                            sub_name     = sr["sub_name"]?.ToString(),
                            room_code    = sr["room_code"]?.ToString(),
                            status       = sr["status"]?.ToString(),
                            created_date = sr["created_date"]?.ToString(),
                            ended_date   = sr["ended_date"] == DBNull.Value ? null : sr["ended_date"]?.ToString()
                        });
                    }
                }

                return Ok(ApiResponseModel.Success(roomMsg, new
                {
                    room_id      = roomRow["room_id"],
                    room_name    = roomRow["room_name"]?.ToString(),
                    room_code    = roomRow["room_code"]?.ToString(),
                    created_date = roomRow["created_date"]?.ToString(),
                    sub_rooms    = subRooms
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET  api/room/validate/{room_code}
        // Called from the join screen before entering a room.
        // Checks both main rooms and sub-rooms.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("validate/{room_code}")]
        public IHttpActionResult ValidateRoomCode(string room_code)
        {
            if (string.IsNullOrWhiteSpace(room_code))
                return BadRequest("room_code is required.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_code", SqlDbType.VarChar, 200) { Value = room_code.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_validate_room_code", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, new
                        {
                            room_id      = row["room_id"]      == DBNull.Value ? (object)null : row["room_id"],
                            sub_room_id  = row["sub_room_id"]  == DBNull.Value ? (object)null : row["sub_room_id"],
                            room_name    = row["room_name"]?.ToString(),
                            room_type    = row["room_type"]?.ToString(),
                            host_user_id = row["host_user_id"] == DBNull.Value ? (object)null : row["host_user_id"]
                        }));

                    case "room_ended":
                        return Content(HttpStatusCode.Gone,
                                       ApiResponseModel.Failure(msg, "ROOM_ENDED"));

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
        // GET  api/room/check-name?name={room_name}
        // Live availability check while the user types a room name.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("check-name")]
        public IHttpActionResult CheckRoomName([FromUri] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("name query parameter is required.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_name", SqlDbType.VarChar, 200) { Value = name.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_check_room_name", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("No response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "available":
                        return Ok(ApiResponseModel.Success(msg, new { available = true }));

                    case "name_taken":
                        return Ok(ApiResponseModel.Success(msg, new { available = false }));

                    default:
                        return Content(HttpStatusCode.BadRequest,
                                       ApiResponseModel.Failure(msg, status?.ToUpper() ?? "ERROR"));
                }
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE  api/room/delete/{room_id}/{user_id}
        // Soft-deletes the room and ends all its active sub-rooms.
        // SP verifies that user_id is the room owner.
        // ─────────────────────────────────────────────────────────────
        [HttpDelete]
        [Route("delete/{room_id:int}/{user_id:int}")]
        public IHttpActionResult DeleteRoom(int room_id, int user_id)
        {
            if (room_id <= 0) return BadRequest("Invalid room_id.");
            if (user_id <= 0) return BadRequest("Invalid user_id.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_id",  SqlDbType.Int) { Value = room_id },
                    new SqlParameter("@user_id",  SqlDbType.Int) { Value = user_id }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_delete_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Delete failed: no response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, new { room_id = room_id }));

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
        // POST  api/room/sub-room/create
        // Creates an ephemeral sub-room under an existing main room.
        // SP generates a random one-time room_code.
        // SP verifies user owns the parent room.
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("sub-room/create")]
        public IHttpActionResult CreateSubRoom([FromBody] CreateSubRoomRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_id",  SqlDbType.Int)          { Value = model.room_id },
                    new SqlParameter("@user_id",  SqlDbType.Int)          { Value = model.user_id },
                    new SqlParameter("@sub_name", SqlDbType.VarChar, 200) { Value = model.sub_name.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_create_sub_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Sub-room creation failed: no response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Content(HttpStatusCode.Created, ApiResponseModel.Success(msg, new
                        {
                            sub_room_id  = row["sub_room_id"],
                            room_id      = row["room_id"],
                            sub_name     = row["sub_name"]?.ToString(),
                            room_code    = row["room_code"]?.ToString(),
                            status       = row["sub_status"]?.ToString(),
                            created_date = row["created_date"]?.ToString()
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
        // GET  api/room/{room_id}/sub-rooms
        // Lists all sub-rooms (active + ended) for a main room.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("{room_id:int}/sub-rooms")]
        public IHttpActionResult GetSubRooms(int room_id)
        {
            if (room_id <= 0)
                return BadRequest("Invalid room_id.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@room_id", SqlDbType.Int) { Value = room_id }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_get_sub_rooms", parameters);

                var subRooms = new List<object>();
                if (result != null)
                {
                    foreach (DataRow row in result.Rows)
                    {
                        subRooms.Add(new
                        {
                            sub_room_id  = row["sub_room_id"],
                            room_id      = row["room_id"],
                            sub_name     = row["sub_name"]?.ToString(),
                            room_code    = row["room_code"]?.ToString(),
                            status       = row["status"]?.ToString(),
                            created_date = row["created_date"]?.ToString(),
                            ended_date   = row["ended_date"] == DBNull.Value ? null : row["ended_date"]?.ToString()
                        });
                    }
                }

                return Ok(ApiResponseModel.Success("Sub-rooms fetched.", subRooms));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex)       { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE  api/room/sub-room/end/{sub_room_id}/{user_id}
        // Marks a sub-room as ended.
        // SP verifies user owns the parent main room.
        // ─────────────────────────────────────────────────────────────
        [HttpDelete]
        [Route("sub-room/end/{sub_room_id:int}/{user_id:int}")]
        public IHttpActionResult EndSubRoom(int sub_room_id, int user_id)
        {
            if (sub_room_id <= 0) return BadRequest("Invalid sub_room_id.");
            if (user_id     <= 0) return BadRequest("Invalid user_id.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@sub_room_id", SqlDbType.Int) { Value = sub_room_id },
                    new SqlParameter("@user_id",     SqlDbType.Int) { Value = user_id }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_end_sub_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("End sub-room failed: no response from database."));

                DataRow row    = result.Rows[0];
                string  status = row["status"]?.ToString();
                string  msg    = row["message"]?.ToString() ?? "";

                switch (status)
                {
                    case "success":
                        return Ok(ApiResponseModel.Success(msg, new { sub_room_id = sub_room_id }));

                    case "already_ended":
                        return Content(HttpStatusCode.Conflict,
                                       ApiResponseModel.Failure(msg, "ALREADY_ENDED"));

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
    }
}
