using LibraryManagement.Services;

namespace LibraryManagement.Models;

public static class Isbn
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string digits = new(value.Where(character => character != '-' && !char.IsWhiteSpace(character)).ToArray());
        if (digits.Length == 10 && IsValid10(digits))
        {
            string prefix = "978" + digits[..9];
            return prefix + CheckDigit13(prefix);
        }
        if (digits.Length == 13 && digits.All(char.IsAsciiDigit) &&
            digits[12] - '0' == CheckDigit13(digits[..12])) return digits;
        throw new BusinessRuleException("ISBN không hợp lệ. Hãy kiểm tra 10/13 ký tự và chữ số kiểm tra.");
    }

    private static bool IsValid10(string value)
    {
        if (!value[..9].All(char.IsAsciiDigit)) return false;
        int last = value[9] is 'X' or 'x' ? 10 : char.IsAsciiDigit(value[9]) ? value[9] - '0' : -1;
        if (last < 0) return false;
        int sum = last;
        for (int index = 0; index < 9; index++) sum += (value[index] - '0') * (10 - index);
        return sum % 11 == 0;
    }

    private static int CheckDigit13(string firstTwelve)
    {
        int sum = 0;
        for (int index = 0; index < 12; index++) sum += (firstTwelve[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }
}
