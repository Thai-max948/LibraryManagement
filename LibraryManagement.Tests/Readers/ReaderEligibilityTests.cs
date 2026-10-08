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
            borrowRepository.Setup(repository => repository.GetEligibilityRecords(1))
                .Returns(new List<BorrowRecord>());
            var service = new ReaderEligibilityService(readerRepository.Object, borrowRepository.Object);

            var result = service.CheckEligibility(1);

            Assert.True(result.IsEligible);
            Assert.Empty(result.Reasons);
            Assert.Equal(ReaderEligibilityService.BorrowLimit, result.BorrowLimit);
        }

        [Fact]
        public void CheckEligibility_QueriesOnlySelectedReadersActiveLoans()
        {
            var readerRepository = new Mock<ReaderRepository>();
            var borrowRepository = new Mock<BorrowRepository>();
            readerRepository.Setup(repository => repository.GetById(8))
                .Returns(new Reader { ReaderId = 8, Status = "Active" });
            borrowRepository.Setup(repository => repository.GetEligibilityRecords(8))
                .Returns(new List<BorrowRecord>
                {
                    new() { ReaderId = 8, Status = "Borrowing", DueDate = DateTime.Today.AddDays(2) },
                    new() { ReaderId = 8, Status = "Borrowing", DueDate = DateTime.Today.AddDays(5) }
                });
            var service = new ReaderEligibilityService(readerRepository.Object, borrowRepository.Object);

            var result = service.CheckEligibility(8);

            Assert.True(result.IsEligible);
            Assert.Equal(2, result.CurrentLoans);
            Assert.Equal("Đang mượn 2/3 sách • Quá hạn 0", result.LoanSummary);
            borrowRepository.Verify(repository => repository.GetEligibilityRecords(8), Times.Once);
            borrowRepository.Verify(repository => repository.GetBorrowingRecords(), Times.Never);
        }

        [Fact]
        public void CheckEligibility_AtReaderLimitShowsThreeOfThreeAndBlocksBorrow()
        {
            var readerRepository = new Mock<ReaderRepository>();
            var borrowRepository = new Mock<BorrowRepository>();
            readerRepository.Setup(repository => repository.GetById(9))
                .Returns(new Reader { ReaderId = 9, Status = "Active" });
            borrowRepository.Setup(repository => repository.GetEligibilityRecords(9))
                .Returns(Enumerable.Range(0, ReaderEligibilityService.BorrowLimit)
                    .Select(_ => new BorrowRecord
                    {
                        ReaderId = 9,
                        Status = "Borrowing",
                        DueDate = DateTime.Today.AddDays(1)
                    }).ToList());
            var service = new ReaderEligibilityService(readerRepository.Object, borrowRepository.Object);

            var result = service.CheckEligibility(9);

            Assert.False(result.IsEligible);
            Assert.Equal(3, result.CurrentLoans);
            Assert.Equal("Đang mượn 3/3 sách • Quá hạn 0", result.LoanSummary);
            Assert.Contains(result.Reasons, reason => reason.Contains("giới hạn"));
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
        public void Evaluate_InactiveReader_IsNotEligible()
        {
            var service = CreateService();

            var result = service.Evaluate(
                new Reader { ReaderId = 1, Status = "Inactive" },
                Array.Empty<BorrowRecord>());

            Assert.False(result.IsEligible);
            Assert.Contains(result.Reasons, reason => reason.Contains("Inactive"));
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

        [Fact]
        public void Evaluate_LostLoanDoesNotBlockAfterReplacementFeeIsPaid()
        {
            var standing = new ReaderFinancialStanding { OutstandingAmount = 0m, BlocksBorrowing = false };
            var result = CreateService().Evaluate(
                new Reader { ReaderId = 7, Status = "Active" },
                new[] { new BorrowRecord { ReaderId = 7, Status = "Lost" } },
                financialStanding: standing);

            Assert.True(result.IsEligible);
            Assert.Empty(result.Reasons);
            Assert.Equal(0m, result.OutstandingAmount);
        }

        [Fact]
        public void CheckEligibility_OutstandingLostBookReplacementFeeBlocksUntilPaid()
        {
            var readers = new Mock<ReaderRepository>();
            var loans = new Mock<BorrowRepository>();
            var financial = new Mock<IReaderFinancialStandingProvider>();
            readers.Setup(repository => repository.GetById(7))
                .Returns(new Reader { ReaderId = 7, Status = "Active" });
            loans.Setup(repository => repository.GetEligibilityRecords(7)).Returns(new List<BorrowRecord>());
            financial.SetupSequence(provider => provider.GetStanding(7))
                .Returns(new ReaderFinancialStanding { OutstandingAmount = 250000m, BlocksBorrowing = true })
                .Returns(new ReaderFinancialStanding { OutstandingAmount = 0m, BlocksBorrowing = false });
            var service = new ReaderEligibilityService(readers.Object, loans.Object, financial.Object);

            var unpaid = service.CheckEligibility(7);
            var paid = service.CheckEligibility(7);

            Assert.False(unpaid.IsEligible);
            Assert.Equal(250000m, unpaid.OutstandingAmount);
            Assert.Contains(unpaid.Reasons, reason => reason.Contains("chưa thanh toán"));
            Assert.True(paid.IsEligible);
            Assert.Empty(paid.Reasons);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        public void Evaluate_MembershipExpiryUsesWholeDay(int offsetDays, bool eligible)
        {
            var today = new DateTime(2026, 10, 2);
            var reader = new Reader
            {
                ReaderId = 1,
                Status = "Active",
                MembershipExpiresOn = today.AddDays(offsetDays)
            };
            var result = CreateService().Evaluate(reader, Array.Empty<BorrowRecord>(), today);
            Assert.Equal(eligible, result.IsEligible);
            Assert.Equal(reader.MembershipExpiresOn, result.MembershipExpiresOn);
        }

        [Fact]
        public void Evaluate_NullMembershipExpiryDoesNotBlockLegacyReader()
        {
            var result = CreateService().Evaluate(
                new Reader { ReaderId = 1, Status = "Active", MembershipExpiresOn = null },
                Array.Empty<BorrowRecord>());
            Assert.True(result.IsEligible);
        }

        [Fact]
        public void Evaluate_ReportsMembershipOverdueAndFinancialReasonsTogether()
        {
            var today = new DateTime(2026, 10, 2);
            var reader = new Reader { ReaderId = 1, Status = "Active", MembershipExpiresOn = today.AddDays(-1) };
            var loans = new[] { new BorrowRecord { ReaderId = 1, Status = "Borrowing", DueDate = today.AddDays(-1) } };
            var standing = new ReaderFinancialStanding { OutstandingAmount = 30000m, BlocksBorrowing = true };

            var result = CreateService().Evaluate(reader, loans, today, standing);

            Assert.False(result.IsEligible);
            Assert.Contains(result.Reasons, reason => reason.Contains("hết hạn"));
            Assert.Contains(result.Reasons, reason => reason.Contains("quá hạn"));
            Assert.Contains(result.Reasons, reason => reason.Contains("30"));
            Assert.Equal(30000m, result.OutstandingAmount);
        }

        [Fact]
        public void CheckEligibility_UsesFinancialStandingProviderWithoutCalculatingFees()
        {
            var readers = new Mock<ReaderRepository>();
            var loans = new Mock<BorrowRepository>();
            var financial = new Mock<IReaderFinancialStandingProvider>();
            readers.Setup(repository => repository.GetById(1)).Returns(new Reader { ReaderId = 1, Status = "Active" });
            loans.Setup(repository => repository.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());
            financial.Setup(provider => provider.GetStanding(1)).Returns(
                new ReaderFinancialStanding { OutstandingAmount = 50000m, BlocksBorrowing = true });
            var service = new ReaderEligibilityService(readers.Object, loans.Object, financial.Object);

            Assert.False(service.CheckEligibility(1).IsEligible);
            financial.Setup(provider => provider.GetStanding(1)).Returns(
                new ReaderFinancialStanding { OutstandingAmount = 50000m, BlocksBorrowing = false });
            Assert.True(service.CheckEligibility(1).IsEligible);
        }

        [Fact]
        public void CheckEligibility_ProviderFailureDoesNotTreatStandingAsClear()
        {
            var readers = new Mock<ReaderRepository>();
            var loans = new Mock<BorrowRepository>();
            var financial = new Mock<IReaderFinancialStandingProvider>();
            readers.Setup(repository => repository.GetById(1)).Returns(new Reader { ReaderId = 1, Status = "Active" });
            loans.Setup(repository => repository.GetEligibilityRecords(1)).Returns(new List<BorrowRecord>());
            financial.Setup(provider => provider.GetStanding(1)).Throws(new InvalidOperationException("Fee unavailable"));
            var service = new ReaderEligibilityService(readers.Object, loans.Object, financial.Object);
            Assert.Throws<InvalidOperationException>(() => service.CheckEligibility(1));
        }

        private static ReaderEligibilityService CreateService()
        {
            return new ReaderEligibilityService(
                new Mock<ReaderRepository>().Object,
                new Mock<BorrowRepository>().Object);
        }
    }
}
