using System.Collections.Generic;

namespace LibraryManagement.Models;

public sealed record BorrowRecommendationPage<T>(IReadOnlyList<T> Items, bool HasMore);
