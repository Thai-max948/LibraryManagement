using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services
{
    public class BookService
    {
        private readonly BookRepository _bookRepo;
        private readonly BorrowRepository _borrowRepo;
        private readonly BookPricingPolicy _pricingPolicy;

        public BookService() : this(new BookRepository(), new BorrowRepository(), BookPricingPolicy.Default)
        {
        }

        public BookService(BookRepository bookRepo, BorrowRepository borrowRepo)
            : this(bookRepo, borrowRepo, BookPricingPolicy.Default)
        {
        }

        public BookService(BookRepository bookRepo, BorrowRepository borrowRepo, BookPricingPolicy pricingPolicy)
        {
            _bookRepo = bookRepo;
            _borrowRepo = borrowRepo;
            _pricingPolicy = pricingPolicy ?? throw new ArgumentNullException(nameof(pricingPolicy));
        }

        public BookPricingPolicy PricingPolicy => _pricingPolicy;

        public int AddBook(Book book)
        {
            Validate(book);
            book.RentalPrice = _pricingPolicy.CalculateRentalPrice(book.ReplacementValue);
            RejectDuplicateIsbn(book);
            book.AvailableQuantity = book.Quantity;
            try
            {
                int id = _bookRepo.AddWithCopies(book, book.Quantity);
                NotificationEvents.PublishAfterSuccess(new(BusinessAction.BookCreated, id.ToString(), book.Title));
                return id;
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627 && book.Isbn != null)
            {
                var existing = _bookRepo.GetByIsbn(book.Isbn);
                if (existing != null) throw new DuplicateBookIsbnException(existing.BookId);
                throw;
            }
        }

        public void UpdateBook(Book book)
        {
            Validate(book);

            var existing = _bookRepo.GetById(book.BookId);
            if (existing == null)
            {
                throw new BusinessRuleException("Sách không tồn tại.");
            }
            if (existing.Status == BookStatuses.Archived)
                throw new BusinessRuleException("Cần khôi phục đầu sách trước khi chỉnh sửa.");

            // Older callers may omit BookPrice; preserve the source value and derive its rental price.
            book.ReplacementValue ??= existing.ReplacementValue;
            book.RentalPrice = _pricingPolicy.CalculateRentalPrice(book.ReplacementValue);

            RejectDuplicateIsbn(book);

            var activeLoans = _borrowRepo.GetBorrowingRecords();
            int borrowing = activeLoans == null
                ? existing.Quantity - existing.AvailableQuantity
                : activeLoans.Count(record => record.BookId == book.BookId);
            if (book.Quantity < borrowing)
            {
                throw new BusinessRuleException($"Không thể đặt Quantity nhỏ hơn số đang được mượn ({borrowing}).");
            }

            book.AvailableQuantity = book.Quantity - borrowing;

            bool updated;
            try { updated = _bookRepo.UpdateWithCopies(book); }
            catch (SqlException exception) when (exception.Number is 2601 or 2627 && book.Isbn != null)
            {
                var duplicate = _bookRepo.GetByIsbn(book.Isbn);
                if (duplicate != null && duplicate.BookId != book.BookId)
                    throw new DuplicateBookIsbnException(duplicate.BookId);
                throw;
            }
            if (!updated)
            {
                throw new BusinessRuleException("Cập nhật sách thất bại.");
            }
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.BookUpdated, book.BookId.ToString(), book.Title));
        }

        public void DeleteBook(int bookId)
            => ArchiveBook(bookId);

        public void ArchiveBook(int bookId)
        {
            if (!_bookRepo.SetArchived(bookId, true))
            {
                throw new BusinessRuleException("Sách không tồn tại.");
            }
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.BookArchived, bookId.ToString()));
        }

        public void RestoreBook(int bookId)
        {
            if (!_bookRepo.SetArchived(bookId, false))
                throw new BusinessRuleException("Sách không tồn tại.");
        }

        public List<Book> SearchBook(string keyword)
        {
            return string.IsNullOrWhiteSpace(keyword) ? _bookRepo.GetAll() : _bookRepo.Search(keyword);
        }

        public List<Book> GetAllBooks()
        {
            return _bookRepo.GetAll();
        }

        public Book? GetBookById(int bookId) => _bookRepo.GetById(bookId);

        public List<Book> GetArchivedBooks() => _bookRepo.GetArchived();
        public List<Book> GetAllBooksIncludingArchived() => _bookRepo.GetAllIncludingArchived();
        public List<Book> SearchArchivedBooks(string keyword) =>
            string.IsNullOrWhiteSpace(keyword) ? _bookRepo.GetArchived() : _bookRepo.SearchArchived(keyword);

        private static void Validate(Book book)
        {
            if (book.ReplacementValue is decimal value &&
                (value < 0m || decimal.Round(value, 2) != value || value >= 10000000000000000m))
                throw new BusinessRuleException("Giá trị thay thế phải không âm và có tối đa hai chữ số thập phân.");
            book.Isbn = Isbn.Normalize(book.Isbn);
            book.Publisher = string.IsNullOrWhiteSpace(book.Publisher) ? null : book.Publisher.Trim();
            book.Language = string.IsNullOrWhiteSpace(book.Language) ? null : book.Language.Trim().ToLowerInvariant();
            if (book.Publisher?.Length > 150)
                throw new BusinessRuleException("Nhà xuất bản không được vượt quá 150 ký tự.");
            if (book.Language?.Length > 50)
                throw new BusinessRuleException("Mã ngôn ngữ không được vượt quá 50 ký tự.");
            if (string.IsNullOrWhiteSpace(book.Title))
            {
                throw new BusinessRuleException("Tiêu đề sách không được để trống.");
            }
            if (string.IsNullOrWhiteSpace(book.Author))
            {
                throw new BusinessRuleException("Tác giả không được để trống.");
            }
            if (book.PublishYear <= 0)
            {
                throw new BusinessRuleException("Năm xuất bản không hợp lệ.");
            }
            if (book.Quantity < 0)
            {
                throw new BusinessRuleException("Số lượng không được âm.");
            }
        }

        private void RejectDuplicateIsbn(Book book)
        {
            if (book.Isbn == null) return;
            var existing = _bookRepo.GetByIsbn(book.Isbn);
            if (existing != null && existing.BookId != book.BookId)
                throw new DuplicateBookIsbnException(existing.BookId);
        }
    }
}
