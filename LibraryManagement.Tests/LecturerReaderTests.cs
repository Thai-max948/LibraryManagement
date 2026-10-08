using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests;

public sealed class LecturerReaderTests
{
    [Fact]
    public void LecturerType_UsesLecturerCodeAndKeepsDepartmentOptional()
    {
        var reader = new Reader
        {
            ReaderType = "Lecturer",
            LecturerCode = " GV001 ",
            Department = " Công nghệ thông tin "
        };

        Assert.True(reader.IsLecturer);
        Assert.False(reader.IsStudent);
        Assert.False(reader.IsExternal);
        Assert.Equal("Mã giảng viên", reader.IdentificationLabel);
        Assert.Equal("GV001", reader.DisplayIdentification);
        Assert.Equal("Công nghệ thông tin", reader.DisplayDepartment);
    }

    [Fact]
    public void AddLecturer_RequiresUniqueCodeAndTrimsOptionalDepartment()
    {
        var repository = new Mock<ReaderRepository>();
        repository.Setup(repo => repo.Add(It.IsAny<Reader>())).Returns(12);
        repository.Setup(repo => repo.IdentificationExists("Lecturer", "GV001", 0)).Returns(false);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);
        var reader = NewLecturer(" GV001 ", " Công nghệ thông tin ");

        Assert.Equal(12, service.AddReader(reader));

        Assert.Equal("Lecturer", reader.ReaderType);
        Assert.Equal("GV001", reader.LecturerCode);
        Assert.Equal("Công nghệ thông tin", reader.Department);
        Assert.Null(reader.StudentId);
        Assert.Null(reader.IdentityNumber);
        repository.Verify(repo => repo.IdentificationExists("Lecturer", "GV001", 0), Times.Once);
    }

    [Fact]
    public void AddLecturer_RejectsMissingCodeBeforeDatabaseWrite()
    {
        var repository = new Mock<ReaderRepository>();
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        var exception = Assert.Throws<BusinessRuleException>(() => service.AddReader(NewLecturer(" ", null)));

        Assert.Equal("Mã giảng viên không được để trống.", exception.Message);
        repository.Verify(repo => repo.Add(It.IsAny<Reader>()), Times.Never);
        repository.Verify(repo => repo.IdentificationExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void AddLecturer_RejectsDuplicateCode()
    {
        var repository = new Mock<ReaderRepository>();
        repository.Setup(repo => repo.IdentificationExists("Lecturer", "GV001", 0)).Returns(true);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);

        var exception = Assert.Throws<BusinessRuleException>(() => service.AddReader(NewLecturer("GV001", null)));

        Assert.Equal("Mã giảng viên đã được sử dụng bởi độc giả khác.", exception.Message);
        repository.Verify(repo => repo.Add(It.IsAny<Reader>()), Times.Never);
    }

    [Fact]
    public void UpdateReader_WhenTypeChanges_PreservesPreviousIdentificationValues()
    {
        var repository = new Mock<ReaderRepository>();
        var current = new Reader { ReaderId = 12, ReaderType = "Lecturer", LecturerCode = "GV001" };
        repository.Setup(repo => repo.GetById(12)).Returns(current);
        repository.Setup(repo => repo.IdentificationExists("Student", "SV001", 12)).Returns(false);
        repository.Setup(repo => repo.Update(It.IsAny<Reader>())).Returns(true);
        var service = new ReaderService(repository.Object, new Mock<BorrowRepository>().Object);
        var edited = new Reader
        {
            ReaderId = 12,
            ReaderType = "Student",
            StudentId = " SV001 ",
            LecturerCode = " GV001 ",
            Department = " Công nghệ thông tin ",
            FullName = "Lecturer",
            Phone = "0901234567"
        };

        service.UpdateReader(edited);

        Assert.Equal("SV001", edited.StudentId);
        Assert.Equal("GV001", edited.LecturerCode);
        Assert.Equal("Công nghệ thông tin", edited.Department);
        repository.Verify(repo => repo.Update(edited), Times.Once);
    }

    private static Reader NewLecturer(string? lecturerCode, string? department) => new()
    {
        ReaderType = "Lecturer",
        LecturerCode = lecturerCode,
        Department = department,
        FullName = "Nguyễn Văn A",
        Phone = "0901234567"
    };
}
