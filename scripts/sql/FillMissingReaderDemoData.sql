-- Chạy trên LibraryDB để điền dữ liệu DEMO cho Reader còn thiếu thông tin.
-- Chỉ cập nhật ô NULL/rỗng của Reader chưa bị xóa; giữ nguyên dữ liệu đã có.
-- Student không cần IdentityNumber và External không cần StudentId.
-- DEMO-* và example.invalid không phải thông tin định danh/liên lạc thật.

USE [LibraryDB];
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Updated TABLE (ReaderId INT PRIMARY KEY);

UPDATE r
SET
    ReaderType = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.ReaderType)), '') IS NULL THEN 'Student'
        ELSE r.ReaderType END,
    FullName = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.FullName)), N'') IS NULL
            THEN CONCAT(N'Độc giả mẫu ', 'R', RIGHT('000000' + CONVERT(VARCHAR(10), r.ReaderId), 6))
        ELSE r.FullName END,
    StudentId = CASE
        WHEN COALESCE(NULLIF(LTRIM(RTRIM(r.ReaderType)), ''), 'Student') = 'Student'
             AND NULLIF(LTRIM(RTRIM(r.StudentId)), '') IS NULL
            THEN CONCAT('DEMO-SV-', RIGHT('000000' + CONVERT(VARCHAR(10), r.ReaderId), 6))
        ELSE r.StudentId END,
    IdentityNumber = CASE
        WHEN r.ReaderType = 'External'
             AND NULLIF(LTRIM(RTRIM(r.IdentityNumber)), '') IS NULL
            THEN CONCAT('DEMO-ID-', RIGHT('000000' + CONVERT(VARCHAR(10), r.ReaderId), 6))
        ELSE r.IdentityNumber END,
    Phone = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.Phone)), '') IS NULL
            THEN CONCAT('0000', RIGHT('000000' + CONVERT(VARCHAR(10), r.ReaderId), 6))
        ELSE r.Phone END,
    Email = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.Email)), '') IS NULL
            THEN CONCAT('reader', r.ReaderId, '@example.invalid')
        ELSE r.Email END,
    Address = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.Address)), N'') IS NULL
            THEN N'Địa chỉ mẫu (chưa xác minh)'
        ELSE r.Address END,
    Status = CASE
        WHEN NULLIF(LTRIM(RTRIM(r.Status)), '') IS NULL THEN 'Suspended'
        ELSE r.Status END
OUTPUT inserted.ReaderId INTO @Updated
FROM dbo.Readers AS r
WHERE r.IsDeleted = 0
  AND (
       NULLIF(LTRIM(RTRIM(r.ReaderType)), '') IS NULL
    OR NULLIF(LTRIM(RTRIM(r.FullName)), N'') IS NULL
    OR (COALESCE(NULLIF(LTRIM(RTRIM(r.ReaderType)), ''), 'Student') = 'Student'
        AND NULLIF(LTRIM(RTRIM(r.StudentId)), '') IS NULL)
    OR (r.ReaderType = 'External' AND NULLIF(LTRIM(RTRIM(r.IdentityNumber)), '') IS NULL)
    OR NULLIF(LTRIM(RTRIM(r.Phone)), '') IS NULL
    OR NULLIF(LTRIM(RTRIM(r.Email)), '') IS NULL
    OR NULLIF(LTRIM(RTRIM(r.Address)), N'') IS NULL
    OR NULLIF(LTRIM(RTRIM(r.Status)), '') IS NULL
  );

SELECT COUNT(*) AS UpdatedReaders FROM @Updated;
SELECT r.ReaderId, r.ReaderType, r.FullName, r.StudentId, r.IdentityNumber,
       r.Phone, r.Email, r.Address, r.Status
FROM dbo.Readers AS r
JOIN @Updated AS u ON u.ReaderId = r.ReaderId
ORDER BY r.ReaderId;

COMMIT TRANSACTION;
GO
