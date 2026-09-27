using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibraryManagement.Models;
using LibraryManagement.Repositories;

namespace LibraryManagement.Services
{
    public class ReaderService
    {
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^[0-9]{9,11}$", RegexOptions.Compiled);

        private readonly ReaderRepository _readerRepo;
        private readonly BorrowRepository _borrowRepo;

        public ReaderService() : this(new ReaderRepository(), new BorrowRepository())
        {
        }

        public ReaderService(ReaderRepository readerRepo, BorrowRepository borrowRepo)
        {
            _readerRepo = readerRepo;
            _borrowRepo = borrowRepo;
        }

        public int AddReader(Reader reader)
        {
            Validate(reader);
            return _readerRepo.Add(reader);
        }

        public void UpdateReader(Reader reader)
        {
            Validate(reader);

            var existing = _readerRepo.GetById(reader.ReaderId);
            if (existing == null || existing.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            if (!_readerRepo.Update(reader))
            {
                throw new BusinessRuleException("Cập nhật độc giả thất bại.");
            }
        }

        public void DeleteReader(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            bool hasActiveBorrow = _borrowRepo.GetBorrowingRecords().Any(r => r.ReaderId == readerId);
            if (hasActiveBorrow)
            {
                throw new BusinessRuleException("Không thể xóa độc giả đang có sách chưa trả.");
            }

            if (!_readerRepo.Delete(readerId))
            {
                throw new BusinessRuleException("Độc giả không tồn tại hoặc xóa thất bại.");
            }
        }

        public void ToggleStatus(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            reader.Status = string.Equals(reader.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                ? "Active"
                : "Suspended";

            if (!_readerRepo.Update(reader))
            {
                throw new BusinessRuleException("Cập nhật trạng thái độc giả thất bại.");
            }
        }

        public Reader? GetReaderById(int readerId)
        {
            return _readerRepo.GetById(readerId);
        }

        public List<Reader> SearchReader(string keyword)
        {
            return SearchReader(keyword, "All", "All");
        }

        public List<Reader> SearchReader(string keyword, string typeFilter, string statusFilter)
        {
            bool isTypeAll = string.IsNullOrWhiteSpace(typeFilter) || string.Equals(typeFilter, "All", StringComparison.OrdinalIgnoreCase);
            bool isStatusAll = string.IsNullOrWhiteSpace(statusFilter) || string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(keyword) && isTypeAll && isStatusAll)
            {
                return _readerRepo.GetAll();
            }

            if (isTypeAll && isStatusAll)
            {
                return _readerRepo.Search(keyword);
            }

            return _readerRepo.Search(keyword, typeFilter, statusFilter);
        }

        public List<Reader> GetAllReaders(bool includeDeleted = false)
        {
            return _readerRepo.GetAll(includeDeleted);
        }

        private static void Validate(Reader reader)
        {
            if (string.IsNullOrWhiteSpace(reader.FullName))
            {
                throw new BusinessRuleException("Họ tên không được để trống.");
            }
            if (string.IsNullOrWhiteSpace(reader.Phone))
            {
                throw new BusinessRuleException("thiếu thông tin sđt");
            }
            if (string.IsNullOrWhiteSpace(reader.Email))
            {
                throw new BusinessRuleException("thiếu thông tin email");
            }
            if (!EmailRegex.IsMatch(reader.Email))
            {
                throw new BusinessRuleException("Email không hợp lệ.");
            }
            if (!PhoneRegex.IsMatch(reader.Phone))
            {
                throw new BusinessRuleException("Số điện thoại không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(reader.ReaderType))
            {
                reader.ReaderType = "Student";
            }

            if (string.IsNullOrWhiteSpace(reader.Status))
            {
                reader.Status = "Active";
            }

            if (reader.RegistrationDate == default)
            {
                reader.RegistrationDate = DateTime.Now;
            }
        }
    }
}