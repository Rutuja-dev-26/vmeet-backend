-- ============================================================
-- VMeet - Register User Setup
-- Run this ONCE on your SQL Server database (vmeet)
-- Safe to re-run: uses IF NOT EXISTS / ALTER checks
-- ============================================================

USE vmeet;
GO

-- ── Add contact_details column to usersm (if it doesn't exist yet) ──
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE  TABLE_SCHEMA = 'vcadmin'
      AND  TABLE_NAME   = 'usersm'
      AND  COLUMN_NAME  = 'contact_details'
)
BEGIN
    ALTER TABLE [vcadmin].[usersm]
        ADD contact_details VARCHAR(20) NULL;
    PRINT 'Column contact_details added to vcadmin.usersm';
END
ELSE
    PRINT 'Column contact_details already exists';
GO

-- ── Recreate sp_register_user ─────────────────────────────────
IF OBJECT_ID('vcadmin.sp_register_user', 'P') IS NOT NULL
    DROP PROCEDURE vcadmin.sp_register_user;
GO

CREATE PROCEDURE vcadmin.sp_register_user
    @user_fullname VARCHAR(255),
    @user_mail_id  VARCHAR(255),
    @contact_details   VARCHAR(20),
    @password      VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- ── Duplicate email check ─────────────────────────────────────
    IF EXISTS (
        SELECT 1 FROM [vcadmin].[usersm]
        WHERE  user_mail_id = @user_mail_id
          AND  is_active    = 1
    )
    BEGIN
        SELECT 'email_exists' AS status,
               'An account with this email address already exists.' AS message,
               NULL AS user_id;
        RETURN;
    END

    -- ── Duplicate phone check ─────────────────────────────────────
    IF @contact_details IS NOT NULL AND @contact_details <> '' AND EXISTS (
        SELECT 1 FROM [vcadmin].[usersm]
        WHERE  contact_details = @contact_details
          AND  is_active   = 1
    )
    BEGIN
        SELECT 'phone_exists' AS status,
               'An account with this mobile number already exists.' AS message,
               NULL AS user_id;
        RETURN;
    END

    -- ── Auto-generate a unique username from email prefix ─────────
    DECLARE @base_name  VARCHAR(100);
    DECLARE @user_name  VARCHAR(100);
    DECLARE @counter    INT = 0;

    -- Take the part of the email before '@', strip dots/special chars
    SET @base_name = LOWER(LEFT(@user_mail_id, CHARINDEX('@', @user_mail_id) - 1));
    SET @user_name = @base_name;

    -- Ensure uniqueness by appending a counter when needed
    WHILE EXISTS (
        SELECT 1 FROM [vcadmin].[usersm]
        WHERE  user_name = @user_name
    )
    BEGIN
        SET @counter   = @counter + 1;
        SET @user_name = @base_name + CAST(@counter AS VARCHAR(10));
    END

    -- ── Insert user ───────────────────────────────────────────────
    DECLARE @new_user_id INT;

    INSERT INTO [vcadmin].[usersm]
        (user_name, user_fullname, user_mail_id, contact_details,
         is_active, is_logged_in, inserted_date, updated_date)
    VALUES
        (@user_name, @user_fullname, @user_mail_id, @contact_details,
         1, 0, GETDATE(), GETDATE());

    SET @new_user_id = SCOPE_IDENTITY();

    -- ── Insert password into passm ────────────────────────────────
    INSERT INTO [vcadmin].[passm]
        (user_id, password, is_active, inserted_by, inserted_date, updated_by, updated_date)
    VALUES
        (@new_user_id, @password, 1, @new_user_id, GETDATE(), @new_user_id, GETDATE());

    SELECT 'success'              AS status,
           'Registration successful.' AS message,
           @new_user_id           AS user_id;
END
GO

PRINT 'sp_register_user updated successfully.';
