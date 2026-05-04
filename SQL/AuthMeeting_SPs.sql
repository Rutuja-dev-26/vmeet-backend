-- ============================================================
-- VMeet  –  Auth & Meeting Helper Stored Procedures
-- Run this on your SQL Server database (vmeet)
-- Safe to re-run: every SP is dropped and recreated.
-- ============================================================

USE vmeet;
GO

-- ─────────────────────────────────────────────────────────────
-- SP: vcadmin.sp_login_by_email
-- Validates credentials by email address.
-- Returns one row on success, zero rows on failure.
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('vcadmin.sp_login_by_email', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_login_by_email;
GO

CREATE PROCEDURE vcadmin.sp_login_by_email
    @user_mail_id VARCHAR(255),
    @password     VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.user_id,
           u.user_name,
           u.user_fullname,
           u.user_mail_id
    FROM   [vcadmin].[usersm] u
    INNER  JOIN [vcadmin].[passm] p
           ON  p.user_id  = u.user_id
           AND p.is_active = 1
    WHERE  u.user_mail_id = @user_mail_id
      AND  u.is_active    = 1
      AND  p.password     = @password;
END
GO

-- ─────────────────────────────────────────────────────────────
-- SP: vcadmin.sp_login_by_phone
-- Validates credentials by mobile number (contact_details).
-- Returns one row on success, zero rows on failure.
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('vcadmin.sp_login_by_phone', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_login_by_phone;
GO

CREATE PROCEDURE vcadmin.sp_login_by_phone
    @contact_details VARCHAR(20),
    @password        VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT u.user_id,
           u.user_name,
           u.user_fullname,
           u.user_mail_id
    FROM   [vcadmin].[usersm] u
    INNER  JOIN [vcadmin].[passm] p
           ON  p.user_id  = u.user_id
           AND p.is_active = 1
    WHERE  u.contact_details = @contact_details
      AND  u.is_active       = 1
      AND  p.password        = @password;
END
GO

-- ─────────────────────────────────────────────────────────────
-- SP: vcadmin.sp_verify_user
-- Finds an active account by email or mobile number.
-- Used by the forgot-password flow to confirm the account exists.
-- Returns one row on success, zero rows if not found.
-- ─────────────────────────────────────────────────────────────
IF OBJECT_ID('vcadmin.sp_verify_user', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_verify_user;
GO

CREATE PROCEDURE vcadmin.sp_verify_user
    @identifier VARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
           user_id,
           user_name,
           user_fullname,
           user_mail_id,
           contact_details
    FROM   [vcadmin].[usersm]
    WHERE  (user_mail_id    = @identifier
         OR contact_details = @identifier)
      AND  is_active = 1;
END
GO

-- ─────────────────────────────────────────────────────────────
-- SP: vcadmin.sp_reset_password_by_id
-- Directly updates a user's password given their user_id.
-- Used by the profile / settings "change password" flow.
-- Returns: status ('success' | 'not_found'), message.
-- ─────────────────────────────────────────────────────────────
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
        SELECT 'not_found' AS status,
               'User not found or account inactive.' AS message;
        RETURN;
    END

    UPDATE [vcadmin].[passm]
    SET    password     = @new_password,
           updated_by   = @user_id,
           updated_date = GETDATE()
    WHERE  user_id   = @user_id
      AND  is_active = 1;

    SELECT 'success' AS status,
           'Password updated successfully.' AS message;
END
GO

-- ─────────────────────────────────────────────────────────────
-- SP: vcadmin.sp_get_user_meetings
-- Lists all meetings hosted by the given user, newest first.
-- Dates returned in dd-MM-yyyy HH:mm format to match the app.
-- ─────────────────────────────────────────────────────────────
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
            + CONVERT(VARCHAR(5),  start_time, 108) AS start_time,
        CONVERT(VARCHAR(10), end_time,   103) + ' '
            + CONVERT(VARCHAR(5),  end_time,   108) AS end_time
    FROM   [vcadmin].[meetings]
    WHERE  host_user_id = @user_id
    ORDER  BY start_time DESC;
END
GO

PRINT 'AuthMeeting_SPs.sql applied successfully.';
