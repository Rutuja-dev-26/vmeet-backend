-- ============================================================
-- VMeet  –  Session / Recording / Snapshot Stored Procedures
-- Run this on SQL Server database (vmeet).
-- Safe to re-run: SPs are dropped and recreated on every run.
--
-- Design rules (same as Rooms_Setup.sql / Core_SPs.sql)
-- ────────────────────────────────────────────────────────────
-- • Every write SP always returns exactly ONE result-set row.
-- • That row ALWAYS contains  status  and  message  columns.
-- • List SPs return data rows directly (no status row).
-- • All business rules live here — not in C#.
-- ============================================================

USE vmeet;
GO


-- ════════════════════════════════════════════════════════════
-- SESSION — sp_join_session
-- Called when any participant (host or guest) enters a room.
-- Finds or creates the active dbo.Rooms session for the code,
-- then inserts a dbo.Participants row for the participant.
-- Statuses: success
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_join_session', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_join_session;
GO

CREATE PROCEDURE dbo.sp_join_session
    @room_code        NVARCHAR(32),
    @user_id          NVARCHAR(100),
    @display_name     NVARCHAR(150),
    @role             NVARCHAR(20),       -- 'host' or 'guest'
    @client_id        INT,
    @max_participants INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Find or create an active (non-ended) session ──────────────────────
    DECLARE @room_id INT;

    SELECT TOP 1 @room_id = id
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
      AND  status   != 'ended'
    ORDER  BY created_at DESC;

    IF @room_id IS NULL
    BEGIN
        -- No active session — create a new one
        INSERT INTO dbo.Rooms (client_id, room_code, status, max_participants)
        VALUES (@client_id, @room_code, 'active', @max_participants);

        SET @room_id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        -- Move from pending → active on first join
        UPDATE dbo.Rooms
        SET    status = 'active'
        WHERE  id     = @room_id
          AND  status = 'pending';
    END

    -- ── Record participant ────────────────────────────────────────────────
    DECLARE @participant_id INT;

    INSERT INTO dbo.Participants (room_id, user_id, display_name, role)
    VALUES (@room_id, @user_id, @display_name, @role);

    SET @participant_id = SCOPE_IDENTITY();

    SELECT 'success'                     AS status,
           'Participant joined session.' AS message,
           @room_id                      AS room_id,
           @participant_id               AS participant_id;
END
GO


-- ════════════════════════════════════════════════════════════
-- SESSION — sp_leave_session
-- Marks a participant's left_at timestamp.
-- Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_leave_session', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_leave_session;
GO

CREATE PROCEDURE dbo.sp_leave_session
    @room_code NVARCHAR(32),
    @user_id   NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @room_id INT;

    SELECT TOP 1 @room_id = id
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
      AND  status   != 'ended'
    ORDER  BY created_at DESC;

    IF @room_id IS NULL
    BEGIN
        SELECT 'not_found'          AS status,
               'Session not found.' AS message;
        RETURN;
    END

    UPDATE dbo.Participants
    SET    left_at = SYSUTCDATETIME()
    WHERE  room_id  = @room_id
      AND  user_id  = @user_id
      AND  left_at IS NULL;

    SELECT 'success'                    AS status,
           'Participant left session.'  AS message;
END
GO


-- ════════════════════════════════════════════════════════════
-- SESSION — sp_end_session
-- Host ends the call: marks room as ended and stamps left_at
-- for any participants still in the room.
-- Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_end_session', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_end_session;
GO

CREATE PROCEDURE dbo.sp_end_session
    @room_code NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @room_id INT;

    SELECT TOP 1 @room_id = id
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
      AND  status   != 'ended'
    ORDER  BY created_at DESC;

    IF @room_id IS NULL
    BEGIN
        SELECT 'not_found'          AS status,
               'Session not found.' AS message,
               NULL AS room_id;
        RETURN;
    END

    UPDATE dbo.Rooms
    SET    status   = 'ended',
           ended_at = SYSUTCDATETIME()
    WHERE  id = @room_id;

    UPDATE dbo.Participants
    SET    left_at = SYSUTCDATETIME()
    WHERE  room_id  = @room_id
      AND  left_at IS NULL;

    SELECT 'success'                AS status,
           'Session ended.'         AS message,
           @room_id                 AS room_id;
END
GO


-- ════════════════════════════════════════════════════════════
-- SESSION — sp_get_session_room
-- Returns all room sessions for a given room_code (newest first).
-- Used to get current and historical session IDs before calling
-- sp_get_session_participants.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_get_session_room', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_get_session_room;
GO

CREATE PROCEDURE dbo.sp_get_session_room
    @room_code NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT id          AS room_id,
           room_code,
           status,
           max_participants,
           created_at,
           ended_at
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
    ORDER  BY created_at DESC;
END
GO


-- ════════════════════════════════════════════════════════════
-- SESSION — sp_get_session_participants
-- Returns all participants for a given room session.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_get_session_participants', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_get_session_participants;
GO

CREATE PROCEDURE dbo.sp_get_session_participants
    @room_id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT id           AS participant_id,
           room_id,
           user_id,
           display_name,
           role,
           joined_at,
           left_at
    FROM   dbo.Participants
    WHERE  room_id = @room_id
    ORDER  BY joined_at ASC;
END
GO


-- ════════════════════════════════════════════════════════════
-- RECORDING — sp_start_recording
-- Creates a RecordingLedger entry when host starts recording.
-- Statuses: success | already_exists | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_start_recording', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_start_recording;
GO

