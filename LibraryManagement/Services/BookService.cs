using System.Collections.Generic;
using System.Linq;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class BookService
    {
        private readonly BookRepository _bookRepo;
        private readonly BorrowRepository _borrowRepo;

        public BookService()
        {
            _bookRepo = new BookRepository();
            _borrowRepo = new BorrowRepository();
        }

        public int AddBook(Book book)
        {
            Validate(book);
            book.AvailableQuantity = book.Quantity;
            return _bookRepo.Add(book);
        }

        public void UpdateBook(Book book)
        {
            Validate(book);

            var existing = _bookRepo.GetById(book.BookId);
            if (existing == null)
            {
                throw new BusinessRuleException("Sách không tồn tại.");
            }

            int borrowing = existing.Quantity - existing.AvailableQuantity;
            if (book.Quantity < borrowing)
            {
                throw new BusinessRuleException($"Không thể đặt Quantity nhỏ hơn số đang được mượn ({borrowing}).");
            }

            book.AvailableQuantity = book.Quantity - borrowing;

            if (!_bookRepo.Update(book))
            {
                throw new BusinessRuleException("Cập nhật sách thất bại.");
            }
        }

        public void DeleteBook(int bookId)
        {
            bool isBorrowing = _borrowRepo.GetBorrowingRecords().Any(r => r.BookId == bookId);
            if (isBorrowing)
            {
                throw new BusinessRuleException("Không thể xóa sách đang được mượn.");
            }

            if (!_bookRepo.Delete(bookId))
            {
                throw new BusinessRuleException("Sách không tồn tại.");
            }
        }

        public List<Book> SearchBook(string keyword)
        {
            return string.IsNullOrWhiteSpace(keyword) ? _bookRepo.GetAll() : _bookRepo.Search(keyword);
        }

        public List<Book> GetAllBooks()
        {
            return _bookRepo.GetAll();
        }

        private static void Validate(Book book)
        {
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
    }
}