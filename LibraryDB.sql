-- =====================================================================
-- DATABASE INITIALIZATION SCRIPT FOR LIBRARY MANAGEMENT SYSTEM
-- Hệ thống Quản lý Thư viện - Library Management
-- Hướng dẫn: Mở SQL Server Management Studio (SSMS), chọn "Execute" (F5)
-- để tự động tạo Database, các bảng và dữ liệu mẫu thử nghiệm.
-- =====================================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'LibraryDB')
BEGIN
    CREATE DATABASE [LibraryDB];
END
GO

USE [LibraryDB];
GO

-- 1. BẢNG USERS (Tài khoản người dùng: Administrator / Librarian)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
BEGIN
    CREATE TABLE Users (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        Email NVARCHAR(150) NOT NULL,
        FullName NVARCHAR(150) NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        Role NVARCHAR(50) NOT NULL DEFAULT 'Librarian',
        CreatedAt DATETIME DEFAULT GETDATE(),
        CONSTRAINT UQ_Users_Email UNIQUE (Email),
        CONSTRAINT UQ_Users_Username UNIQUE (Username)
    );
END
GO

-- 2. BẢNG BOOKS (Danh mục sách trong thư viện)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Books')
BEGIN
    CREATE TABLE Books (
        BookId INT IDENTITY(1,1) PRIMARY KEY,
        Title NVARCHAR(255) NOT NULL,
        Author NVARCHAR(150) NOT NULL,
        ISBN NVARCHAR(13) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Books_Status DEFAULT 'Active',
        ArchivedAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Books_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NULL,
        ReplacementValue DECIMAL(18,2) NULL,
        RentalPrice DECIMAL(18,2) NULL,
        Category NVARCHAR(100) NULL,
        Publisher NVARCHAR(150) NULL,
        Language NVARCHAR(50) NULL,
        PublishYear INT NULL,
        -- Legacy snapshots only; current inventory is derived from BookCopies after migration.
        Quantity INT NOT NULL DEFAULT 0,
        AvailableQuantity INT NOT NULL DEFAULT 0
    );
END
GO

-- Book owns the current replacement value. Legacy rows stay NULL until explicitly set.
IF COL_LENGTH('dbo.Books', 'ReplacementValue') IS NULL
    ALTER TABLE dbo.Books ADD ReplacementValue DECIMAL(18,2) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.Books') AND name = 'CK_Books_ReplacementValue')
    ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_ReplacementValue
        CHECK (ReplacementValue IS NULL OR ReplacementValue >= 0);
GO
IF COL_LENGTH('dbo.Books', 'RentalPrice') IS NULL
    ALTER TABLE dbo.Books ADD RentalPrice DECIMAL(18,2) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.Books') AND name = 'CK_Books_RentalPrice')
    ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_RentalPrice
        CHECK (RentalPrice IS NULL OR RentalPrice >= 0);
GO

-- 2a. BẢNG BOOK COPIES (Từng bản sách vật lý)
IF COL_LENGTH('dbo.Books', 'ISBN') IS NULL
    ALTER TABLE dbo.Books ADD ISBN NVARCHAR(13) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Books') AND name = 'UX_Books_ISBN')
    CREATE UNIQUE INDEX UX_Books_ISBN ON dbo.Books(ISBN) WHERE ISBN IS NOT NULL;
GO
IF COL_LENGTH('dbo.Books', 'Status') IS NULL
    ALTER TABLE dbo.Books ADD Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Books_Status DEFAULT 'Active';
GO
IF COL_LENGTH('dbo.Books', 'ArchivedAt') IS NULL
    ALTER TABLE dbo.Books ADD ArchivedAt DATETIME2 NULL;
GO
-- Existing rows receive the migration time, not an invented original creation date.
IF COL_LENGTH('dbo.Books', 'CreatedAt') IS NULL
    ALTER TABLE dbo.Books ADD CreatedAt DATETIME2 NOT NULL
        CONSTRAINT DF_Books_CreatedAt DEFAULT SYSUTCDATETIME();
GO
IF COL_LENGTH('dbo.Books', 'UpdatedAt') IS NULL
    ALTER TABLE dbo.Books ADD UpdatedAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Books_Status')
    ALTER TABLE dbo.Books ADD CONSTRAINT CK_Books_Status CHECK (Status IN ('Active', 'Archived'));
GO
IF COL_LENGTH('dbo.Books', 'Publisher') IS NULL
    ALTER TABLE dbo.Books ADD Publisher NVARCHAR(150) NULL;
