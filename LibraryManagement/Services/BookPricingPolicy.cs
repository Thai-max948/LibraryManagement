namespace LibraryManagement.Services;

/// <summary>
/// Owns Book pricing rules. The default policy charges five percent of Book.ReplacementValue.
/// </summary>
public sealed class BookPricingPolicy
{
    private const decimal MaxSqlPriceExclusive = 10000000000000000m;

    public static BookPricingPolicy Default { get; } = new(0.05m);

    public decimal RentalRate { get; }

    public BookPricingPolicy(decimal rentalRate)
    {
        if (rentalRate < 0m || rentalRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(rentalRate), "Rental rate must be between 0 and 1.");

        RentalRate = rentalRate;
    }

    public decimal? CalculateRentalPrice(decimal? bookPrice)
        => bookPrice is decimal value ? CalculateRentalPrice(value) : null;

    public decimal CalculateRentalPrice(decimal bookPrice)
    {
        if (bookPrice < 0m || decimal.Round(bookPrice, 2) != bookPrice || bookPrice >= MaxSqlPriceExclusive)
            throw new BusinessRuleException("Book Price phải không âm, có tối đa hai chữ số thập phân và nằm trong giới hạn DECIMAL(18,2).");

        return Math.Round(bookPrice * RentalRate, 2, MidpointRounding.AwayFromZero);
    }
}
