using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.TestSupport;

public sealed class FakeMusicBrainzClient : IMusicBrainzClient
{
    public Dictionary<string, List<MusicBrainzRecording>> ByIsrc { get; } = new();

    public List<MusicBrainzRecording> SearchResults { get; set; } = [];

    /// <summary>"isrc:&lt;isrc&gt;" and "search:&lt;title&gt;|&lt;artist&gt;" in call order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Thrown by the next call only.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    public static MusicBrainzRecording Recording(
        string firstReleaseDate,
        string title = "Rich Girl",
        string artist = "Daryl Hall & John Oates",
        string? disambiguation = null,
        int score = 100,
        string? id = null) =>
        new(id ?? $"rec-{firstReleaseDate}-{disambiguation}", title, artist, firstReleaseDate, disambiguation, score);

    public Task<IReadOnlyList<MusicBrainzRecording>> LookupByIsrcAsync(string isrc, CancellationToken cancellationToken)
    {
        Calls.Add($"isrc:{isrc}");
        ThrowIfRequested();
        return Task.FromResult<IReadOnlyList<MusicBrainzRecording>>(ByIsrc.GetValueOrDefault(isrc) ?? []);
    }

    public Task<IReadOnlyList<MusicBrainzRecording>> SearchRecordingsAsync(string title, string artist, CancellationToken cancellationToken)
    {
        Calls.Add($"search:{title}|{artist}");
        ThrowIfRequested();
        return Task.FromResult<IReadOnlyList<MusicBrainzRecording>>(SearchResults);
    }

    private void ThrowIfRequested()
    {
        if (ThrowOnNextCall is { } exception)
        {
            ThrowOnNextCall = null;
            throw exception;
        }
    }
}
