namespace LibraryManagement.Models;

/// <summary>One source of truth for language codes shown throughout the Book UI.</summary>
public static class LanguageCatalog
{
    public const string AllFilterCode = "All";
    public const string UnknownFilterCode = "__unknown__";

    private static readonly LanguageOption[] KnownLanguages =
    [
        new("en", "English"),
        new("vi", "Vietnamese"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("zh", "Chinese"),
        new("fr", "French"),
        new("de", "German")
    ];

    public static IReadOnlyList<LanguageOption> Options { get; } = Array.AsReadOnly(KnownLanguages);
    public static IReadOnlyList<LanguageOption> BookOptions { get; } = Array.AsReadOnly(
        new[] { new LanguageOption(string.Empty, "Unknown") }.Concat(KnownLanguages).ToArray());
    public static IReadOnlyList<LanguageOption> FilterOptions { get; } = Array.AsReadOnly(
        new[]
        {
            new LanguageOption(AllFilterCode, "All"),
            new LanguageOption(UnknownFilterCode, "Unknown")
        }.Concat(KnownLanguages).ToArray());

    public static string GetDisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Unknown";
        string normalized = code.Trim();
        return KnownLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? normalized;
    }

    public static string? ResolveCode(LanguageOption? selectedOption, string? enteredText)
    {
        if (selectedOption is not null)
            return string.IsNullOrWhiteSpace(selectedOption.Code) ? null : selectedOption.Code;
        if (string.IsNullOrWhiteSpace(enteredText)) return null;

        string entered = enteredText.Trim();
        var known = KnownLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, entered, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language.DisplayName, entered, StringComparison.OrdinalIgnoreCase));
        return known?.Code ?? entered;
    }

    public static IReadOnlyList<LanguageOption> GetBookOptions(string? currentCode)
    {
        if (string.IsNullOrWhiteSpace(currentCode) || KnownLanguages.Any(language =>
                string.Equals(language.Code, currentCode.Trim(), StringComparison.OrdinalIgnoreCase)))
            return BookOptions;

        return Array.AsReadOnly(BookOptions.Concat(new[]
        {
            new LanguageOption(currentCode.Trim(), currentCode.Trim())
        }).ToArray());
    }
}
