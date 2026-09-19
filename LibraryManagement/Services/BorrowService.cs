using System;
using System.Collections.Generic;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class BorrowService
    {
        private const int MaxActiveBorrowsPerReader = 3;
        private const string StatusBorrowing = "Borrowing";

        private readonly BookRepository _bookRepo;
        private readonly BorrowRepository _borrowRepo;

        public BorrowService()
        {
            _bookRepo = new BookRepository();
            _borrowRepo = new BorrowRepository();
        }

        public bool CanBorrow(int readerId, int bookId, out string reason)
        {
            var book = _bookRepo.GetById(bookId);
            if (book == null)
            {
                reason = "Sách không tồn tại.";
                return false;
            }
            if (book.AvailableQuantity <= 0)
            {
                reason = "Sách đã hết, không thể mượn.";
                return false;
            }

            int count = 0;
            foreach (var r in _borrowRepo.GetBorrowingRecords())
            {
                if (r.ReaderId == readerId)
                {
                    count++;
                }
            }

            if (count >= MaxActiveBorrowsPerReader)
            {
                reason = $"Độc giả đã mượn tối đa {MaxActiveBorrowsPerReader} sách.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public int BorrowBook(int readerId, int bookId, DateTime borrowDate, DateTime dueDate)
        {
            if (dueDate <= borrowDate)
            {
                throw new BusinessRuleException("Ngày hẹn trả phải sau ngày mượn.");
            }

            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                var book = _bookRepo.GetById(conn, tran, bookId);
                if (book == null)
                {
                    throw new BusinessRuleException("Sách không tồn tại.");
                }
                if (book.AvailableQuantity <= 0)
                {
                    throw new BusinessRuleException("Sách đã hết, không thể mượn.");
                }

                int activeCount = _borrowRepo.CountActiveBorrowsByReader(conn, tran, readerId);
                if (activeCount >= MaxActiveBorrowsPerReader)
                {
                    throw new BusinessRuleException($"Độc giả đã mượn tối đa {MaxActiveBorrowsPerReader} sách.");
                }

                var actualBorrowDate = (borrowDate.Date == DateTime.Today && borrowDate.TimeOfDay == TimeSpan.Zero)
                    ? DateTime.Now
                    : borrowDate;

                var record = new BorrowRecord
                {
                    BookId = bookId,
                    ReaderId = readerId,
                    BorrowDate = actualBorrowDate,
                    DueDate = dueDate,
                    ReturnDate = null,
                    Status = StatusBorrowing
                };
                int borrowId = _borrowRepo.Add(conn, tran, record);

                if (!_bookRepo.UpdateAvailableQuantity(conn, tran, bookId, -1))
                {
                    throw new BusinessRuleException("Sách vừa hết trong lúc xử lý, vui lòng thử lại.");
                }

                tran.Commit();
                return borrowId;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public void ReturnBook(int borrowId, DateTime returnDate)
        {
            using var conn = Database.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();
            try
            {
                var record = _borrowRepo.GetById(conn, tran, borrowId);
                if (record == null)
                {
                    throw new BusinessRuleException("Bản ghi mượn không tồn tại.");
                }

                if (!_borrowRepo.MarkAsReturned(conn, tran, borrowId, returnDate))
                {
                    throw new BusinessRuleException("Sách này đã được trả trước đó.");
                }

                if (!_bookRepo.UpdateAvailableQuantity(conn, tran, record.BookId, +1))
                {
                    throw new BusinessRuleException("Cập nhật số lượng sách thất bại.");
                }

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public List<BorrowRecord> GetBorrowingBooks() => _borrowRepo.GetBorrowingRecords();

        public List<BorrowRecord> GetHistory(
            int? readerId = null,
            int? bookId = null,
            string? status = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            return _borrowRepo.GetHistory(readerId, bookId, status, fromDate, toDate);
        }
    }
}