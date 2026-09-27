using System;
using System.Globalization;
using System.Text;

namespace LibraryManagement.Helpers
{
    public static class StringHelper
    {
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            string clean = stringBuilder.ToString().Normalize(NormalizationForm.FormC);
            return clean.Replace('đ', 'd').Replace('Đ', 'D');
        }

        public static bool ContainsNormalized(string? source, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            string cleanSource = RemoveDiacritics(source);
            string cleanQuery = RemoveDiacritics(query);

            return cleanSource.IndexOf(cleanQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
