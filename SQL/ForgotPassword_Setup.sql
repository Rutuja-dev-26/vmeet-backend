-- ============================================================
-- VMeet - Forgot Password Setup
-- Run this script once on your SQL Server database (vmeet)
-- ============================================================

USE vmeet;
GO

-- ---------------------------------------------------------------
-- 1. Table: password_reset_tokens
-- ---------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'password_reset_tokens'
)
BEGIN
    CREATE TABLE password_reset_tokens (
        id          INT IDENTITY(1,1) PRIMARY KEY,
        user_id     INT          NOT NULL,
        token       VARCHAR(256) NOT NULL UNIQUE,
        expires_at  DATETIME     NOT NULL,
        is_used     BIT          NOT NULL DEFAULT 0,
        created_at  DATETIME     NOT NULL DEFAULT GETDATE()
    );
    PRINT 'Table password_reset_tokens created.';
END
ELSE
    PRINT 'Table password_reset_tokens already exists — skipped.';
GO

-- ---------------------------------------------------------------
-- 2. SP: sp_forgot_password
--    Verifies the email is registered, then saves the reset token.
--    Returns: status, message, user_id, user_fullname
-- ---------------------------------------------------------------
IF OBJECT_ID('dbo.sp_forgot_password', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_forgot_password;
GO

CREATE PROCEDURE dbo.sp_forgot_password
    @user_mail_id   VARCHAR(255),
    @token          VARCHAR(256),
    @expires_at     DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id       INT;
    DECLARE @user_fullname VARCHAR(255);

    -- Look up the user by email
    SELECT @user_id = user_id, @user_fullname = user_fullname
    FROM   users
    WHERE  user_mail_id = @user_mail_id;

    -- Email not registered
    IF @user_id IS NULL
    BEGIN
        SELECT 'not_found' AS status,
               'No account found with this email address.' AS message,
               NULL AS user_id,
               NULL AS user_fullname;
        RETURN;
    END

    -- Invalidate any previous unused tokens for this user
    UPDATE password_reset_tokens
    SET    is_used = 1
    WHERE  user_id = @user_id AND is_used = 0;

    -- Insert the new reset token
    INSERT INTO password_reset_tokens (user_id, token, expires_at)
    VALUES (@user_id, @token, @expires_at);

    SELECT 'success'        AS status,
           'Reset link sent.' AS message,
           @user_id         AS user_id,
           @user_fullname   AS user_fullname;
END
GO

-- ---------------------------------------------------------------
-- 3. SP: sp_reset_password
--    Validates the token and updates the user's password.
--    Returns: status, message
-- ---------------------------------------------------------------
IF OBJECT_ID('dbo.sp_reset_password', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_reset_password;
GO

CREATE PROCEDURE dbo.sp_reset_password
    @token        VARCHAR(256),
    @new_password VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @user_id    INT;
    DECLARE @is_used    BIT;
    DECLARE @expires_at DATETIME;

    -- Find the token record
    SELECT @user_id    = user_id,
           @is_used    = is_used,
           @expires_at = expires_at
    FROM   password_reset_tokens
    WHERE  token = @token;

    -- Token does not exist
    IF @user_id IS NULL
    BEGIN
        SELECT 'invalid' AS status,
               'Invalid or expired reset link.' AS message;
        RETURN;
    END

    -- Token already used
    IF @is_used = 1
    BEGIN
        SELECT 'used' AS status,
               'This reset link has already been used.' AS message;
        RETURN;
    END

    -- Token expired
    IF @expires_at < GETDATE()
    BEGIN
        SELECT 'expired' AS status,
               'This reset link has expired. Please request a new one.' AS message;
        RETURN;
    END

    -- Update password
    UPDATE users
    SET    password = @new_password
    WHERE  user_id  = @user_id;

    -- Mark token as used so it cannot be replayed
    UPDATE password_reset_tokens
    SET    is_used = 1
    WHERE  token   = @token;

    SELECT 'success' AS status,
           'Password updated successfully.' AS message;
END
GO

PRINT 'Forgot Password setup complete.';
