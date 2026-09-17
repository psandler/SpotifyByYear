using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.Services;

public class TrackMatchingTests
{
    [Theory]
    [InlineData("Rich Girl - 2003 Remaster", "Rich Girl")]
    [InlineData("Hey Jude - Remastered 2015", "Hey Jude")]
    [InlineData("America - Single Mix", "America")]
    [InlineData("Heroes (Live at Wembley)", "Heroes")]
    [InlineData("All Too Well (Taylor's Version)", "All Too Well")]
    [InlineData("Part 1 - Part 2 - 2011 Mix", "Part 1 - Part 2")]
    [InlineData("Semi-Charmed Life", "Semi-Charmed Life")]
    [InlineData("(I Can't Get No) Satisfaction", "(I Can't Get No) Satisfaction")]
    [InlineData("(Live)", "(Live)")] // never cleans a title down to nothing
    public void CleanTitle_removes_version_text(string title, string expected) =>
        Assert.Equal(expected, TrackMatching.CleanTitle(title));

    [Theory]
    [InlineData("Beyoncé & Jay-Z", "beyonce and jay z")]
    [InlineData("Don’t Stop", "dont stop")]
    [InlineData("  AC/DC!! ", "ac dc")]
    public void Normalize_lowercases_and_strips_accents_and_punctuation(string value, string expected) =>
        Assert.Equal(expected, TrackMatching.Normalize(value));

    [Theory]
    [InlineData("Don't Stop Me Now - 2011 Mix", "Don’t Stop Me Now", true)]
    [InlineData("Beyoncé", "Beyonce", true)]
    [InlineData("Rich Girl", "Rich Man", false)]
    [InlineData("", "", false)]
    public void TitlesMatch_ignores_version_text_accents_and_apostrophes(string a, string b, bool expected) =>
        Assert.Equal(expected, TrackMatching.TitlesMatch(a, b));

    [Theory]
    [InlineData("Valerie - Live At BBC Radio 1", "Valerie - Live At BBC Radio 1", true)]
    [InlineData("Valerie - Live At BBC Radio 1", "Valerie", false)]
    public void TitlesMatchExactly_keeps_version_text(string a, string b, bool expected) =>
        Assert.Equal(expected, TrackMatching.TitlesMatchExactly(a, b));

    [Theory]
    [InlineData("The Beatles", "Beatles", true)]
    [InlineData("Daryl Hall & John Oates", "Daryl Hall and John Oates", true)]
    [InlineData("U2", "U2", true)]
    [InlineData("U2", "U2 Tribute Band", false)] // short names must match exactly
    [InlineData("ABBA", "ABC", false)]
    [InlineData(null, "Queen", false)]
    [InlineData("Queen", "", false)]
    public void ArtistsMatch(string? a, string? b, bool expected) =>
        Assert.Equal(expected, TrackMatching.ArtistsMatch(a, b));

    [Theory]
    [InlineData("live, 2008-05-22/23: The Troubadour, Los Angeles, CA", true)]
    [InlineData("demo", true)]
    [InlineData("instrumental", true)]
    [InlineData("2003 remaster", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAlternateVersion(string? disambiguation, bool expected) =>
        Assert.Equal(expected, TrackMatching.IsAlternateVersion(disambiguation));

    [Theory]
    [InlineData("Valerie - Live At BBC Radio 1 Live Lounge, London / 2007", true, 2007)]
    [InlineData("Heroes (Live at Wembley 1986)", true, 1986)]
    [InlineData("Song - Live", true, null)]
    [InlineData("Live and Let Die", false, null)]
    [InlineData("Alive", false, null)]
    [InlineData("Live Forever - Remastered", false, null)]
    [InlineData("All Too Well (Taylor's Version)", false, null)]
    [InlineData("Blackbird - Remastered 2009", false, null)] // a remaster year is not a live year
    public void Live_versions_and_their_title_year(string title, bool isLive, int? liveYear)
    {
        Assert.Equal(isLive, TrackMatching.IsLiveVersion(title));
        Assert.Equal(liveYear, TrackMatching.LiveYearFromTitle(title));
    }

    [Theory]
    [InlineData("live, 2007: BBC", true)]
    [InlineData("live", true)]
    [InlineData("alive and well", false)]
    [InlineData(null, false)]
    public void IsLiveDisambiguation(string? disambiguation, bool expected) =>
        Assert.Equal(expected, TrackMatching.IsLiveDisambiguation(disambiguation));
}
