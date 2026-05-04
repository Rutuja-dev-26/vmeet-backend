-- ============================================================
-- VMeet - Forgot Password Setup
-- Run this on your SQL Server database (vmeet)
-- ============================================================

USE vmeet;
GO

-- ---------------------------------------------------------------
-- TABLE: vcadmin.password_reset
-- Stores short-lived (15 min) one-time reset tokens.
-- ---------------------------------------------------------------
IF OBJECT_ID('vcadmin.password_reset', 'U') IS NULL
BEGIN
    CREATE TABLE [vcadmin].[password_reset] (
        id             INT IDENTITY(1,1) PRIMARY KEY,
        user_id        INT           NOT NULL,
        reset_token    VARCHAR(256)  NOT NULL,
        expiry_date    DATETIME      NOT NULL,
        is_used        BIT           NOT NULL DEFAULT 0,
        is_active      BIT           NOT NULL DEFAULT 1,
        inserted_by    INT           NULL,
        inserted_date  DATETIME      NOT NULL DEFAULT GETDATE(),
        updated_by     INT           NULL,
        updated_date   DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE UNIQUE INDEX UX_password_reset_token ON [vcadmin].[password_reset] (reset_token);
    PRINT 'Table vcadmin.password_reset created.';
END
ELSE
    PRINT 'Table vcadmin.password_reset already exists.';
GO

-- ---------------------------------------------------------------
-- SP: vcadmin.sp_forgot_password
--
-- 1. Looks up the user by email in vcadmin.usersm
-- 2. Generates a unique reset token using NEWID()
-- 3. Expiry = DATEADD(MINUTE, 15, GETDATE())
-- 4. Invalidates previous unused tokens for the user
-- 5. Inserts new token into vcadmin.password_reset
--
-- Returns: status, message, user_id, user_fullname, reset_token
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

    SELECT @user_id       = user_id,
           @user_fullname = user_fullname
    FROM   [vcadmin].[usersm]
    WHERE  user_mail_id = @user_mail_id
      AND  is_active    = 1;

    -- Email not found or account not active
    IF @user_id IS NULL
    BEGIN
        SELECT 'not_found' AS status,
               'No account found with this email address.' AS message,
               NULL AS user_id,
               NULL AS user_fullname,
               NULL AS reset_token;
        RETURN;
    END

    -- Generate token and expiry entirely in SQL
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
-- 2. Expiry checked with GETDATE()
-- 3. Updates password in vcadmin.passm
-- 4. Marks token as used / inactive
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

    -- Token not found
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

    -- Token expired
    IF @expiry_date < GETDATE()
    BEGIN
        SELECT 'expired' AS status,
               'This reset link has expired. Please request a new one.' AS message;
        RETURN;
    END

    -- Update password
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

PRINT 'ForgotPassword_Setup completed successfully.';
