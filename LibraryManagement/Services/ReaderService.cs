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
        private readonly BookRepository _bookRepo;
        private readonly ReaderEligibilityService _eligibilityService;

        public ReaderService() : this(new ReaderRepository(), new BorrowRepository(), new BookRepository())
        {
        }

        public ReaderService(ReaderRepository readerRepo, BorrowRepository borrowRepo)
            : this(readerRepo, borrowRepo, new BookRepository())
        {
        }

        public ReaderService(ReaderRepository readerRepo, BorrowRepository borrowRepo, BookRepository bookRepo)
        {
            _readerRepo = readerRepo;
            _borrowRepo = borrowRepo;
            _bookRepo = bookRepo;
            _eligibilityService = new ReaderEligibilityService(readerRepo, borrowRepo);
        }

        public int AddReader(Reader reader)
        {
            Validate(reader);
            reader.Status = "Active";
            reader.SuspensionReason = string.Empty;
            reader.SuspendedDate = null;
            EnsureIdentificationIsUnique(reader);
            int id = _readerRepo.Add(reader);
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.ReaderCreated, id.ToString()));
            return id;
        }

        public void UpdateReader(Reader reader)
        {
            Validate(reader);

            var existing = _readerRepo.GetById(reader.ReaderId);
            if (existing == null || existing.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            EnsureIdentificationIsUnique(reader);
            ValidateLifecycleTransition(reader, existing);
            ApplyStatusAudit(reader, existing);

            if (!_readerRepo.Update(reader))
            {
                throw new BusinessRuleException("Cập nhật độc giả thất bại.");
            }
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.ReaderUpdated, reader.ReaderId.ToString()));
        }

        public void DeleteReader(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            bool hasActiveBorrow = (_borrowRepo.GetBorrowingRecords() ?? new List<BorrowRecord>())
                .Any(r => r.ReaderId == readerId);
            if (hasActiveBorrow)
            {
                throw new BusinessRuleException("Không thể xóa độc giả đang có sách chưa trả.");
            }

            bool hasHistory = (_borrowRepo.GetHistory(readerId: readerId) ?? new List<BorrowRecord>()).Count > 0;
            if (hasHistory)
            {
                throw new BusinessRuleException("Độc giả đã có lịch sử mượn trả. Hãy chuyển trạng thái sang Inactive thay vì xóa.");
            }

            if (!_readerRepo.Delete(readerId))
            {
                throw new BusinessRuleException("Độc giả không tồn tại hoặc xóa thất bại.");
            }
        }

        public void ToggleStatus(int readerId, string? suspensionReason = null)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            if (reader.IsSuspended || reader.IsInactive)
            {
                ReactivateReader(readerId);
            }
            else
            {
                SuspendReader(readerId, string.IsNullOrWhiteSpace(suspensionReason)
                    ? "Khóa thủ công" : suspensionReason);
            }
        }

        public void SuspendReader(int readerId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new BusinessRuleException("Vui lòng nhập lý do tạm khóa độc giả.");

            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
                throw new BusinessRuleException("Độc giả không tồn tại.");
            if (!reader.IsActive)
                throw new BusinessRuleException($"Không thể tạm khóa độc giả từ trạng thái {reader.Status}.");

            reader.Status = "Suspended";
            reader.SuspensionReason = reason.Trim();
            reader.SuspendedDate = DateTime.Now;

            if (!_readerRepo.Update(reader))
            {
                throw new BusinessRuleException("Cập nhật trạng thái độc giả thất bại.");
            }
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.ReaderSuspended, readerId.ToString()));
        }

        public void DeactivateReader(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
                throw new BusinessRuleException("Độc giả không tồn tại.");

            if (reader.IsInactive)
                return;
            if (!reader.IsActive)
                throw new BusinessRuleException($"Không thể chuyển trạng thái từ {reader.Status} sang Inactive.");

            if ((_borrowRepo.GetBorrowingRecords() ?? new List<BorrowRecord>())
                .Any(record => record.ReaderId == readerId))
                throw new BusinessRuleException("Không thể chuyển Inactive khi độc giả còn sách chưa trả.");

            reader.Status = "Inactive";
            reader.SuspensionReason = string.Empty;
            reader.SuspendedDate = null;
            if (!_readerRepo.Update(reader))
                throw new BusinessRuleException("Cập nhật trạng thái độc giả thất bại.");
        }

        public void ReactivateReader(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
                throw new BusinessRuleException("Độc giả không tồn tại.");

            if (reader.IsActive)
                return;
            if (!reader.IsSuspended && !reader.IsInactive)
                throw new BusinessRuleException($"Không thể kích hoạt độc giả từ trạng thái {reader.Status}.");

            reader.Status = "Active";
            reader.SuspensionReason = string.Empty;
            reader.SuspendedDate = null;
            if (!_readerRepo.Update(reader))
                throw new BusinessRuleException("Cập nhật trạng thái độc giả thất bại.");
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.ReaderActivated, readerId.ToString()));
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

        public ReaderPage GetReaderPage(string keyword, string typeFilter, string statusFilter,
            string sortBy, int pageNumber, int pageSize)
        {
            pageSize = Math.Clamp(pageSize, 5, 100);
            var filtered = SearchReader(keyword, typeFilter, statusFilter);
            IEnumerable<Reader> sorted = sortBy switch
            {
                "Name Z-A" => filtered.OrderByDescending(r => r.FullName, StringComparer.CurrentCultureIgnoreCase),
                "Newest" => filtered.OrderByDescending(r => r.RegistrationDate),
                "Oldest" => filtered.OrderBy(r => r.RegistrationDate),
                "Status" => filtered.OrderBy(r => r.Status).ThenBy(r => r.FullName),
                _ => filtered.OrderBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
            };
            int total = filtered.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            pageNumber = Math.Clamp(pageNumber, 1, totalPages);
            return new ReaderPage
            {
                Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public ReaderProfile GetReaderProfile(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
                throw new BusinessRuleException("Độc giả không tồn tại.");

            var records = _borrowRepo.GetHistory(readerId: readerId);
            var history = records.Take(5).Select(record => new ReaderBorrowHistoryItem
            {
                BorrowId = record.BorrowId,
                BookTitle = _bookRepo.GetById(record.BookId)?.Title ?? $"Book #{record.BookId}",
                BorrowDate = record.BorrowDate,
                DueDate = record.DueDate,
                ReturnDate = record.ReturnDate,
                Status = record.Status
            }).ToList();
            return new ReaderProfile
            {
                Reader = reader,
                Eligibility = _eligibilityService.Evaluate(reader, records),
                BorrowingHistory = history,
                CurrentlyBorrowing = records.Count(r => string.Equals(r.Status, "Borrowing", StringComparison.OrdinalIgnoreCase)),
                TotalBorrowed = records.Count,
                OverdueCount = records.Count(r => string.Equals(r.Status, "Borrowing", StringComparison.OrdinalIgnoreCase)
                    && r.DueDate.Date < DateTime.Today)
            };
        }

        private void EnsureIdentificationIsUnique(Reader reader)
        {
            string identification = reader.IsExternal ? reader.IdentityNumber! : reader.StudentId!;
            if (_readerRepo.IdentificationExists(reader.ReaderType, identification, reader.ReaderId))
            {
                string label = reader.IsExternal ? "Số CCCD / Định danh" : "Mã sinh viên";
                throw new BusinessRuleException($"{label} đã được sử dụng bởi độc giả khác.");
            }
        }

        private static void ApplyStatusAudit(Reader reader, Reader existing)
        {
            if (!reader.IsSuspended)
            {
                reader.SuspensionReason = string.Empty;
                reader.SuspendedDate = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(reader.SuspensionReason))
            {
                if (!existing.IsSuspended)
                    throw new BusinessRuleException("Vui lòng nhập lý do tạm khóa độc giả.");
                reader.SuspensionReason = existing.SuspensionReason;
            }
            reader.SuspensionReason = reader.SuspensionReason.Trim();
            reader.SuspendedDate = existing.IsSuspended && existing.SuspendedDate.HasValue
                ? existing.SuspendedDate : DateTime.Now;
        }

        private void ValidateLifecycleTransition(Reader reader, Reader existing)
        {
            if (string.Equals(reader.Status, existing.Status, StringComparison.OrdinalIgnoreCase))
                return;

            bool transitionAllowed = (existing.IsActive && reader.Status is "Suspended" or "Inactive")
                || (existing.IsSuspended && reader.Status == "Active")
                || (existing.IsInactive && reader.Status == "Active");
            if (!transitionAllowed)
                throw new BusinessRuleException($"Không thể chuyển trạng thái từ {existing.Status} sang {reader.Status}.");

            if (reader.Status == "Inactive"
                && (_borrowRepo.GetBorrowingRecords() ?? new List<BorrowRecord>())
                    .Any(record => record.ReaderId == reader.ReaderId))
                throw new BusinessRuleException("Không thể chuyển Inactive khi độc giả còn sách chưa trả.");
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
            if (!string.IsNullOrWhiteSpace(reader.Email) && !EmailRegex.IsMatch(reader.Email.Trim()))
            {
                throw new BusinessRuleException("Email không hợp lệ.");
            }
            if (!PhoneRegex.IsMatch(reader.Phone))
            {
                throw new BusinessRuleException("Số điện thoại không hợp lệ.");
            }

            if (!string.Equals(reader.ReaderType, "Student", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reader.ReaderType, "External", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("Loại độc giả không hợp lệ.");
            }

            if (string.Equals(reader.ReaderType, "Student", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(reader.StudentId))
                    throw new BusinessRuleException("Mã sinh viên không được để trống.");
                reader.ReaderType = "Student";
                reader.StudentId = reader.StudentId.Trim();
                reader.IdentityNumber = null;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(reader.IdentityNumber))
                    throw new BusinessRuleException("Số CCCD / Định danh không được để trống.");
                reader.ReaderType = "External";
                reader.IdentityNumber = reader.IdentityNumber.Trim();
                reader.StudentId = null;
            }

            if (!string.Equals(reader.Status, "Active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reader.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(reader.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("Trạng thái độc giả không hợp lệ.");

            reader.Status = string.Equals(reader.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                ? "Suspended"
                : string.Equals(reader.Status, "Inactive", StringComparison.OrdinalIgnoreCase) ? "Inactive" : "Active";
            reader.FullName = reader.FullName.Trim();
            reader.Phone = reader.Phone.Trim();
            reader.Email = reader.Email?.Trim() ?? string.Empty;

            if (reader.RegistrationDate == default)
            {
                reader.RegistrationDate = DateTime.Now;
            }
        }
    }
}
