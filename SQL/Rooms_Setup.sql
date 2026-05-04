-- ============================================================
-- VMeet  –  Rooms & Sub-Rooms Setup
-- Run this on SQL Server database (vmeet).
-- Safe to re-run: tables use IF NOT EXISTS, SPs are dropped
-- and recreated on every run.
--
-- Design rules  (same as Core_SPs.sql)
-- ────────────────────────────────────────────────────────────
-- • Every SP always returns exactly ONE result-set row.
-- • That row ALWAYS contains  status  and  message  columns.
-- • status values are lowercase_snake_case constants.
-- • All business-rule decisions live here — not in C#.
-- • C# only maps  status  →  HTTP code  and forwards  message
--   to the client unchanged.
-- ============================================================

USE vmeet;
GO


-- ════════════════════════════════════════════════════════════
-- TABLE:  vcadmin.rooms
--   One permanent room per user.
--   room_code is a URL-safe slug derived from room_name.
-- ════════════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE  TABLE_SCHEMA = 'vcadmin'
      AND  TABLE_NAME   = 'rooms'
)
BEGIN
    CREATE TABLE vcadmin.rooms (
        room_id      INT           IDENTITY(1,1) PRIMARY KEY,
        user_id      INT           NOT NULL,
        room_name    VARCHAR(200)  NOT NULL,
        room_code    VARCHAR(200)  NOT NULL UNIQUE,   -- slug  e.g. "dr-kim-office"
        is_active    BIT           NOT NULL DEFAULT 1,
        created_date DATETIME      NOT NULL DEFAULT GETDATE(),
        updated_date DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_rooms_user_id   ON vcadmin.rooms (user_id);
    CREATE INDEX IX_rooms_room_code ON vcadmin.rooms (room_code);
    PRINT 'Table vcadmin.rooms created.';
END
ELSE
    PRINT 'Table vcadmin.rooms already exists.';
GO


-- ════════════════════════════════════════════════════════════
-- TABLE:  vcadmin.sub_rooms
--   Many ephemeral sub-rooms per main room.
--   room_code is a random hex token,  status = active | ended.
-- ════════════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE  TABLE_SCHEMA = 'vcadmin'
      AND  TABLE_NAME   = 'sub_rooms'
)
BEGIN
    CREATE TABLE vcadmin.sub_rooms (
        sub_room_id  INT           IDENTITY(1,1) PRIMARY KEY,
        room_id      INT           NOT NULL,
        sub_name     VARCHAR(200)  NOT NULL,
        room_code    VARCHAR(100)  NOT NULL UNIQUE,   -- e.g. "sub-a3f9x2q1"
        status       VARCHAR(20)   NOT NULL DEFAULT 'active',   -- active | ended
        created_date DATETIME      NOT NULL DEFAULT GETDATE(),
        ended_date   DATETIME      NULL
    );
    CREATE INDEX IX_sub_rooms_room_id   ON vcadmin.sub_rooms (room_id);
    CREATE INDEX IX_sub_rooms_room_code ON vcadmin.sub_rooms (room_code);
    CREATE INDEX IX_sub_rooms_status    ON vcadmin.sub_rooms (status);
    PRINT 'Table vcadmin.sub_rooms created.';
END
ELSE
    PRINT 'Table vcadmin.sub_rooms already exists.';
GO


-- ════════════════════════════════════════════════════════════
-- 1.  sp_create_room
--     Creates a user's single permanent room.
--     Enforces:
--       • One room per user  → room_exists
--       • Unique room name   → name_taken
--     Generates a URL-safe slug from the room name.
--     Handles slug collisions by appending -2, -3, ...
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_create_room', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_create_room;
GO