GO
IF COL_LENGTH('dbo.Books', 'Language') IS NULL
    ALTER TABLE dbo.Books ADD Language NVARCHAR(50) NULL;
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BookCopies')
BEGIN
    CREATE TABLE BookCopies (
        CopyId INT IDENTITY(1,1) PRIMARY KEY,
        BookId INT NOT NULL,
        Barcode NVARCHAR(100) NULL,
        Status NVARCHAR(30) NOT NULL DEFAULT 'Available',
        Condition NVARCHAR(50) NOT NULL DEFAULT 'Good',
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BookCopies_Books FOREIGN KEY (BookId) REFERENCES Books(BookId),
        CONSTRAINT CK_BookCopies_Status CHECK (Status IN ('Available','Borrowed','Lost','Damaged','UnderRepair','Retired'))
    );
END
GO
IF COL_LENGTH('dbo.BookCopies', 'Condition') IS NULL
    ALTER TABLE dbo.BookCopies ADD Condition NVARCHAR(50) NOT NULL
        CONSTRAINT DF_BookCopies_Condition DEFAULT ('Good');
GO
-- Reverse the temporary verification-flag schema if it was applied by the previous build.
IF COL_LENGTH('dbo.BookCopies', 'IsLegacyUnverified') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE dbo.BookCopies
        SET Condition = N''LegacyUnverified''
        WHERE IsLegacyUnverified = 1;';

    DECLARE @LegacyFlagDefaultConstraint sysname;
    SELECT @LegacyFlagDefaultConstraint = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.BookCopies') AND c.name = 'IsLegacyUnverified';
    IF @LegacyFlagDefaultConstraint IS NOT NULL
    BEGIN
        DECLARE @DropLegacyFlagDefaultSql nvarchar(max) =
            N'ALTER TABLE dbo.BookCopies DROP CONSTRAINT ' + QUOTENAME(@LegacyFlagDefaultConstraint);
        EXEC sys.sp_executesql @DropLegacyFlagDefaultSql;
    END;

    ALTER TABLE dbo.BookCopies DROP COLUMN IsLegacyUnverified;
END;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'CK_BookCopies_Status'
    AND (definition NOT LIKE '%Available%' OR definition NOT LIKE '%Borrowed%'
        OR definition NOT LIKE '%Lost%' OR definition NOT LIKE '%Damaged%'
        OR definition NOT LIKE '%UnderRepair%' OR definition NOT LIKE '%Retired%'))
    ALTER TABLE dbo.BookCopies DROP CONSTRAINT CK_BookCopies_Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'CK_BookCopies_Status')
    ALTER TABLE dbo.BookCopies ADD CONSTRAINT CK_BookCopies_Status
        CHECK (Status IN ('Available','Borrowed','Lost','Damaged','UnderRepair','Retired'));
GO
-- Remove the former unfiltered unique constraint before normalizing values.
BEGIN TRY
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UQ_BookCopies_Barcode')
    ALTER TABLE dbo.BookCopies DROP CONSTRAINT UQ_BookCopies_Barcode;
IF EXISTS (SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'Barcode' AND is_nullable = 0)
    ALTER TABLE dbo.BookCopies ALTER COLUMN Barcode NVARCHAR(100) NULL;
-- CopyId is the sole source of truth. Keep all digits when IDs exceed six digits.
UPDATE dbo.BookCopies
SET Barcode = CONCAT('BK-', RIGHT(CONCAT('000000', CONVERT(VARCHAR(20), CopyId)),
    CASE WHEN LEN(CONVERT(VARCHAR(20), CopyId)) > 6
        THEN LEN(CONVERT(VARCHAR(20), CopyId)) ELSE 6 END))
WHERE Barcode IS NULL OR Barcode <> CONCAT('BK-', RIGHT(CONCAT('000000', CONVERT(VARCHAR(20), CopyId)),
    CASE WHEN LEN(CONVERT(VARCHAR(20), CopyId)) > 6
        THEN LEN(CONVERT(VARCHAR(20), CopyId)) ELSE 6 END));
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.BookCopies') AND name = 'UX_BookCopies_Barcode_NotNull')
    CREATE UNIQUE INDEX UX_BookCopies_Barcode_NotNull
        ON dbo.BookCopies(Barcode) WHERE Barcode IS NOT NULL;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- 3. BẢNG READERS (Danh mục độc giả)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Readers')
