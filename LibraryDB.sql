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
        Category NVARCHAR(100) NULL,
        PublishYear INT NULL,
        Quantity INT NOT NULL DEFAULT 0,
        AvailableQuantity INT NOT NULL DEFAULT 0
    );
END
GO

-- 3. BẢNG READERS (Danh mục độc giả)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Readers')
BEGIN
    CREATE TABLE Readers (
        ReaderId INT IDENTITY(1,1) PRIMARY KEY,
        FullName NVARCHAR(150) NOT NULL,
        Phone NVARCHAR(20) NULL,
        Email NVARCHAR(150) NULL
    );
END
GO

-- 4. BẢNG BORROWRECORDS (Phiếu mượn/trả sách)
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
    INSERT INTO Readers (ReaderId, FullName, Phone, Email) VALUES
    (1, N'Nguyễn Văn A', '0901234567', 'nguyenvana@email.com'),
    (2, N'Trần Văn B', '0912345678', 'tranvanb@email.com'),
    (3, N'Lê Thị C', '0923456789', 'lethic@email.com');
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
