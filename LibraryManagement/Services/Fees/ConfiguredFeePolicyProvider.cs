using System.IO;
using System.Text.Json;
using LibraryManagement.Models;

namespace LibraryManagement.Services;

// Keep fee rates in application configuration; never duplicate Book-owned price data here.
public sealed class ConfiguredFeePolicyProvider : IFeePolicyProvider
{
    private readonly string _path;
    public ConfiguredFeePolicyProvider() : this(Path.Combine(AppContext.BaseDirectory, "appsettings.json")) { }
    public ConfiguredFeePolicyProvider(string path) => _path = path;

    public FeePolicy GetCurrent()
    {
        if (!File.Exists(_path)) return new FeePolicy();
        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        if (!document.RootElement.TryGetProperty("FeePolicy", out var section)) return new FeePolicy();
        return new FeePolicy(
            ReadDecimal(section, "LateFeeRatePerDay"), ReadDecimal(section, "MaxLateFeePercent"),
            ReadDecimal(section, "MinorDamageRate"), ReadDecimal(section, "MajorDamageRate"),
            ReadDecimal(section, "ReplacementRate"));
    }

    private static decimal? ReadDecimal(JsonElement section, string name) =>
        section.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
}

public sealed class FeePolicyNotConfiguredException(string message) : BusinessRuleException(message);