BEGIN
    CREATE TABLE Readers (
        ReaderId INT IDENTITY(1,1) PRIMARY KEY,
        FullName NVARCHAR(150) NOT NULL,
        ReaderType NVARCHAR(50) NOT NULL DEFAULT 'Student',
        StudentId NVARCHAR(50) NULL,
        IdentityNumber NVARCHAR(50) NULL,
        Phone NVARCHAR(20) NULL,
        Email NVARCHAR(150) NULL,
        Address NVARCHAR(255) NULL,
        RegistrationDate DATETIME NOT NULL DEFAULT GETDATE(),
        MembershipExpiresOn DATE NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Active',
        SuspensionReason NVARCHAR(500) NULL,
        SuspendedDate DATETIME NULL,
        IsDeleted BIT NOT NULL DEFAULT 0
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'ReaderType')
        ALTER TABLE Readers ADD ReaderType NVARCHAR(50) NOT NULL DEFAULT 'Student';
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'StudentId')
        ALTER TABLE Readers ADD StudentId NVARCHAR(50) NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'IdentityNumber')
        ALTER TABLE Readers ADD IdentityNumber NVARCHAR(50) NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'Address')
        ALTER TABLE Readers ADD Address NVARCHAR(255) NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'RegistrationDate')
        ALTER TABLE Readers ADD RegistrationDate DATETIME NOT NULL DEFAULT GETDATE();
    IF COL_LENGTH('dbo.Readers', 'MembershipExpiresOn') IS NULL
        ALTER TABLE dbo.Readers ADD MembershipExpiresOn DATE NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'Status')
        ALTER TABLE Readers ADD Status NVARCHAR(50) NOT NULL DEFAULT 'Active';
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'SuspensionReason')
        ALTER TABLE Readers ADD SuspensionReason NVARCHAR(500) NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'SuspendedDate')
        ALTER TABLE Readers ADD SuspendedDate DATETIME NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Readers' AND COLUMN_NAME = 'IsDeleted')
        ALTER TABLE Readers ADD IsDeleted BIT NOT NULL DEFAULT 0;
END
GO

-- 4. BẢNG BORROWRECORDS (Phiếu mượn/trả sách)
IF OBJECT_ID('dbo.LoanPolicies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoanPolicies (
        LoanPolicyId INT IDENTITY(1,1) PRIMARY KEY,
        ReaderType NVARCHAR(50) NOT NULL,
        LoanPeriodDays INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_LoanPolicies_IsActive DEFAULT 1,
        CONSTRAINT UQ_LoanPolicies_ReaderType UNIQUE (ReaderType),
        CONSTRAINT CK_LoanPolicies_LoanPeriodDays CHECK (LoanPeriodDays > 0)
    );
    INSERT INTO dbo.LoanPolicies (ReaderType, LoanPeriodDays)
    VALUES ('Student', 14), ('External', 7);
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BorrowRecords')
BEGIN
    CREATE TABLE BorrowRecords (
        BorrowId INT IDENTITY(1,1) PRIMARY KEY,
        BookId INT NOT NULL,
        ReaderId INT NOT NULL,
        BorrowDate DATETIME NOT NULL DEFAULT GETDATE(),
        DueDate DATETIME NOT NULL,
        ReturnDate DATETIME NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'Borrowing',
        CONSTRAINT FK_BorrowRecords_Books FOREIGN KEY (BookId) REFERENCES Books(BookId),
        CONSTRAINT FK_BorrowRecords_Readers FOREIGN KEY (ReaderId) REFERENCES Readers(ReaderId)
    );
END
GO

-- CopyId is nullable for old borrowing history; new loans always record a physical copy.
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BorrowRecords' AND COLUMN_NAME = 'CopyId')
    ALTER TABLE BorrowRecords ADD CopyId INT NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BorrowRecords' AND COLUMN_NAME = 'LoanPeriodDaysApplied')
    ALTER TABLE BorrowRecords ADD LoanPeriodDaysApplied INT NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'ReturnCondition') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD ReturnCondition NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'ConditionNote') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD ConditionNote NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'LostDate') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD LostDate DATETIME NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'LostNote') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD LostNote NVARCHAR(500) NULL;
GO

-- Upgrade the old loan-status check so lost-book reports can be saved.
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.BorrowRecords')
      AND name = 'CHK_BorrowRecords_Status'
      AND definition NOT LIKE '%Lost%')
    ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CHK_BorrowRecords_Status;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.BorrowRecords')
      AND name = 'CHK_BorrowRecords_Status')
    ALTER TABLE dbo.BorrowRecords ADD CONSTRAINT CHK_BorrowRecords_Status
        CHECK (Status IN ('Borrowing', 'Returned', 'Overdue', 'Lost'));
