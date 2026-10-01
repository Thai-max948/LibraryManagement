using System;
using System.Collections.Generic;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using LibraryManagement.Services;
using Moq;
using Xunit;

namespace LibraryManagement.Tests
{
    public class ReaderEligibilityTests
    {
        [Fact]
        public void CheckEligibility_ActiveReaderWithoutLoans_IsEligible()
        {
            var readerRepository = new Mock<ReaderRepository>();
            var borrowRepository = new Mock<BorrowRepository>();
            readerRepository.Setup(repository => repository.GetById(1))
                .Returns(new Reader { ReaderId = 1, Status = "Active" });
            borrowRepository.Setup(repository => repository.GetBorrowingRecords())
                .Returns(new List<BorrowRecord>());
            var service = new ReaderEligibilityService(readerRepository.Object, borrowRepository.Object);

            var result = service.CheckEligibility(1);

            Assert.True(result.IsEligible);
            Assert.Empty(result.Reasons);
            Assert.Equal(ReaderEligibilityService.BorrowLimit, result.BorrowLimit);
        }

        [Fact]
        public void Evaluate_SuspendedReader_IsNotEligible()
        {
            var service = CreateService();

            var result = service.Evaluate(
                new Reader { ReaderId = 1, Status = "Suspended" },
                Array.Empty<BorrowRecord>());

            Assert.False(result.IsEligible);
            Assert.Contains(result.Reasons, reason => reason.Contains("Suspended"));
        }

        [Fact]
        public void Evaluate_ReaderWithOverdueBook_IsNotEligible()
        {
            var service = CreateService();
            var evaluationDate = new DateTime(2026, 10, 1);
            var loans = new[]
            {
                new BorrowRecord { ReaderId = 1, Status = "Borrowing", DueDate = evaluationDate.AddDays(-1) }
            };

            var result = service.Evaluate(
                new Reader { ReaderId = 1, Status = "Active" }, loans, evaluationDate);

            Assert.False(result.IsEligible);
            Assert.Equal(1, result.OverdueLoans);
            Assert.Contains(result.Reasons, reason => reason.Contains("quá hạn"));
        }

        [Fact]
        public void Evaluate_ReaderAtBorrowLimit_IsNotEligible()
        {
            var service = CreateService();
            var evaluationDate = new DateTime(2026, 10, 1);
            var loans = new List<BorrowRecord>();
            for (int index = 0; index < ReaderEligibilityService.BorrowLimit; index++)
            {
                loans.Add(new BorrowRecord
                {
                    ReaderId = 1,
                    Status = "Borrowing",
                    DueDate = evaluationDate.AddDays(2)
                });
            }

            var result = service.Evaluate(
                new Reader { ReaderId = 1, Status = "Active" }, loans, evaluationDate);

            Assert.False(result.IsEligible);
            Assert.Equal(ReaderEligibilityService.BorrowLimit, result.CurrentLoans);
            Assert.Contains(result.Reasons, reason => reason.Contains("giới hạn"));
        }

        [Fact]
        public void Evaluate_AfterOverdueLoanIsReturned_BecomesEligibleAutomatically()
        {
            var service = CreateService();
            var evaluationDate = new DateTime(2026, 10, 1);
            var loan = new BorrowRecord
            {
                ReaderId = 1,
                Status = "Borrowing",
                DueDate = evaluationDate.AddDays(-1)
            };
            var reader = new Reader { ReaderId = 1, Status = "Active" };

            Assert.False(service.Evaluate(reader, new[] { loan }, evaluationDate).IsEligible);

            loan.Status = "Returned";
            loan.ReturnDate = evaluationDate;

            Assert.True(service.Evaluate(reader, new[] { loan }, evaluationDate).IsEligible);
        }

        private static ReaderEligibilityService CreateService()
        {
            return new ReaderEligibilityService(
                new Mock<ReaderRepository>().Object,
                new Mock<BorrowRepository>().Object);
        }
    }
}