CREATE PROCEDURE dbo.sp_start_recording
    @session_id NVARCHAR(100),
    @room_code  NVARCHAR(32),
    @client_id  INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Guard: no duplicate session ID
    IF EXISTS (SELECT 1 FROM dbo.RecordingLedger WHERE session_id = @session_id)
    BEGIN
        SELECT 'already_exists'                                          AS status,
               'A recording with this session ID already exists.'       AS message,
               NULL AS recording_id, NULL AS room_id;
        RETURN;
    END

    -- Find the active room session
    DECLARE @room_id INT;

    SELECT TOP 1 @room_id = id
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
      AND  status   != 'ended'
    ORDER  BY created_at DESC;

    IF @room_id IS NULL
    BEGIN
        SELECT 'not_found'                                               AS status,
               'No active room session found for this room code.'        AS message,
               NULL AS recording_id, NULL AS room_id;
        RETURN;
    END

    DECLARE @recording_id INT;

    INSERT INTO dbo.RecordingLedger (room_id, session_id, client_id, status)
    VALUES (@room_id, @session_id, @client_id, 'recording');

    SET @recording_id = SCOPE_IDENTITY();

    SELECT 'success'             AS status,
           'Recording started.'  AS message,
           @recording_id         AS recording_id,
           @room_id              AS room_id,
           @session_id           AS session_id;
END
GO


-- ════════════════════════════════════════════════════════════
-- RECORDING — sp_finalize_recording
-- Updates the ledger entry to 'ready' once the file is processed.
-- Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_finalize_recording', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_finalize_recording;
GO

CREATE PROCEDURE dbo.sp_finalize_recording
    @session_id       NVARCHAR(100),
    @file_path        NVARCHAR(500),
    @chunk_count      INT,
    @duration_seconds INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.RecordingLedger WHERE session_id = @session_id)
    BEGIN
        SELECT 'not_found'                    AS status,
               'Recording session not found.' AS message;
        RETURN;
    END

    UPDATE dbo.RecordingLedger
    SET    status           = 'ready',
           file_path        = @file_path,
           chunk_count      = @chunk_count,
           duration_seconds = @duration_seconds,
           completed_at     = SYSUTCDATETIME()
    WHERE  session_id = @session_id;

    SELECT 'success'              AS status,
           'Recording finalized.' AS message;
END
GO


-- ════════════════════════════════════════════════════════════
-- RECORDING — sp_get_recordings
-- Returns all recording entries for a room (newest first).
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_get_recordings', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_get_recordings;
GO

CREATE PROCEDURE dbo.sp_get_recordings
    @room_code NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT rl.id              AS recording_id,
           rl.room_id,
           rl.session_id,
           rl.status,
           rl.file_path,
           rl.chunk_count,
           rl.duration_seconds,
           rl.created_at,
           rl.completed_at
    FROM   dbo.RecordingLedger rl
    JOIN   dbo.Rooms           r  ON r.id = rl.room_id
    WHERE  r.room_code = @room_code
    ORDER  BY rl.created_at DESC;
END
GO


-- ════════════════════════════════════════════════════════════
-- SNAPSHOT — sp_create_snapshot
-- Saves a snapshot metadata entry after the image file has
-- already been stored on disk by the Node.js server.
-- Statuses: success | not_found
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_create_snapshot', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_create_snapshot;
GO

CREATE PROCEDURE dbo.sp_create_snapshot
    @room_code   NVARCHAR(32),
    @captured_by NVARCHAR(100),
    @file_path   NVARCHAR(500),
    @metadata    NVARCHAR(MAX) = NULL,
    @category_id INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Find the most recent room session for this code (active or ended)
    DECLARE @room_id INT;

    SELECT TOP 1 @room_id = id
    FROM   dbo.Rooms
    WHERE  room_code = @room_code
    ORDER  BY created_at DESC;

    IF @room_id IS NULL
    BEGIN
        SELECT 'not_found'                     AS status,
               'Room not found for this code.' AS message,
               NULL AS snapshot_id, NULL AS created_at;
        RETURN;
    END

    DECLARE @snapshot_id INT;

    INSERT INTO dbo.Snapshots (room_id, category_id, captured_by, file_path, metadata)
    VALUES (@room_id, @category_id, @captured_by, @file_path, @metadata);

    SET @snapshot_id = SCOPE_IDENTITY();

    SELECT 'success'          AS status,
           'Snapshot saved.'  AS message,
           @snapshot_id       AS snapshot_id,
           @room_id           AS room_id,
           SYSUTCDATETIME()   AS created_at;
END
GO


-- ════════════════════════════════════════════════════════════
-- SNAPSHOT — sp_get_snapshots
-- Returns all snapshot entries for a room (newest first).
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.sp_get_snapshots', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_get_snapshots;
GO

CREATE PROCEDURE dbo.sp_get_snapshots
    @room_code NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.id          AS snapshot_id,
           s.room_id,
           s.category_id,
           s.captured_by,
           s.file_path,
           s.metadata,
           s.created_at
    FROM   dbo.Snapshots s
    JOIN   dbo.Rooms     r  ON r.id = s.room_id
    WHERE  r.room_code = @room_code
    ORDER  BY s.created_at DESC;
END
GO


PRINT 'Sessions_Snapshots_Recordings_Setup.sql applied successfully.';