GO

-- Run after adding ReturnCondition in a separate batch for existing databases.
IF COL_LENGTH('dbo.BorrowRecords', 'ReaderNameSnapshot') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD ReaderNameSnapshot NVARCHAR(150) NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'BookTitleSnapshot') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD BookTitleSnapshot NVARCHAR(255) NULL;
IF COL_LENGTH('dbo.BorrowRecords', 'BarcodeSnapshot') IS NULL
    ALTER TABLE dbo.BorrowRecords ADD BarcodeSnapshot NVARCHAR(100) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BorrowRecords_ReturnCondition' AND definition NOT LIKE '%NeedsRepair%')
    ALTER TABLE dbo.BorrowRecords DROP CONSTRAINT CK_BorrowRecords_ReturnCondition;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_BorrowRecords_ReturnCondition')
    ALTER TABLE dbo.BorrowRecords ADD CONSTRAINT CK_BorrowRecords_ReturnCondition
        CHECK (ReturnCondition IS NULL OR ReturnCondition IN ('Normal', 'Damaged', 'NeedsRepair'));
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_BorrowRecords_BookCopies')
    ALTER TABLE BorrowRecords ADD CONSTRAINT FK_BorrowRecords_BookCopies FOREIGN KEY (CopyId) REFERENCES BookCopies(CopyId);
GO

IF OBJECT_ID('dbo.CirculationAuditEvents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CirculationAuditEvents (
        AuditEventId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BorrowId INT NOT NULL,
        EventType NVARCHAR(40) NOT NULL,
        ActorUserId INT NULL,
        ActorNameSnapshot NVARCHAR(150) NOT NULL,
        OccurredAt DATETIME2 NOT NULL,
        BookCopyId INT NULL,
        Note NVARCHAR(500) NULL,
        CONSTRAINT FK_CirculationAudit_Borrow FOREIGN KEY (BorrowId) REFERENCES dbo.BorrowRecords(BorrowId),
        CONSTRAINT FK_CirculationAudit_User FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_CirculationAudit_EventType CHECK (EventType IN
            ('BorrowCreated','LegacyCopyMapped','Returned','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair','MarkedLost'))
    );
    CREATE INDEX IX_CirculationAudit_Borrow_OccurredAt
        ON dbo.CirculationAuditEvents(BorrowId, OccurredAt, AuditEventId);
END
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CirculationAudit_EventType'
    AND (definition NOT LIKE '%ReturnedNeedsRepair%' OR definition NOT LIKE '%''Returned''%'
        OR definition NOT LIKE '%MarkedLost%'))
    ALTER TABLE dbo.CirculationAuditEvents DROP CONSTRAINT CK_CirculationAudit_EventType;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CirculationAudit_EventType')
    ALTER TABLE dbo.CirculationAuditEvents ADD CONSTRAINT CK_CirculationAudit_EventType CHECK (EventType IN
    ('BorrowCreated','LegacyCopyMapped','Returned','ReturnedNormal','ReturnedDamaged','ReturnedNeedsRepair','MarkedLost'));
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CirculationAudit_BookCopy')
    ALTER TABLE dbo.CirculationAuditEvents ADD CONSTRAINT FK_CirculationAudit_BookCopy
        FOREIGN KEY (BookCopyId) REFERENCES dbo.BookCopies(CopyId);
GO

-- =====================================================================
-- DỮ LIỆU MẪU ĐỂ KIỂM THỬ (SEED DATA)
-- =====================================================================

-- Tài khoản kiểm thử:
-- 1. Admin: username 'admin', mật khẩu 'admin123', quyền Administrator
-- 2. Thủ thư: username 'librarian', mật khẩu 'librarian123', quyền Librarian
IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'admin')
BEGIN
    INSERT INTO Users (Username, Email, FullName, PasswordHash, Role, CreatedAt)
    VALUES ('admin', 'admin@library.com', N'Quản Trị Viên', '$2a$11$cNARD6DZjrzznOAzgdld/uPZlQclE0gYgWuksm0vhQ58LkLG39KPO', 'Administrator', GETDATE());
END

IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'librarian')
BEGIN
    INSERT INTO Users (Username, Email, FullName, PasswordHash, Role, CreatedAt)
    VALUES ('librarian', 'librarian@library.com', N'Thủ Thư Mẫu', '$2a$11$.z9Gd4uVHXS3IcbDNtyy3uOGhiRFxgkqOhNoMIMUUdtSnFW5vOB8a', 'Librarian', GETDATE());
