-- ============================================================
-- VMeet  –  Core Stored Procedures  (authoritative version)
-- Run this on SQL Server database (vmeet).
-- Safe to re-run: every SP is dropped and recreated.
--
-- Design rules
-- ────────────
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
-- 1.  sp_login_user
--     Unified login: detects email vs. phone automatically.
--     All credential/account checks happen inside the SP.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_login_user', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_login_user;
GO

CREATE PROCEDURE vcadmin.sp_login_user
    @user_name VARCHAR(255),    -- email (contains @)  OR  mobile number
    @password  VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id       INT;
    DECLARE @db_user_name  VARCHAR(100);
    DECLARE @user_fullname VARCHAR(255);
    DECLARE @user_mail_id  VARCHAR(255);
    DECLARE @is_active     BIT;
    DECLARE @stored_pass   VARBINARY(MAX);

    -- ── Auto-detect: email contains '@', otherwise treat as mobile number ─
    IF CHARINDEX('@', @user_name) > 0
        -- Email login
        SELECT @user_id       = u.user_id,
               @db_user_name  = u.user_name,
               @user_fullname = u.user_fullname,
               @user_mail_id  = u.user_mail_id,
               @is_active     = u.is_active,
               @stored_pass   = p.password
        FROM   [vcadmin].[usersm] u
        LEFT   JOIN [vcadmin].[passm] p
               ON  p.user_id  = u.user_id
               AND p.is_active = 1
        WHERE  u.user_mail_id = @user_name;
    ELSE
        -- Mobile number login
        SELECT @user_id       = u.user_id,
               @db_user_name  = u.user_name,
               @user_fullname = u.user_fullname,
               @user_mail_id  = u.user_mail_id,
               @is_active     = u.is_active,
               @stored_pass   = p.password
        FROM   [vcadmin].[usersm] u
        LEFT   JOIN [vcadmin].[passm] p
               ON  p.user_id  = u.user_id
               AND p.is_active = 1
        WHERE  u.contact_details = @user_name;

    -- ── User not found ────────────────────────────────────────────────────
    IF @user_id IS NULL
    BEGIN
        SELECT 'invalid_credentials'           AS status,
               'Invalid email/phone or password.' AS message,
               NULL AS user_id, NULL AS user_name,
               NULL AS user_fullname, NULL AS user_mail_id;
        RETURN;
    END

    -- ── Account disabled ──────────────────────────────────────────────────
    IF @is_active = 0
    BEGIN
        SELECT 'account_inactive'                                        AS status,
               'Your account has been deactivated. Please contact support.' AS message,
               NULL AS user_id, NULL AS user_name,
               NULL AS user_fullname, NULL AS user_mail_id;
        RETURN;
    END

    -- ── Wrong password ────────────────────────────────────────────────────
    IF @stored_pass IS NULL OR @stored_pass != @password
    BEGIN
        SELECT 'invalid_credentials'           AS status,
               'Invalid email/phone or password.' AS message,
               NULL AS user_id, NULL AS user_name,
               NULL AS user_fullname, NULL AS user_mail_id;
        RETURN;
    END

    -- ── Success: stamp last-login and return user info ────────────────────
    UPDATE [vcadmin].[usersm]
    SET    is_logged_in = 1,
           updated_date  = GETDATE()
    WHERE  user_id = @user_id;

    SELECT 'success'          AS status,
           'Login successful.' AS message,
           @user_id           AS user_id,
           @db_user_name      AS user_name,
           @user_fullname     AS user_fullname,
           @user_mail_id      AS user_mail_id;
END
GO


-- ════════════════════════════════════════════════════════════
-- 2.  sp_verify_user
--     Finds an account by email or phone.
--     Returns a status row so C# never needs to inspect
--     row-count or column values for flow control.
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_verify_user', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_verify_user;
GO

CREATE PROCEDURE vcadmin.sp_verify_user
    @identifier VARCHAR(255)    -- email or phone
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id         INT;
    DECLARE @user_fullname   VARCHAR(255);
    DECLARE @user_mail_id    VARCHAR(255);
    DECLARE @contact_details VARCHAR(20);

    SELECT @user_id         = user_id,
           @user_fullname   = user_fullname,
           @user_mail_id    = user_mail_id,
           @contact_details = contact_details
    FROM   [vcadmin].[usersm]
    WHERE  (user_mail_id    = @identifier
         OR contact_details = @identifier)
      AND  is_active = 1;

    -- ── Not found ─────────────────────────────────────────────────────────
    IF @user_id IS NULL
    BEGIN
        SELECT 'not_found'                              AS status,
               'No account found with that identifier.' AS message,
               NULL AS user_id, NULL AS user_fullname,
               NULL AS user_mail_id, NULL AS contact_details;
        RETURN;
    END

    -- ── Found ─────────────────────────────────────────────────────────────
    SELECT 'success'       AS status,
           'Account found.' AS message,
           @user_id         AS user_id,
           @user_fullname   AS user_fullname,
           @user_mail_id    AS user_mail_id,
           @contact_details AS contact_details;
END
GO


-- ════════════════════════════════════════════════════════════
-- 3.  sp_reset_password_by_id
--     Direct password change by user_id (profile/settings flow).
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_reset_password_by_id', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_reset_password_by_id;
GO

CREATE PROCEDURE vcadmin.sp_reset_password_by_id
    @user_id      INT,
    @new_password VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM [vcadmin].[passm]
        WHERE  user_id   = @user_id
          AND  is_active = 1
    )
    BEGIN
        SELECT 'not_found'                            AS status,
               'User not found or account inactive.'  AS message;
        RETURN;
    END

    UPDATE [vcadmin].[passm]
    SET    password     = @new_password,
           updated_by   = @user_id,
           updated_date = GETDATE()
    WHERE  user_id   = @user_id
      AND  is_active = 1;

    SELECT 'success'                     AS status,
           'Password updated successfully.' AS message;
END
GO


-- ════════════════════════════════════════════════════════════
-- 4.  sp_get_user_meetings
--     Lists all meetings hosted by a user, newest first.
--     Dates formatted as  dd-MM-yyyy HH:mm  (app standard).
-- ════════════════════════════════════════════════════════════
IF OBJECT_ID('vcadmin.sp_get_user_meetings', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_get_user_meetings;
GO

CREATE PROCEDURE vcadmin.sp_get_user_meetings
    @user_id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        meeting_id,
        host_user_id,
        title,
        description,
        meeting_code,
        CONVERT(VARCHAR(10), start_time, 103) + ' '
            + CONVERT(VARCHAR(5), start_time, 108) AS start_time,
        CONVERT(VARCHAR(10), end_time, 103)   + ' '
            + CONVERT(VARCHAR(5), end_time,   108) AS end_time
    FROM   [vcadmin].[meetings]
    WHERE  host_user_id = @user_id
    ORDER  BY start_time DESC;
END
GO


PRINT 'Core_SPs.sql applied successfully.';
