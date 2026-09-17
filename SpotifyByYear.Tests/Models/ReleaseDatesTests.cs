using SpotifyByYear.Models;

namespace SpotifyByYear.Tests.Models;

public class ReleaseDatesTests
{
    [Theory]
    [InlineData("1976", 1976)]
    [InlineData("1976-03", 1976)]
    [InlineData("1976-03-01", 1976)]
    [InlineData("0000", null)]
    [InlineData("0000-00-00", null)]
    [InlineData("197", null)]
    [InlineData("abcd", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseYear_takes_the_first_four_digits(string? date, int? expected) =>
        Assert.Equal(expected, ReleaseDates.ParseYear(date));
}
