using LibraryManagement.Models;
using LibraryManagement.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LibraryManagement.Services
{
    public class ReaderService
    {
        private readonly ReaderRepository _readerRepo;
        private readonly BorrowRepository _borrowRepo;

        public ReaderService()
        {
            _readerRepo = new ReaderRepository();
            _borrowRepo = new BorrowRepository();
        }

        public int AddReader(Reader reader)
        {
            Validate(reader);
            return _readerRepo.Add(reader);
        }

        public void UpdateReader(Reader reader)
        {
            Validate(reader);

            if (_readerRepo.GetById(reader.ReaderId) == null)
                throw new BusinessRuleException("Độc giả không tồn tại.");

            if (!_readerRepo.Update(reader))
                throw new BusinessRuleException("Cập nhật độc giả thất bại.");
        }

        public void DeleteReader(int readerId)
        {
            bool hasActiveBorrow = _borrowRepo.GetBorrowingRecords().Any(r => r.ReaderId == readerId);
            if (hasActiveBorrow)
                throw new BusinessRuleException("Không thể xóa độc giả đang có sách chưa trả.");

            if (!_readerRepo.Delete(readerId))
                throw new BusinessRuleException("Độc giả không tồn tại.");
        }

        public List<Reader> SearchReader(string keyword)
            => string.IsNullOrWhiteSpace(keyword) ? _readerRepo.GetAll() : _readerRepo.Search(keyword);

        public List<Reader> GetAllReaders() => _readerRepo.GetAll();

        private void Validate(Reader reader)
        {
            if (string.IsNullOrWhiteSpace(reader.FullName))
                throw new BusinessRuleException("Họ tên không được để trống.");
            if (string.IsNullOrWhiteSpace(reader.Phone))
                throw new BusinessRuleException("thiếu thông tin sđt");
            if (string.IsNullOrWhiteSpace(reader.Email))
                throw new BusinessRuleException("thiếu thông tin email");
            if (!string.IsNullOrWhiteSpace(reader.Email) && !Regex.IsMatch(reader.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                throw new BusinessRuleException("Email không hợp lệ.");
            if (!string.IsNullOrWhiteSpace(reader.Phone) && !Regex.IsMatch(reader.Phone, @"^[0-9]{9,11}$"))
                throw new BusinessRuleException("Số điện thoại không hợp lệ.");
        }
    }
}