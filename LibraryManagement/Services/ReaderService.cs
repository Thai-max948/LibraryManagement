using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibraryManagement.Models;
using LibraryManagement.Repositories;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Services
{
    public class ReaderService
    {
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^[0-9]{9,11}$", RegexOptions.Compiled);

        private readonly ReaderRepository _readerRepo;
        private readonly BorrowRepository _borrowRepo;
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
            _eligibilityService = new ReaderEligibilityService(readerRepo, borrowRepo);
        }

        public int AddReader(Reader reader)
        {
            Validate(reader, preserveOtherIdentifiers: false);
            reader.Status = "Active";
            reader.SuspensionReason = string.Empty;
            reader.SuspendedDate = null;
            EnsureIdentificationIsUnique(reader);
            int id;
            try
            {
                id = _readerRepo.Add(reader);
            }
            catch (SqlException ex) when (IsLecturerCodeUniqueViolation(ex))
            {
                throw new BusinessRuleException("Mã giảng viên đã được sử dụng bởi độc giả khác.");
            }
            NotificationEvents.PublishAfterSuccess(new(BusinessAction.ReaderCreated, id.ToString()));
            return id;
        }

        public void UpdateReader(Reader reader)
        {
            Validate(reader, preserveOtherIdentifiers: true);

            var existing = _readerRepo.GetById(reader.ReaderId);
            if (existing == null || existing.IsDeleted)
            {
                throw new BusinessRuleException("Độc giả không tồn tại.");
            }

            EnsureIdentificationIsUnique(reader);
            ValidateLifecycleTransition(reader, existing);
            ApplyStatusAudit(reader, existing);

            bool updated;
            try
            {
                updated = _readerRepo.Update(reader);
            }
            catch (SqlException ex) when (IsLecturerCodeUniqueViolation(ex))
            {
                throw new BusinessRuleException("Mã giảng viên đã được sử dụng bởi độc giả khác.");
            }

            if (!updated)
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

            bool hasActiveBorrow = _borrowRepo.HasActiveBorrowByReader(readerId);
            if (hasActiveBorrow)
            {
                throw new BusinessRuleException("Không thể xóa độc giả đang có sách chưa trả.");
            }

            bool hasHistory = _borrowRepo.HasBorrowHistoryByReader(readerId);
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

            if (_borrowRepo.HasActiveBorrowByReader(readerId))
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

        public List<Reader> SearchForBorrow(string? query, int limit = 10)
        {
            string normalizedQuery = query?.Trim() ?? string.Empty;
            if (normalizedQuery.Length == 0) return new List<Reader>();

            bool isExactNumericIdentifier = int.TryParse(normalizedQuery, out _);
            if (normalizedQuery.Length < 2 && !isExactNumericIdentifier)
                return new List<Reader>();

            return _readerRepo.SearchForBorrow(normalizedQuery, Math.Clamp(limit, 1, 20));
        }

        public BorrowRecommendationPage<Reader> SearchForBorrow(
            string? query, int pageNumber, int pageSize)
        {
            string normalizedQuery = query?.Trim() ?? string.Empty;
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 20);

            bool isExactNumericIdentifier = int.TryParse(normalizedQuery, out _);
            if (normalizedQuery.Length == 1 && !isExactNumericIdentifier)
                return new BorrowRecommendationPage<Reader>(Array.Empty<Reader>(), false);

            return _readerRepo.SearchForBorrow(normalizedQuery, pageNumber, pageSize);
        }

        public List<Reader> GetAllReaders(bool includeDeleted = false)
        {
            return _readerRepo.GetAll(includeDeleted);
        }

        public ReaderPage GetReaderPage(string keyword, string typeFilter, string statusFilter,
            string sortBy, int pageNumber, int pageSize)
        {
            var query = new ReaderPageQuery(keyword, typeFilter, statusFilter, sortBy, pageNumber, pageSize);
            return _readerRepo.GetPage(query);
        }

        public ReaderProfile GetReaderProfile(int readerId)
        {
            var reader = _readerRepo.GetById(readerId);
            if (reader == null || reader.IsDeleted)
                throw new BusinessRuleException("Độc giả không tồn tại.");

            var borrowData = _borrowRepo.GetReaderProfileBorrowData(readerId, DateTime.Today, 5);
            return new ReaderProfile
            {
                Reader = reader,
                Eligibility = _eligibilityService.Evaluate(reader, borrowData.EligibilityRecords),
                BorrowingHistory = borrowData.RecentHistory,
                CurrentlyBorrowing = borrowData.CurrentlyBorrowing,
                TotalBorrowed = borrowData.TotalBorrowed,
                OverdueCount = borrowData.OverdueCount
            };
        }

        private void EnsureIdentificationIsUnique(Reader reader)
        {
            string identification = reader.IsExternal
                ? reader.IdentityNumber!
                : reader.IsLecturer ? reader.LecturerCode! : reader.StudentId!;
            if (_readerRepo.IdentificationExists(reader.ReaderType, identification, reader.ReaderId))
            {
                string label = reader.IdentificationLabel;
                throw new BusinessRuleException($"{label} đã được sử dụng bởi độc giả khác.");
            }
        }

        private static bool IsLecturerCodeUniqueViolation(SqlException exception) =>
            (exception.Number is 2601 or 2627)
            && exception.Message.Contains("UX_Readers_LecturerCode_Active", StringComparison.OrdinalIgnoreCase);

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
                && _borrowRepo.HasActiveBorrowByReader(reader.ReaderId))
                throw new BusinessRuleException("Không thể chuyển Inactive khi độc giả còn sách chưa trả.");
        }

        private static void Validate(Reader reader, bool preserveOtherIdentifiers)
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

            if (!reader.IsStudent && !reader.IsLecturer && !reader.IsExternal)
            {
                throw new BusinessRuleException("Loại độc giả không hợp lệ.");
            }

            if (reader.IsStudent)
            {
                if (string.IsNullOrWhiteSpace(reader.StudentId))
                    throw new BusinessRuleException("Mã sinh viên không được để trống.");
                reader.ReaderType = "Student";
                reader.StudentId = reader.StudentId.Trim();
                if (!preserveOtherIdentifiers)
                {
                    reader.IdentityNumber = null;
                    reader.LecturerCode = null;
                    reader.Department = null;
                }
            }
            else if (reader.IsExternal)
            {
                if (string.IsNullOrWhiteSpace(reader.IdentityNumber))
                    throw new BusinessRuleException("Số CCCD / Định danh không được để trống.");
                reader.ReaderType = "External";
                reader.IdentityNumber = reader.IdentityNumber.Trim();
                if (!preserveOtherIdentifiers)
                {
                    reader.StudentId = null;
                    reader.LecturerCode = null;
                    reader.Department = null;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(reader.LecturerCode))
                    throw new BusinessRuleException("Mã giảng viên không được để trống.");
                reader.ReaderType = "Lecturer";
                reader.LecturerCode = reader.LecturerCode.Trim();
                reader.Department = string.IsNullOrWhiteSpace(reader.Department) ? null : reader.Department.Trim();
                if (!preserveOtherIdentifiers)
                {
                    reader.StudentId = null;
                    reader.IdentityNumber = null;
                }
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
