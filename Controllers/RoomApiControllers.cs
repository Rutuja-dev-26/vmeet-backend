using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Http;
using VMeetTool.Filters;
using VMeetTool.Helpers;
using VMeetTool.Models;

namespace VMeetTool.Controllers
{
    [RoutePrefix("api/room")]
    [JwtAuthorize]
    public class RoomApiController : ApiController
    {
        // ─────────────────────────────────────────────────────────────
        // POST api/room/create
        // Header: Authorization: Bearer <token>
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
                    new SqlParameter("@user_id",   SqlDbType.Int)          { Value = model.user_id.Value },
                    new SqlParameter("@room_name", SqlDbType.VarChar, 200) { Value = model.room_name.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_create_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Room creation failed: no response from database."));

                var row = result.Rows[0];
                string spMessage = row["message"]?.ToString();

                if (spMessage != "Room Created")
                    return Content(System.Net.HttpStatusCode.Conflict,
                                   ApiResponseModel.Failure(spMessage));

                string roomCode = row["room_code"]?.ToString();

                return Ok(ApiResponseModel.Success(spMessage, new
                {
                    user_id = model.user_id.Value,
                    room_name = model.room_name.Trim(),
                    room_code = roomCode
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        // ─────────────────────────────────────────────────────────────
        // GET api/room/list/{user_id}
        // Header: Authorization: Bearer <token>
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        [Route("list/{user_id:int}")]
        public IHttpActionResult GetRoomsByUser(int user_id)
        {
            if (user_id <= 0)
                return BadRequest("user_id must be greater than 0.");

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_id", SqlDbType.Int) { Value = user_id }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_get_room_code_by_user", parameters);

                if (result == null || result.Rows.Count == 0)
                    return NotFound();

                var rooms = new List<object>();

                foreach (DataRow row in result.Rows)
                {
                    rooms.Add(new
                    {
                        room_id = Convert.ToInt32(row["room_id"]),
                        room_code = row["room_code"]?.ToString(),
                        is_live = Convert.ToBoolean(row["is_live"])
                    });
                }

                return Ok(ApiResponseModel.Success("Rooms fetched successfully.", rooms));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }
                                 
        // ─────────────────────────────────────────────────────────────
        // POST api/room/join
        // Header: Authorization: Bearer <token>
        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        [Route("join")]
        public IHttpActionResult JoinRoom([FromBody] JoinRoomRequestModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var parameters = new[]
                {
                    new SqlParameter("@user_id",          SqlDbType.Int)          { Value = model.user_id.Value },
                    new SqlParameter("@participant_name",  SqlDbType.VarChar, 200) { Value = model.participant_name.Trim() },
                    new SqlParameter("@room_code",         SqlDbType.VarChar,  50) { Value = model.room_code.Trim() }
                };

                DataTable result = DbHelper.ExecuteStoredProcedure("vcadmin.sp_join_room", parameters);

                if (result == null || result.Rows.Count == 0)
                    return InternalServerError(new Exception("Join room failed: no response from database."));

                var row = result.Rows[0];
                string spMessage = row["message"]?.ToString();

                if (spMessage == "Invalid Room Code")
                    return Content(System.Net.HttpStatusCode.BadRequest,
                                   ApiResponseModel.Failure("Invalid room code. Please check and try again."));

                return Ok(ApiResponseModel.Success(spMessage, new
                {
                    user_id = model.user_id.Value,
                    participant_name = model.participant_name.Trim(),
                    room_code = model.room_code.Trim(),
                    room_id = Convert.ToInt32(row["room_id"])
                }));
            }
            catch (SqlException sqlEx) { return InternalServerError(sqlEx); }
            catch (Exception ex) { return InternalServerError(ex); }
        }
    }
}