END
GO

-- Dữ liệu Sách mẫu
IF NOT EXISTS (SELECT 1 FROM Books)
BEGIN
    SET IDENTITY_INSERT Books ON;
    INSERT INTO Books (BookId, Title, Author, Category, PublishYear, Quantity, AvailableQuantity) VALUES
    (1, N'Clean Code', N'Robert C. Martin', N'Programming', 2008, 11, 8),
    (2, N'C# in Depth', N'Jon Skeet', N'Programming', 2019, 5, 2),
    (3, N'The Pragmatic Programmer', N'Andrew Hunt', N'Programming', 1999, 6, 6),
    (4, N'Design Patterns', N'Gang of Four', N'Programming', 1994, 4, 1),
    (5, N'Refactoring', N'Martin Fowler', N'Programming', 2018, 5, 3),
    (6, N'Bruh', N'Thái', N'Comedy', 2026, 10, 10);
    SET IDENTITY_INSERT Books OFF;
END
GO

-- Dữ liệu Độc giả mẫu
IF NOT EXISTS (SELECT 1 FROM Readers)
BEGIN
    SET IDENTITY_INSERT Readers ON;
    INSERT INTO Readers (ReaderId, FullName, ReaderType, StudentId, IdentityNumber, Phone, Email, Address, RegistrationDate, Status, IsDeleted) VALUES
    (1, N'Nguyễn Văn An', 'Student', '23A12345', NULL, '0901234567', 'nguyenvana@email.com', N'Hà Nội', '2026-09-01', 'Active', 0),
    (2, N'Trần Minh Bảo', 'External', NULL, '001201007789', '0912345678', 'tranvanb@email.com', N'Thái Nguyên', '2026-09-02', 'Active', 0),
    (3, N'Lê Thị Mai', 'Student', '23A54321', NULL, '0923456789', 'lethic@email.com', N'Đà Nẵng', '2026-09-05', 'Suspended', 0);
    SET IDENTITY_INSERT Readers OFF;
END
GO

-- Dữ liệu Phiếu mượn mẫu
IF NOT EXISTS (SELECT 1 FROM BorrowRecords)
BEGIN
    SET IDENTITY_INSERT BorrowRecords ON;
    INSERT INTO BorrowRecords (BorrowId, BookId, ReaderId, BorrowDate, DueDate, ReturnDate, Status) VALUES
    (1, 1, 1, '2026-09-01', '2026-09-08', '2026-09-10 21:12:20', 'Returned'),
    (2, 2, 2, '2026-09-02', '2026-09-09', '2026-09-10 21:17:16', 'Returned'),
    (3, 4, 1, '2026-08-20', '2026-08-27', '2026-08-26 15:30:00', 'Returned'),
    (4, 1, 1, '2026-09-10', '2026-09-17', '2026-09-10 21:06:00', 'Returned'),
    (5, 5, 3, '2026-09-11', '2026-09-20', '2026-09-10 21:24:10', 'Returned'),
    (6, 2, 3, '2026-09-10', '2026-09-17', '2026-09-10 21:12:29', 'Returned'),
    (7, 1, 2, '2026-09-10', '2026-09-17', '2026-09-10 21:17:27', 'Returned'),
    (8, 4, 1, '2026-09-10', '2026-09-17', '2026-09-10 21:17:24', 'Returned'),
    (9, 1, 2, '2026-09-08', '2026-09-09', '2026-09-10 21:12:25', 'Returned'),
    (10, 1, 1, '2026-09-10', '2026-09-17', '2026-09-10 21:17:21', 'Returned'),
    (11, 2, 1, '2026-09-03', '2026-09-11', '2026-09-10 21:17:18', 'Returned'),
    (12, 1, 1, '2026-09-11', '2026-09-17', '2026-09-10 21:23:55', 'Returned'),
    (13, 5, 3, '2026-09-10', '2026-09-17', NULL, 'Borrowing'),
    (14, 1, 1, '2026-09-08', '2026-09-21', NULL, 'Borrowing'),
    (15, 4, 3, '2026-09-14', '2026-09-21', NULL, 'Borrowing'),
    (16, 2, 1, '2026-09-15', '2026-09-22', NULL, 'Borrowing'),
    (17, 2, 3, '2026-09-15 13:48:00', '2026-09-22', NULL, 'Borrowing'),
    (18, 5, 2, '2026-09-17', '2026-09-27', NULL, 'Borrowing');
    SET IDENTITY_INSERT BorrowRecords OFF;
