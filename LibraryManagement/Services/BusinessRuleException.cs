using System;

namespace LibraryManagement.Services
{
    // Exception cho lỗi nghiệp vụ (không phải lỗi hệ thống) -> ViewModel bắt để show message thân thiện.
    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message) { }
    }
}