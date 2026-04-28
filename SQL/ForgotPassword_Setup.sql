-- ============================================================
-- VMeet - Forgot Password Stored Procedures
-- Uses existing tables: vcadmin.passm, vcadmin.password_reset
-- Token generation and expiry handled entirely in SQL (no C# datetime)
-- Run this script on your SQL Server database (vmeet)
-- ============================================================

USE vmeet;
GO

-- ---------------------------------------------------------------
-- SP: vcadmin.sp_forgot_password
--
-- 1. Looks up the user by email
-- 2. Generates a unique reset token using NEWID() inside SQL
-- 3. Sets expiry to DATEADD(MINUTE, 15, GETDATE()) — all in SQL time
-- 4. Invalidates previous unused tokens for the user
-- 5. Inserts new token into vcadmin.password_reset
--
-- Input : @user_mail_id only — no token or datetime from C#
-- Returns: status, message, user_id, user_fullname, reset_token
--
-- ⚠️  Verify your user table name below (currently vcadmin.userm)
-- ---------------------------------------------------------------
IF OBJECT_ID('vcadmin.sp_forgot_password', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_forgot_password;
GO

CREATE PROCEDURE vcadmin.sp_forgot_password
    @user_mail_id VARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id       INT;
    DECLARE @user_fullname VARCHAR(255);
    DECLARE @reset_token   VARCHAR(256);
    DECLARE @expiry_date   DATETIME;

    -- ⚠️ Verify table name: [vcadmin].[userm]
    SELECT @user_id       = user_id,
           @user_fullname = user_fullname
    FROM   [vcadmin].[userm]
    WHERE  user_mail_id = @user_mail_id
      AND  is_active    = 1;

    -- Email not found / account not active
    IF @user_id IS NULL
    BEGIN
        SELECT 'not_found' AS status,
               'No account found with this email address.' AS message,
               NULL AS user_id,
               NULL AS user_fullname,
               NULL AS reset_token;
        RETURN;
    END

    -- Generate token and expiry entirely in SQL — no timezone mismatch
    SET @reset_token = LOWER(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', ''));
    SET @expiry_date = DATEADD(MINUTE, 15, GETDATE());

    -- Invalidate any previous unused tokens for this user
    UPDATE [vcadmin].[password_reset]
    SET    is_used      = 1,
           is_active    = 0,
           updated_by   = @user_id,
           updated_date = GETDATE()
    WHERE  user_id   = @user_id
      AND  is_used   = 0
      AND  is_active = 1;

    -- Insert the new reset token
    INSERT INTO [vcadmin].[password_reset]
        (user_id, reset_token, expiry_date, is_used, is_active, inserted_by, inserted_date, updated_by, updated_date)
    VALUES
        (@user_id, @reset_token, @expiry_date, 0, 1, @user_id, GETDATE(), @user_id, GETDATE());

    SELECT 'success'          AS status,
           'Reset link sent.' AS message,
           @user_id           AS user_id,
           @user_fullname     AS user_fullname,
           @reset_token       AS reset_token;
END
GO

-- ---------------------------------------------------------------
-- SP: vcadmin.sp_reset_password
--
-- 1. Finds and validates the token in vcadmin.password_reset
-- 2. Expiry checked with GETDATE() — fully in SQL time
-- 3. Updates password in vcadmin.passm
-- 4. Marks token as used
--
-- Returns: status, message
-- ---------------------------------------------------------------
IF OBJECT_ID('vcadmin.sp_reset_password', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_reset_password;
GO

CREATE PROCEDURE vcadmin.sp_reset_password
    @token        VARCHAR(256),
    @new_password VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id     INT;
    DECLARE @is_used     BIT;
    DECLARE @expiry_date DATETIME;
    DECLARE @is_active   BIT;

    SELECT @user_id     = user_id,
           @is_used     = is_used,
           @expiry_date = expiry_date,
           @is_active   = is_active
    FROM   [vcadmin].[password_reset]
    WHERE  reset_token = @token;

    -- Token does not exist
    IF @user_id IS NULL
    BEGIN
        SELECT 'invalid' AS status,
               'Invalid or expired reset link.' AS message;
        RETURN;
    END

    -- Token already used
    IF @is_used = 1 OR @is_active = 0
    BEGIN
        SELECT 'used' AS status,
               'This reset link has already been used.' AS message;
        RETURN;
    END

    -- Token expired — GETDATE() is the same clock that set expiry_date
    IF @expiry_date < GETDATE()
    BEGIN
        SELECT 'expired' AS status,
               'This reset link has expired. Please request a new one.' AS message;
        RETURN;
    END

    -- Update password in vcadmin.passm
    UPDATE [vcadmin].[passm]
    SET    password     = @new_password,
           updated_by   = @user_id,
           updated_date = GETDATE()
    WHERE  user_id   = @user_id
      AND  is_active = 1;

    -- Mark token as used
    UPDATE [vcadmin].[password_reset]
    SET    is_used      = 1,
           is_active    = 0,
           updated_by   = @user_id,
           updated_date = GETDATE()
    WHERE  reset_token = @token;

    SELECT 'success' AS status,
           'Password updated successfully.' AS message;
END
GO

PRINT 'sp_forgot_password and sp_reset_password updated successfully.';
