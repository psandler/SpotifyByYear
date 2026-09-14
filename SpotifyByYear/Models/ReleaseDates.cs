using System;
using System.Globalization;

namespace SpotifyByYear.Models;

public static class ReleaseDates
{
    /// <summary>Year from a YYYY, YYYY-MM or YYYY-MM-DD date. Null for missing or "0000" dates.</summary>
    public static int? ParseYear(string? date) =>
        date is { Length: >= 4 } &&
        int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
        year > 0
            ? year
            : null;
}