CREATE PROCEDURE vcadmin.sp_create_room
    @user_id   INT,
    @room_name VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Guard: only one room per user ─────────────────────────────────────
    IF EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  user_id   = @user_id
          AND  is_active = 1
    )
    BEGIN
        SELECT 'room_exists'                                              AS status,
               'You already have a room. Only one room is allowed per user.' AS message,
               NULL AS room_id, NULL AS room_code, NULL AS room_name;
        RETURN;
    END

    -- ── Guard: room name must be globally unique (case-insensitive) ───────
    IF EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  LOWER(room_name) = LOWER(LTRIM(RTRIM(@room_name)))
          AND  is_active = 1
    )
    BEGIN
        SELECT 'name_taken'                                                    AS status,
               'This room name is already taken. Please choose a different name.' AS message,
               NULL AS room_id, NULL AS room_code, NULL AS room_name;
        RETURN;
    END

    -- ── Slugify room_name → room_code ─────────────────────────────────────
    -- Rules: lowercase · spaces→hyphens · strip common special chars
    --        collapse consecutive hyphens · trim leading/trailing hyphens
    DECLARE @slug      VARCHAR(200);
    DECLARE @base_slug VARCHAR(200);
    DECLARE @counter   INT = 0;

    SET @base_slug = LOWER(LTRIM(RTRIM(@room_name)));

    -- Replace spaces with hyphens
    SET @base_slug = REPLACE(@base_slug, ' ',  '-');

    -- Strip common special characters
    SET @base_slug = REPLACE(@base_slug, '''', '');
    SET @base_slug = REPLACE(@base_slug, '"',  '');
    SET @base_slug = REPLACE(@base_slug, '`',  '');
    SET @base_slug = REPLACE(@base_slug, '.',  '');
    SET @base_slug = REPLACE(@base_slug, ',',  '');
    SET @base_slug = REPLACE(@base_slug, '!',  '');
    SET @base_slug = REPLACE(@base_slug, '?',  '');
    SET @base_slug = REPLACE(@base_slug, '@',  '');
    SET @base_slug = REPLACE(@base_slug, '#',  '');
    SET @base_slug = REPLACE(@base_slug, '$',  '');
    SET @base_slug = REPLACE(@base_slug, '%',  '');
    SET @base_slug = REPLACE(@base_slug, '&',  '');
    SET @base_slug = REPLACE(@base_slug, '*',  '');
    SET @base_slug = REPLACE(@base_slug, '(',  '');
    SET @base_slug = REPLACE(@base_slug, ')',  '');
    SET @base_slug = REPLACE(@base_slug, '[',  '');
    SET @base_slug = REPLACE(@base_slug, ']',  '');
    SET @base_slug = REPLACE(@base_slug, '{',  '');
    SET @base_slug = REPLACE(@base_slug, '}',  '');
    SET @base_slug = REPLACE(@base_slug, '+',  '');
    SET @base_slug = REPLACE(@base_slug, '=',  '');
    SET @base_slug = REPLACE(@base_slug, '/',  '');
    SET @base_slug = REPLACE(@base_slug, '\',  '');
    SET @base_slug = REPLACE(@base_slug, ':',  '');
    SET @base_slug = REPLACE(@base_slug, ';',  '');
    SET @base_slug = REPLACE(@base_slug, '<',  '');
    SET @base_slug = REPLACE(@base_slug, '>',  '');
    SET @base_slug = REPLACE(@base_slug, '|',  '');
    SET @base_slug = REPLACE(@base_slug, '^',  '');
    SET @base_slug = REPLACE(@base_slug, '~',  '');
    SET @base_slug = REPLACE(@base_slug, '_',  '-');

    -- Collapse consecutive hyphens (e.g. "a--b" → "a-b")
    WHILE CHARINDEX('--', @base_slug) > 0
        SET @base_slug = REPLACE(@base_slug, '--', '-');

    -- Trim leading hyphens
    WHILE LEN(@base_slug) > 0 AND LEFT(@base_slug, 1) = '-'
        SET @base_slug = SUBSTRING(@base_slug, 2, LEN(@base_slug));

    -- Trim trailing hyphens
    WHILE LEN(@base_slug) > 0 AND RIGHT(@base_slug, 1) = '-'
        SET @base_slug = LEFT(@base_slug, LEN(@base_slug) - 1);

    -- Safety: empty slug fallback (should not happen for valid input)
    IF LEN(@base_slug) = 0
        SET @base_slug = 'room';

    SET @slug = @base_slug;

    -- ── Resolve slug collision: append -2, -3, … ─────────────────────────
    WHILE EXISTS (
        SELECT 1 FROM vcadmin.rooms WHERE room_code = @slug
    )
    BEGIN
        SET @counter = @counter + 1;
        SET @slug    = @base_slug + '-' + CAST(@counter AS VARCHAR(10));
    END

    -- ── Insert room ───────────────────────────────────────────────────────
    DECLARE @new_room_id INT;

    INSERT INTO vcadmin.rooms (user_id, room_name, room_code, is_active, created_date, updated_date)
    VALUES (@user_id, LTRIM(RTRIM(@room_name)), @slug, 1, GETDATE(), GETDATE());

    SET @new_room_id = SCOPE_IDENTITY();

    SELECT 'success'                   AS status,
           'Room created successfully.' AS message,
           @new_room_id                AS room_id,
           @slug                       AS room_code,
           LTRIM(RTRIM(@room_name))    AS room_name,
           GETDATE()                   AS created_date;
END
GO


-- ════════════════════════════════════════════════════════════
-- 2.  sp_get_my_room
--     Returns the calling user's room details.
--     A separate call to sp_get_sub_rooms fetches sub-rooms.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_get_my_room', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_get_my_room;
GO

CREATE PROCEDURE vcadmin.sp_get_my_room
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  user_id   = @user_id
          AND  is_active = 1
    )
    BEGIN
        SELECT 'not_found'                       AS status,
               'No room found for this user.' AS message,
               NULL AS room_id, NULL AS room_name,
               NULL AS room_code, NULL AS created_date;
        RETURN;
    END

    SELECT 'success'                      AS status,
           'Room fetched successfully.'   AS message,
           room_id,
           room_name,
           room_code,
           created_date
    FROM   vcadmin.rooms
    WHERE  user_id   = @user_id
      AND  is_active = 1;
END
GO


-- ════════════════════════════════════════════════════════════
-- 3.  sp_get_sub_rooms
--     Returns all sub-rooms (active + ended) for a main room,
--     ordered newest first.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_get_sub_rooms', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_get_sub_rooms;
GO

CREATE PROCEDURE vcadmin.sp_get_sub_rooms
    @room_id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT sub_room_id,
           room_id,
           sub_name,
           room_code,
           status,
           created_date,
           ended_date
    FROM   vcadmin.sub_rooms
    WHERE  room_id = @room_id
    ORDER  BY created_date DESC;
END
GO


-- ════════════════════════════════════════════════════════════
-- 4.  sp_validate_room_code
--     Used on the join screen to check if a code is valid.
--     Checks main rooms first, then sub-rooms.
--     Returns: room type (main/sub), name, host user.
--     Statuses: success | room_ended | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_validate_room_code', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_validate_room_code;
GO

CREATE PROCEDURE vcadmin.sp_validate_room_code
    @room_code VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Check main rooms ──────────────────────────────────────────────────
    DECLARE @room_id   INT;
    DECLARE @room_name VARCHAR(200);
    DECLARE @host_id   INT;

    SELECT @room_id   = room_id,
           @room_name = room_name,
           @host_id   = user_id
    FROM   vcadmin.rooms
    WHERE  room_code  = @room_code
      AND  is_active  = 1;

    IF @room_id IS NOT NULL
    BEGIN
        SELECT 'success'           AS status,
               'Room is valid.'    AS message,
               @room_id            AS room_id,
               NULL                AS sub_room_id,
               @room_name          AS room_name,
               'main'              AS room_type,
               @host_id            AS host_user_id;
        RETURN;
    END

    -- ── Check sub-rooms ───────────────────────────────────────────────────
    DECLARE @sub_room_id    INT;
    DECLARE @sub_name       VARCHAR(200);
    DECLARE @parent_room_id INT;
    DECLARE @sub_status     VARCHAR(20);
    DECLARE @sub_host_id    INT;

    SELECT @sub_room_id    = sr.sub_room_id,
           @sub_name       = sr.sub_name,
           @parent_room_id = sr.room_id,
           @sub_status     = sr.status,
           @sub_host_id    = r.user_id
    FROM   vcadmin.sub_rooms  sr
    JOIN   vcadmin.rooms      r  ON r.room_id = sr.room_id
    WHERE  sr.room_code = @room_code;

    IF @sub_room_id IS NOT NULL
    BEGIN
        -- Sub-room found but already ended
        IF @sub_status = 'ended'
        BEGIN
            SELECT 'room_ended'                                                       AS status,
                   'This sub-room session has ended and is no longer active.' AS message,
                   @parent_room_id AS room_id,
                   @sub_room_id    AS sub_room_id,
                   @sub_name       AS room_name,
                   'sub'           AS room_type,
                   @sub_host_id    AS host_user_id;
            RETURN;
        END

        -- Active sub-room
        SELECT 'success'             AS status,
               'Sub-room is valid.'  AS message,
               @parent_room_id       AS room_id,
               @sub_room_id          AS sub_room_id,
               @sub_name             AS room_name,
               'sub'                 AS room_type,
               @sub_host_id          AS host_user_id;
        RETURN;
    END

    -- ── Nothing found ─────────────────────────────────────────────────────
    SELECT 'not_found'                                                   AS status,
           'Invalid room code. No active room found with this code.' AS message,
           NULL AS room_id, NULL AS sub_room_id,
           NULL AS room_name, NULL AS room_type,
           NULL AS host_user_id;
END
GO


-- ════════════════════════════════════════════════════════════
-- 5.  sp_check_room_name
--     Live availability check while user types.
--     Statuses: available | name_taken
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_check_room_name', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_check_room_name;
GO

CREATE PROCEDURE vcadmin.sp_check_room_name
    @room_name VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  LOWER(room_name) = LOWER(LTRIM(RTRIM(@room_name)))
          AND  is_active = 1
    )
    BEGIN
        SELECT 'name_taken'                        AS status,
               'This room name is already taken.'  AS message;
        RETURN;
    END

    SELECT 'available'               AS status,
           'Room name is available.' AS message;
END
GO


-- ════════════════════════════════════════════════════════════
-- 6.  sp_delete_room
--     Soft-deletes a room (is_active = 0) and ends all its
--     active sub-rooms.  Verifies ownership before acting.
--     Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_delete_room', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_delete_room;
GO

CREATE PROCEDURE vcadmin.sp_delete_room
    @room_id INT,
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Ownership check ───────────────────────────────────────────────────
    IF NOT EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  room_id   = @room_id
          AND  user_id   = @user_id
          AND  is_active = 1
    )
    BEGIN
        SELECT 'not_found'                                                            AS status,
               'Room not found or you do not have permission to delete it.' AS message;
        RETURN;
    END

    -- ── End all active sub-rooms first ────────────────────────────────────
    UPDATE vcadmin.sub_rooms
    SET    status     = 'ended',
           ended_date = GETDATE()
    WHERE  room_id = @room_id
      AND  status  = 'active';

    -- ── Soft-delete the main room ─────────────────────────────────────────
    UPDATE vcadmin.rooms
    SET    is_active    = 0,
           updated_date = GETDATE()
    WHERE  room_id = @room_id;

    SELECT 'success'                    AS status,
           'Room deleted successfully.' AS message;
END
GO


-- ════════════════════════════════════════════════════════════
-- 7.  sp_create_sub_room
--     Creates a new ephemeral sub-room under the caller's
--     main room.  Verifies ownership.
--     Generates a random  sub-{8hex}  code guaranteed unique.
--     Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_create_sub_room', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_create_sub_room;
GO

CREATE PROCEDURE vcadmin.sp_create_sub_room
    @room_id  INT,
    @user_id  INT,
    @sub_name VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Ownership check ───────────────────────────────────────────────────
    IF NOT EXISTS (
        SELECT 1 FROM vcadmin.rooms
        WHERE  room_id   = @room_id
          AND  user_id   = @user_id
          AND  is_active = 1
    )
    BEGIN
        SELECT 'not_found'                                                    AS status,
               'Room not found or you do not have permission.' AS message,
               NULL AS sub_room_id, NULL AS room_code,
               NULL AS sub_name,    NULL AS created_date;
        RETURN;
    END

    -- ── Generate unique sub-room code:  sub-{8 random hex chars} ─────────
    DECLARE @code    VARCHAR(100);
    DECLARE @attempt INT = 0;

    SET @code = 'sub-' + LOWER(LEFT(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', ''), 8));

    WHILE EXISTS (SELECT 1 FROM vcadmin.sub_rooms WHERE room_code = @code)
    BEGIN
        SET @attempt = @attempt + 1;
        SET @code    = 'sub-' + LOWER(LEFT(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', ''), 8));
        IF @attempt > 10 BREAK;   -- safety guard (astronomically unlikely to reach)
    END

    -- ── Insert sub-room ───────────────────────────────────────────────────
    DECLARE @new_sub_id INT;

    INSERT INTO vcadmin.sub_rooms (room_id, sub_name, room_code, status, created_date)
    VALUES (@room_id, LTRIM(RTRIM(@sub_name)), @code, 'active', GETDATE());

    SET @new_sub_id = SCOPE_IDENTITY();

    SELECT 'success'                        AS status,
           'Sub-room created successfully.' AS message,
           @new_sub_id                      AS sub_room_id,
           @room_id                         AS room_id,
           LTRIM(RTRIM(@sub_name))          AS sub_name,
           @code                            AS room_code,
           'active'                         AS sub_status,
           GETDATE()                        AS created_date;
END
GO


-- ════════════════════════════════════════════════════════════
-- 8.  sp_end_sub_room
--     Marks a sub-room as ended.  Verifies the caller owns
--     the parent room.
--     Statuses: success | already_ended | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_end_sub_room', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_end_sub_room;
GO

CREATE PROCEDURE vcadmin.sp_end_sub_room
    @sub_room_id INT,
    @user_id     INT
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Check sub-room exists and caller owns the parent room ─────────────
    IF NOT EXISTS (
        SELECT 1
        FROM   vcadmin.sub_rooms sr
        JOIN   vcadmin.rooms     r  ON r.room_id = sr.room_id
        WHERE  sr.sub_room_id = @sub_room_id
          AND  r.user_id      = @user_id
          AND  r.is_active    = 1
    )
    BEGIN
        SELECT 'not_found'                                                    AS status,
               'Sub-room not found or you do not have permission.' AS message;
        RETURN;
    END

    -- ── Guard: already ended ──────────────────────────────────────────────
    IF EXISTS (
        SELECT 1 FROM vcadmin.sub_rooms
        WHERE  sub_room_id = @sub_room_id
          AND  status      = 'ended'
    )
    BEGIN
        SELECT 'already_ended'                         AS status,
               'This sub-room has already been ended.' AS message;
        RETURN;
    END

    -- ── End it ────────────────────────────────────────────────────────────
    UPDATE vcadmin.sub_rooms
    SET    status     = 'ended',
           ended_date = GETDATE()
    WHERE  sub_room_id = @sub_room_id;

    SELECT 'success'                       AS status,
           'Sub-room ended successfully.'  AS message;
END
GO


PRINT 'Rooms_Setup.sql applied successfully.';