END
GO

-- Backfill physical copies from legacy aggregate inventory. Existing open loans
-- keep CopyId NULL until a librarian verifies the physical barcode and links it.
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @BackfilledBooks TABLE (BookId INT PRIMARY KEY, LegacyQuantity INT, LegacyAvailable INT);
DECLARE @BackfillBookId INT, @BackfillQuantity INT, @CopyNumber INT, @GeneratedCopyId INT;
DECLARE books_without_copies CURSOR LOCAL FAST_FORWARD FOR
    SELECT BookId, Quantity FROM Books b
    WHERE NOT EXISTS (SELECT 1 FROM BookCopies c WHERE c.BookId = b.BookId);
OPEN books_without_copies;
FETCH NEXT FROM books_without_copies INTO @BackfillBookId, @BackfillQuantity;
WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO @BackfilledBooks (BookId, LegacyQuantity, LegacyAvailable)
    SELECT BookId, Quantity, AvailableQuantity FROM Books WHERE BookId = @BackfillBookId;
    SET @CopyNumber = 1;
    WHILE @CopyNumber <= @BackfillQuantity
    BEGIN
        INSERT INTO BookCopies (BookId, Barcode, Status)
        VALUES (@BackfillBookId, NULL, 'Available');
        SET @GeneratedCopyId = CONVERT(INT, SCOPE_IDENTITY());
        UPDATE BookCopies SET Barcode = CONCAT('BK-', RIGHT(CONCAT('000000', @GeneratedCopyId),
            CASE WHEN LEN(CONVERT(VARCHAR(20), @GeneratedCopyId)) > 6
                THEN LEN(CONVERT(VARCHAR(20), @GeneratedCopyId)) ELSE 6 END))
        WHERE CopyId = @GeneratedCopyId AND Barcode IS NULL;
        IF @@ROWCOUNT <> 1 THROW 50003, 'Could not assign generated copy barcode.', 1;
        SET @CopyNumber += 1;
    END;
    FETCH NEXT FROM books_without_copies INTO @BackfillBookId, @BackfillQuantity;
END;
CLOSE books_without_copies;
DEALLOCATE books_without_copies;

DECLARE @ReviewBookId INT, @NeedsReview INT, @ReviewCopyId INT;
DECLARE books_needing_review CURSOR LOCAL FAST_FORWARD FOR
    SELECT b.BookId,
           CASE WHEN b.LegacyQuantity - b.LegacyAvailable <
                (SELECT COUNT(*) FROM BorrowRecords r WHERE r.BookId = b.BookId AND r.Status = 'Borrowing' AND r.CopyId IS NULL)
                THEN (SELECT COUNT(*) FROM BorrowRecords r WHERE r.BookId = b.BookId AND r.Status = 'Borrowing' AND r.CopyId IS NULL)
                ELSE b.LegacyQuantity - b.LegacyAvailable END
    FROM @BackfilledBooks b;
OPEN books_needing_review;
FETCH NEXT FROM books_needing_review INTO @ReviewBookId, @NeedsReview;
WHILE @@FETCH_STATUS = 0
BEGIN
    WHILE @NeedsReview > 0
    BEGIN
        SET @ReviewCopyId = NULL;
        SELECT TOP (1) @ReviewCopyId = CopyId FROM BookCopies
        WHERE BookId = @ReviewBookId AND Status = 'Available' ORDER BY CopyId;
        IF @ReviewCopyId IS NULL
            THROW 50002, 'Legacy unavailable inventory exceeds the number of physical copies.', 1;
        UPDATE BookCopies SET Status = 'UnderRepair', Condition = 'LegacyUnverified'
        WHERE CopyId = @ReviewCopyId;
        SET @NeedsReview -= 1;
    END;
    FETCH NEXT FROM books_needing_review INTO @ReviewBookId, @NeedsReview;
END;
CLOSE books_needing_review;
DEALLOCATE books_needing_review;

-- Preserve legacy snapshots. Current counts are read from physical copies;
-- drift detection reports differences without silently rewriting old data.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BorrowRecords')
    AND name = 'UX_BorrowRecords_ActiveCopy')
    CREATE UNIQUE INDEX UX_BorrowRecords_ActiveCopy ON dbo.BorrowRecords(CopyId)
    WHERE CopyId IS NOT NULL AND Status = 'Borrowing';
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
