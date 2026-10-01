using System;
using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class ReaderTests
    {
        [Theory]
        [InlineData("Student", null, null, "Mã sinh viên không được để trống.")]
        [InlineData("External", null, null, "Số CCCD / Định danh không được để trống.")]
        public void AddReader_RequiresIdentificationForSelectedType(string type, string? studentId, string? identityNumber, string message)
        {
            var repo = new Mock<ReaderRepository>();
            var service = new ReaderService(repo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { ReaderType = type, StudentId = studentId, IdentityNumber = identityNumber,
                FullName = "Nguyễn Văn A", Phone = "0901234567" };

            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal(message, ex.Message);
            repo.Verify(r => r.Add(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void AddReader_ExternalWithoutEmail_ClearsStudentIdAndSaves()
        {
            var repo = new Mock<ReaderRepository>();
            repo.Setup(r => r.Add(It.IsAny<Reader>())).Returns(7);
            var service = new ReaderService(repo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { ReaderType = "External", IdentityNumber = "001201007789",
                StudentId = "old", FullName = "Trần Minh Bảo", Phone = "0901234567" };

            Assert.Equal(7, service.AddReader(reader));
            Assert.Null(reader.StudentId);
            Assert.Equal("*********789", reader.DisplayIdentification);
            repo.Verify(r => r.Add(reader), Times.Once);
        }

        [Fact]
        public void UpdateReader_StudentWithoutStudentId_RejectsBeforeRepositoryUpdate()
        {
            var repo = new Mock<ReaderRepository>();
            var service = new ReaderService(repo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { ReaderId = 125, ReaderType = "Student", FullName = "Nguyễn Văn An",
                Phone = "0901234567", StudentId = " " };

            var ex = Assert.Throws<BusinessRuleException>(() => service.UpdateReader(reader));
            Assert.Equal("Mã sinh viên không được để trống.", ex.Message);
            repo.Verify(r => r.Update(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void AddReader_UnknownStatus_RejectsBeforeSaving()
        {
            var repo = new Mock<ReaderRepository>();
            var service = new ReaderService(repo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { ReaderType = "Student", FullName = "Nguyễn Văn An",
                Phone = "0901234567", StudentId = "23A12345", Status = "Deleted" };

            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Trạng thái độc giả không hợp lệ.", ex.Message);
            repo.Verify(r => r.Add(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void AddReader_ValidData_ReturnsNewId()
        {
            // Arrange (TC-READER-01)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var reader = new Reader
            {
                FullName = "Nguyễn Văn A",
                StudentId = "23A12345",
                Phone = "0901234567",
                Email = "a@mail.com"
            };

            mockReaderRepo.Setup(r => r.Add(It.IsAny<Reader>())).Returns(1);
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            int newId = service.AddReader(reader);

            // Assert
            Assert.Equal(1, newId);
            mockReaderRepo.Verify(r => r.Add(reader), Times.Once);
        }

        [Fact]
        public void AddReader_AlwaysStartsLifecycleAsActive()
        {
            var readerRepo = new Mock<ReaderRepository>();
            readerRepo.Setup(repository => repository.Add(It.IsAny<Reader>())).Returns(15);
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader
            {
                FullName = "Nguyễn Văn Mới",
                ReaderType = "Student",
                StudentId = "SV-LIFECYCLE-NEW",
                Phone = "0901234567",
                Status = "Inactive"
            };

            service.AddReader(reader);

            Assert.Equal("Active", reader.Status);
            readerRepo.Verify(repository => repository.Add(reader), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AddReader_EmptyOrWhitespaceFullName_ThrowsBusinessRuleException(string? fullName)
        {
            // Arrange (TC-READER-02)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = fullName!, Phone = "0901234567", Email = "a@mail.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Họ tên không được để trống.", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AddReader_EmptyOrWhitespacePhone_ThrowsBusinessRuleException(string? phone)
        {
            // Arrange (TC-READER-03)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = phone!, Email = "a@mail.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("thiếu thông tin sđt", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AddReader_EmptyOrWhitespaceEmail_IsAllowed(string? email)
        {
            // Arrange (TC-READER-04)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "0901234567", Email = email! };

            service.AddReader(reader);
            mockReaderRepo.Verify(r => r.Add(reader), Times.Once);
        }

        [Fact]
        public void AddReader_Phone8Digits_ThrowsBusinessRuleException()
        {
            // Arrange (TC-READER-05: BVA 8 digits lower - 1)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "09012345", Email = "a@mail.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Số điện thoại không hợp lệ.", ex.Message);
        }

        [Theory]
        [InlineData("090123456")]   // TC-READER-06: 9 digits lower bound
        [InlineData("09012345678")] // TC-READER-07: 11 digits upper bound
        public void AddReader_PhoneValidBoundaries_SavesSuccessfully(string phone)
        {
            // Arrange
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockReaderRepo.Setup(r => r.Add(It.IsAny<Reader>())).Returns(1);
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = phone, Email = "a@mail.com" };

            // Act
            int id = service.AddReader(reader);

            // Assert
            Assert.Equal(1, id);
        }

        [Fact]
        public void AddReader_Phone12Digits_ThrowsBusinessRuleException()
        {
            // Arrange (TC-READER-08: 12 digits upper + 1)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "090123456789", Email = "a@mail.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Số điện thoại không hợp lệ.", ex.Message);
        }

        [Theory]
        [InlineData("09012abc67")]
        [InlineData("+84901234567")]
        [InlineData("090 123 4567")]
        [InlineData("090-123-4567")]
        public void AddReader_PhoneWithLettersOrSpecialChars_ThrowsBusinessRuleException(string phone)
        {
            // Arrange (TC-READER-09)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = phone, Email = "a@mail.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Số điện thoại không hợp lệ.", ex.Message);
        }

        [Fact]
        public void AddReader_EmailMissingAtSymbol_ThrowsBusinessRuleException()
        {
            // Arrange (TC-READER-10)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "0901234567", Email = "abc.com" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Email không hợp lệ.", ex.Message);
        }

        [Theory]
        [InlineData("a@b")]
        [InlineData("a@.com")]
        [InlineData("a b@c.com")]
        public void AddReader_EmailMissingDomainOrMalformed_ThrowsBusinessRuleException(string email)
        {
            // Arrange (TC-READER-11)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "0901234567", Email = email };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Equal("Email không hợp lệ.", ex.Message);
        }

        [Fact]
        public void AddReader_ComplexValidEmail_SavesSuccessfully()
        {
            // Arrange (TC-READER-12)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockReaderRepo.Setup(r => r.Add(It.IsAny<Reader>())).Returns(1);
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var reader = new Reader { StudentId = "23A12345", FullName = "Nguyen Van A", Phone = "0901234567", Email = "a.b+c@sub.mail.com" };

            // Act
            int id = service.AddReader(reader);

            // Assert
            Assert.Equal(1, id);
        }

        [Fact]
        public void AddReader_UnicodeAndApostropheName_PreservedAccurately()
        {
            // Arrange (TC-READER-15)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            mockReaderRepo.Setup(r => r.Add(It.IsAny<Reader>())).Returns(1);
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            const string name = "Nguyễn O'Brien";
            var reader = new Reader { StudentId = "23A12345", FullName = name, Phone = "0901234567", Email = "obrien@mail.com" };

            // Act
            int id = service.AddReader(reader);

            // Assert
            Assert.Equal(1, id);
            Assert.Equal(name, reader.FullName);
        }

        [Fact]
        public void UpdateReader_ValidData_UpdatesSuccessfully()
        {
            // Arrange (TC-READER-17)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var existing = new Reader { ReaderId = 1, FullName = "Nguyen Van A", Phone = "0901234567", Email = "a@mail.com", IsDeleted = false };
            mockReaderRepo.Setup(r => r.GetById(1)).Returns(existing);
            mockReaderRepo.Setup(r => r.Update(It.IsAny<Reader>())).Returns(true);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var updated = new Reader { ReaderId = 1, FullName = "Nguyen Van B", StudentId = "23A12345", Phone = "0909999999", Email = "b@mail.com" };

            // Act
            service.UpdateReader(updated);

            // Assert
            mockReaderRepo.Verify(r => r.Update(updated), Times.Once);
        }

        [Fact]
        public void UpdateReader_InvalidEmail_ThrowsBusinessRuleException()
        {
            // Arrange (TC-READER-18)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            var updated = new Reader { ReaderId = 1, FullName = "Nguyen Van A", Phone = "0901234567", Email = "abc" };

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.UpdateReader(updated));
            Assert.Equal("Email không hợp lệ.", ex.Message);
        }

        [Fact]
        public void DeleteReader_NeverBorrowed_DeletesSuccessfully()
        {
            // Arrange (TC-READER-20)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var reader = new Reader { ReaderId = 1, IsDeleted = false };
            mockReaderRepo.Setup(r => r.GetById(1)).Returns(reader);
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(new List<BorrowRecord>());
            mockReaderRepo.Setup(r => r.Delete(1)).Returns(true);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            service.DeleteReader(1);

            // Assert
            mockReaderRepo.Verify(r => r.Delete(1), Times.Once);
        }

        [Fact]
        public void DeleteReader_CurrentlyHasUnreturnedBooks_ThrowsBusinessRuleException()
        {
            // Arrange (TC-READER-21)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var reader = new Reader { ReaderId = 1, IsDeleted = false };
            mockReaderRepo.Setup(r => r.GetById(1)).Returns(reader);

            var activeRecords = new List<BorrowRecord>
            {
                new BorrowRecord { ReaderId = 1, Status = "Borrowing" }
            };
            mockBorrowRepo.Setup(r => r.GetBorrowingRecords()).Returns(activeRecords);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() => service.DeleteReader(1));
            Assert.Equal("Không thể xóa độc giả đang có sách chưa trả.", ex.Message);
            mockReaderRepo.Verify(r => r.Delete(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void DeleteReader_WithReturnedHistory_RequiresInactiveInstead()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var borrowRepo = new Mock<BorrowRepository>();
            readerRepo.Setup(repository => repository.GetById(1))
                .Returns(new Reader { ReaderId = 1, Status = "Active" });
            borrowRepo.Setup(repository => repository.GetBorrowingRecords())
                .Returns(new List<BorrowRecord>());
            borrowRepo.Setup(repository => repository.GetHistory(1, null, null, null, null))
                .Returns(new List<BorrowRecord>
                {
                    new() { ReaderId = 1, Status = "Returned", ReturnDate = DateTime.Today }
                });
            var service = new ReaderService(readerRepo.Object, borrowRepo.Object);

            var exception = Assert.Throws<BusinessRuleException>(() => service.DeleteReader(1));

            Assert.Contains("Inactive", exception.Message);
            readerRepo.Verify(repository => repository.Delete(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void UpdateReader_ActiveToInactiveWithoutLoans_Succeeds()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var borrowRepo = new Mock<BorrowRepository>();
            readerRepo.Setup(repository => repository.GetById(1))
                .Returns(new Reader { ReaderId = 1, Status = "Active" });
            readerRepo.Setup(repository => repository.Update(It.IsAny<Reader>())).Returns(true);
            borrowRepo.Setup(repository => repository.GetBorrowingRecords())
                .Returns(new List<BorrowRecord>());
            var service = new ReaderService(readerRepo.Object, borrowRepo.Object);
            var updated = new Reader
            {
                ReaderId = 1,
                FullName = "Nguyễn Văn An",
                ReaderType = "Student",
                StudentId = "SV-LIFECYCLE-1",
                Phone = "0901234567",
                Status = "Inactive"
            };

            service.UpdateReader(updated);

            Assert.Equal("Inactive", updated.Status);
            Assert.Empty(updated.SuspensionReason);
            Assert.Null(updated.SuspendedDate);
            readerRepo.Verify(repository => repository.Update(updated), Times.Once);
        }

        [Fact]
        public void UpdateReader_ActiveToInactiveWithCurrentLoan_IsRejected()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var borrowRepo = new Mock<BorrowRepository>();
            readerRepo.Setup(repository => repository.GetById(1))
                .Returns(new Reader { ReaderId = 1, Status = "Active" });
            borrowRepo.Setup(repository => repository.GetBorrowingRecords())
                .Returns(new List<BorrowRecord> { new() { ReaderId = 1, Status = "Borrowing" } });
            var service = new ReaderService(readerRepo.Object, borrowRepo.Object);
            var updated = new Reader
            {
                ReaderId = 1,
                FullName = "Nguyễn Văn An",
                ReaderType = "Student",
                StudentId = "SV-LIFECYCLE-1",
                Phone = "0901234567",
                Status = "Inactive"
            };

            var exception = Assert.Throws<BusinessRuleException>(() => service.UpdateReader(updated));

            Assert.Contains("còn sách chưa trả", exception.Message);
            readerRepo.Verify(repository => repository.Update(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void ReactivateReader_InactiveReader_PreservesReaderIdAndSetsActive()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var reader = new Reader { ReaderId = 125, Status = "Inactive" };
            readerRepo.Setup(repository => repository.GetById(125)).Returns(reader);
            readerRepo.Setup(repository => repository.Update(reader)).Returns(true);
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);

            service.ReactivateReader(125);

            Assert.Equal(125, reader.ReaderId);
            Assert.Equal("Active", reader.Status);
            readerRepo.Verify(repository => repository.Update(reader), Times.Once);
        }

        [Fact]
        public void UpdateReader_InactiveToActive_PreservesReaderId()
        {
            var readerRepo = new Mock<ReaderRepository>();
            readerRepo.Setup(repository => repository.GetById(125))
                .Returns(new Reader { ReaderId = 125, Status = "Inactive" });
            readerRepo.Setup(repository => repository.Update(It.IsAny<Reader>())).Returns(true);
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);
            var updated = new Reader
            {
                ReaderId = 125,
                FullName = "Nguyễn Văn An",
                ReaderType = "Student",
                StudentId = "SV-LIFECYCLE-125",
                Phone = "0901234567",
                Status = "Active"
            };

            service.UpdateReader(updated);

            Assert.Equal(125, updated.ReaderId);
            Assert.Equal("Active", updated.Status);
            readerRepo.Verify(repository => repository.Update(updated), Times.Once);
        }

        [Fact]
        public void SearchReader_Keyword_CallsRepositorySearch()
        {
            // Arrange (TC-READER-23)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var searchResult = new List<Reader> { new Reader { ReaderId = 1, FullName = "Alice" } };
            mockReaderRepo.Setup(r => r.Search("Alice")).Returns(searchResult);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            var results = service.SearchReader("Alice");

            // Assert
            Assert.Single(results);
            Assert.Equal("Alice", results[0].FullName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void SearchReader_EmptyOrWhitespaceKeyword_ReturnsGetAll(string? keyword)
        {
            // Arrange (TC-READER-23 empty)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var all = new List<Reader> { new Reader { ReaderId = 1 }, new Reader { ReaderId = 2 } };
            mockReaderRepo.Setup(r => r.GetAll(false)).Returns(all);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            var results = service.SearchReader(keyword!);

            // Assert
            Assert.Equal(2, results.Count);
            mockReaderRepo.Verify(r => r.GetAll(false), Times.Once);
            mockReaderRepo.Verify(r => r.Search(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void ReadersViewModel_TotalReadersCount_MatchesLoadedList()
        {
            // Arrange (TC-READER-25)
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();

            var readers = new List<Reader>
            {
                new Reader { ReaderId = 1, FullName = "R1" },
                new Reader { ReaderId = 2, FullName = "R2" },
                new Reader { ReaderId = 3, FullName = "R3" }
            };
            mockReaderRepo.Setup(r => r.GetAll(false)).Returns(readers);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act & Assert
            StaHelper.RunInSta(() =>
            {
                var vm = new ReadersViewModel(service);
                Assert.Equal(3, vm.TotalReaders);
            });
        }

        [Fact]
        public void ReadersViewModel_NextPage_ShowsRemainingReadersAndUpdatesSummary()
        {
            var mockReaderRepo = new Mock<ReaderRepository>();
            var readers = new List<Reader>();
            for (int i = 1; i <= 25; i++)
            {
                readers.Add(new Reader
                {
                    ReaderId = i,
                    FullName = $"Reader {i:D2}",
                    ReaderType = "Student",
                    StudentId = $"SV{i:D4}",
                    Status = "Active"
                });
            }
            mockReaderRepo.Setup(r => r.GetAll(false)).Returns(readers);

            var service = new ReaderService(mockReaderRepo.Object, new Mock<BorrowRepository>().Object);

            StaHelper.RunInSta(() =>
            {
                var vm = new ReadersViewModel(service);
                Assert.Equal(20, vm.Readers.Count);
                Assert.Equal(2, vm.TotalPages);

                vm.NextPageCommand.Execute(null);

                Assert.Equal(2, vm.CurrentPage);
                Assert.Equal(5, vm.Readers.Count);
                Assert.Equal("Trang 2/2 • 25 độc giả", vm.PageSummary);
            });
        }

        [Theory]
        [InlineData(1, "R000001")]
        [InlineData(125, "R000125")]
        [InlineData(123456, "R123456")]
        public void Reader_FormattedId_FormatsWithSixDigitsAndPrefixR(int id, string expectedFormattedId)
        {
            var reader = new Reader { ReaderId = id };
            Assert.Equal(expectedFormattedId, reader.FormattedId);
        }

        [Fact]
        public void Reader_DisplayIdentification_Student_ReturnsStudentId()
        {
            var reader = new Reader
            {
                ReaderType = "Student",
                StudentId = "23A12345"
            };
            Assert.Equal("23A12345", reader.DisplayIdentification);
        }

        [Fact]
        public void Reader_DisplayIdentification_External_MasksIdentityNumber()
        {
            var reader = new Reader
            {
                ReaderType = "External",
                IdentityNumber = "001201007789"
            };
            Assert.Equal("*********789", reader.DisplayIdentification);
        }

        [Fact]
        public void Reader_DisplayIdentification_Empty_ReturnsDash()
        {
            var student = new Reader { ReaderType = "Student", StudentId = "" };
            var external = new Reader { ReaderType = "External", IdentityNumber = null };

            Assert.Equal("-", student.DisplayIdentification);
            Assert.Equal("-", external.DisplayIdentification);
        }

        [Fact]
        public void SearchReader_WithReaderTypeAndStatusFilters_CallsRepositoryWithFilters()
        {
            // Arrange
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var expectedList = new List<Reader> { new Reader { ReaderId = 1, ReaderType = "Student", Status = "Active" } };

            mockReaderRepo.Setup(r => r.Search("An", "Student", "Active")).Returns(expectedList);
            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            var result = service.SearchReader("An", "Student", "Active");

            // Assert
            Assert.Single(result);
            mockReaderRepo.Verify(r => r.Search("An", "Student", "Active"), Times.Once);
        }

        [Fact]
        public void ToggleStatus_ActiveToSuspended_UpdatesSuccessfully()
        {
            // Arrange
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var reader = new Reader { ReaderId = 1, Status = "Active", IsDeleted = false };

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(reader);
            mockReaderRepo.Setup(r => r.Update(It.IsAny<Reader>())).Returns(true);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            service.ToggleStatus(1);

            // Assert
            Assert.Equal("Suspended", reader.Status);
            Assert.Equal("Khóa thủ công", reader.SuspensionReason);
            Assert.NotNull(reader.SuspendedDate);
            mockReaderRepo.Verify(r => r.Update(reader), Times.Once);
        }

        [Fact]
        public void ToggleStatus_SuspendedToActive_UpdatesSuccessfully()
        {
            // Arrange
            var mockReaderRepo = new Mock<ReaderRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var reader = new Reader { ReaderId = 1, Status = "Suspended", IsDeleted = false };

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(reader);
            mockReaderRepo.Setup(r => r.Update(It.IsAny<Reader>())).Returns(true);

            var service = new ReaderService(mockReaderRepo.Object, mockBorrowRepo.Object);

            // Act
            service.ToggleStatus(1);

            // Assert
            Assert.Equal("Active", reader.Status);
            Assert.Empty(reader.SuspensionReason);
            Assert.Null(reader.SuspendedDate);
            mockReaderRepo.Verify(r => r.Update(reader), Times.Once);
        }

        [Fact]
        public void SuspendReader_WithoutReason_IsRejected()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);

            var exception = Assert.Throws<BusinessRuleException>(() => service.SuspendReader(1, "   "));

            Assert.Contains("lý do", exception.Message);
            readerRepo.Verify(repository => repository.Update(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void SuspendReader_ActiveReader_RecordsReasonAndDate()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var reader = new Reader { ReaderId = 1, Status = "Active" };
            readerRepo.Setup(repository => repository.GetById(1)).Returns(reader);
            readerRepo.Setup(repository => repository.Update(reader)).Returns(true);
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);

            service.SuspendReader(1, "  Vi phạm quy định  ");

            Assert.Equal("Suspended", reader.Status);
            Assert.Equal("Vi phạm quy định", reader.SuspensionReason);
            Assert.NotNull(reader.SuspendedDate);
            readerRepo.Verify(repository => repository.Update(reader), Times.Once);
        }

        [Fact]
        public void CanBorrow_ReaderIsSuspended_ReturnsFalseWithReason()
        {
            // Arrange
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            var suspendedReader = new Reader { ReaderId = 1, Status = "Suspended", IsDeleted = false };
            var book = new Book { BookId = 1, AvailableQuantity = 3 };

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(suspendedReader);
            mockBookRepo.Setup(r => r.GetById(1)).Returns(book);

            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act
            bool canBorrow = borrowService.CanBorrow(1, 1, out string reason);

            // Assert
            Assert.False(canBorrow);
            Assert.Contains("Suspended", reason);
        }

        [Fact]
        public void BorrowBook_ReaderIsSuspended_ThrowsBusinessRuleException()
        {
            // Arrange
            var mockBookRepo = new Mock<BookRepository>();
            var mockBorrowRepo = new Mock<BorrowRepository>();
            var mockReaderRepo = new Mock<ReaderRepository>();

            var suspendedReader = new Reader { ReaderId = 1, Status = "Suspended", IsDeleted = false };

            mockReaderRepo.Setup(r => r.GetById(1)).Returns(suspendedReader);
            var borrowService = new BorrowService(mockBookRepo.Object, mockBorrowRepo.Object, mockReaderRepo.Object);

            // Act & Assert
            var ex = Assert.Throws<BusinessRuleException>(() =>
                borrowService.BorrowBook(1, 1, DateTime.Today, DateTime.Today.AddDays(7)));

            Assert.Contains("Suspended", ex.Message);
        }

        [Fact]
        public void AddReader_DuplicateStudentId_IsRejected()
        {
            var readerRepo = new Mock<ReaderRepository>();
            readerRepo.Setup(r => r.IdentificationExists("Student", "23A12345", 0)).Returns(true);
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { FullName = "Nguyễn Văn An", ReaderType = "Student",
                StudentId = "23A12345", Phone = "0901234567" };

            var ex = Assert.Throws<BusinessRuleException>(() => service.AddReader(reader));
            Assert.Contains("Mã sinh viên", ex.Message);
            readerRepo.Verify(r => r.Add(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void UpdateReader_NewSuspension_RequiresReason()
        {
            var readerRepo = new Mock<ReaderRepository>();
            readerRepo.Setup(r => r.GetById(1)).Returns(new Reader { ReaderId = 1, Status = "Active" });
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);
            var reader = new Reader { ReaderId = 1, FullName = "Nguyễn Văn An", ReaderType = "Student",
                StudentId = "23A12345", Phone = "0901234567", Status = "Suspended" };

            var ex = Assert.Throws<BusinessRuleException>(() => service.UpdateReader(reader));
            Assert.Contains("lý do", ex.Message);
            readerRepo.Verify(r => r.Update(It.IsAny<Reader>()), Times.Never);
        }

        [Fact]
        public void GetReaderPage_SortsAndPaginatesFilteredResults()
        {
            var readerRepo = new Mock<ReaderRepository>();
            readerRepo.Setup(r => r.GetAll(false)).Returns(new List<Reader>
            {
                new() { ReaderId = 1, FullName = "An" }, new() { ReaderId = 2, FullName = "Bình" },
                new() { ReaderId = 3, FullName = "Chi" }, new() { ReaderId = 4, FullName = "Dũng" },
                new() { ReaderId = 5, FullName = "Giang" }, new() { ReaderId = 6, FullName = "Hà" }
            });
            var service = new ReaderService(readerRepo.Object, new Mock<BorrowRepository>().Object);

            var page = service.GetReaderPage("", "All", "All", "Name Z-A", 2, 5);

            Assert.Equal(6, page.TotalCount);
            Assert.Equal(2, page.PageNumber);
            Assert.Single(page.Items);
            Assert.Equal("An", page.Items[0].FullName);
        }

        [Fact]
        public void GetReaderProfile_CalculatesSummaryAndBookTitles()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var borrowRepo = new Mock<BorrowRepository>();
            var bookRepo = new Mock<BookRepository>();
            readerRepo.Setup(r => r.GetById(7)).Returns(new Reader { ReaderId = 7, FullName = "Mai" });
            borrowRepo.Setup(r => r.GetHistory(7, null, null, null, null)).Returns(new List<BorrowRecord>
            {
                new() { BorrowId = 1, ReaderId = 7, BookId = 11, Status = "Borrowing", DueDate = DateTime.Today.AddDays(-1) },
                new() { BorrowId = 2, ReaderId = 7, BookId = 12, Status = "Returned", DueDate = DateTime.Today.AddDays(-5), ReturnDate = DateTime.Today.AddDays(-4) }
            });
            bookRepo.Setup(r => r.GetById(11)).Returns(new Book { BookId = 11, Title = "Clean Code" });
            bookRepo.Setup(r => r.GetById(12)).Returns(new Book { BookId = 12, Title = "Refactoring" });
            var service = new ReaderService(readerRepo.Object, borrowRepo.Object, bookRepo.Object);

            var profile = service.GetReaderProfile(7);

            Assert.Equal(2, profile.TotalBorrowed);
            Assert.Equal(1, profile.CurrentlyBorrowing);
            Assert.Equal(1, profile.OverdueCount);
            Assert.False(profile.Eligibility.IsEligible);
            Assert.Contains("quá hạn", profile.Eligibility.ReasonSummary);
            Assert.Contains(profile.BorrowingHistory, h => h.BookTitle == "Clean Code");
        }

        [Fact]
        public void GetReaderProfile_ExposesOnlyFiveMostRecentActivities()
        {
            var readerRepo = new Mock<ReaderRepository>();
            var borrowRepo = new Mock<BorrowRepository>();
            var bookRepo = new Mock<BookRepository>();
            readerRepo.Setup(repository => repository.GetById(8))
                .Returns(new Reader { ReaderId = 8, FullName = "Lan", Status = "Active" });
            var records = Enumerable.Range(1, 6)
                .Select(id => new BorrowRecord
                {
                    BorrowId = id,
                    ReaderId = 8,
                    BookId = id,
                    Status = "Returned",
                    BorrowDate = DateTime.Today.AddDays(-id),
                    DueDate = DateTime.Today.AddDays(7 - id),
                    ReturnDate = DateTime.Today.AddDays(1 - id)
                })
                .ToList();
            borrowRepo.Setup(repository => repository.GetHistory(8, null, null, null, null)).Returns(records);
            bookRepo.Setup(repository => repository.GetById(It.IsAny<int>()))
                .Returns<int>(id => new Book { BookId = id, Title = $"Book {id}" });
            var service = new ReaderService(readerRepo.Object, borrowRepo.Object, bookRepo.Object);

            var profile = service.GetReaderProfile(8);

            Assert.Equal(6, profile.TotalBorrowed);
            Assert.Equal(5, profile.BorrowingHistory.Count);
            Assert.DoesNotContain(profile.BorrowingHistory, activity => activity.BorrowId == 6);
        }

        [Theory]
        [InlineData("Active", System.Windows.Visibility.Visible, System.Windows.Visibility.Collapsed)]
        [InlineData("Suspended", System.Windows.Visibility.Collapsed, System.Windows.Visibility.Visible)]
        [InlineData("Inactive", System.Windows.Visibility.Collapsed, System.Windows.Visibility.Visible)]
        public void ReaderDetail_LifecycleActions_MatchReaderStatus(
            string status,
            System.Windows.Visibility expectedActiveActions,
            System.Windows.Visibility expectedReactivate)
        {
            StaHelper.RunInSta(() =>
            {
                var profile = new ReaderProfile { Reader = new Reader { ReaderId = 5, Status = status } };
                var service = new ReaderService(new Mock<ReaderRepository>().Object, new Mock<BorrowRepository>().Object);
                var dialog = new LibraryManagement.Views.Readers.ReaderDetailDialog(profile, service);
                var activeActions = Assert.IsType<System.Windows.Controls.StackPanel>(dialog.FindName("ActiveActionsPanel"));
                var reactivate = Assert.IsType<System.Windows.Controls.Button>(dialog.FindName("ReactivateButton"));

                Assert.Equal(expectedActiveActions, activeActions.Visibility);
                Assert.Equal(expectedReactivate, reactivate.Visibility);
                dialog.Close();
            });
        }

        [Fact]
        public void LibraryCard_DoesNotExposeLifecycleStatus()
        {
            StaHelper.RunInSta(() =>
            {
                var dialog = new LibraryManagement.Views.Readers.LibraryCardDialog(
                    new Reader { ReaderId = 3, FullName = "Reader", Status = "Inactive" });

                Assert.Null(dialog.FindName("StatusText"));
                dialog.Close();
            });
        }
    }
}

