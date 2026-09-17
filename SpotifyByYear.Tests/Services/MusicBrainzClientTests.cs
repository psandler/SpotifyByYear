using System.Diagnostics;
using System.Net;
using System.Text;
using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.Services;

public class MusicBrainzClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Isrc_lookup_parses_recordings()
    {
        var handler = new StubHandler(_ => Json("""
            { "isrc": "USRC19206280", "recordings": [
              { "id": "r1", "title": "Rich Girl", "first-release-date": "1976", "disambiguation": "", "length": 143000, "video": false }
            ] }
            """));
        using var client = Create(handler);

        var recordings = await client.LookupByIsrcAsync("USRC19206280", Ct);

        var recording = Assert.Single(recordings);
        Assert.Equal("r1", recording.Id);
        Assert.Equal("Rich Girl", recording.Title);
        Assert.Equal("1976", recording.FirstReleaseDate);
        Assert.Null(recording.Disambiguation); // empty string becomes null
        Assert.Null(recording.Artist);
        Assert.EndsWith("/ws/2/isrc/USRC19206280?fmt=json", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task Search_parses_score_and_joins_the_artist_credit()
    {
        var handler = new StubHandler(_ => Json("""
            { "recordings": [
              { "id": "r1", "score": 100, "title": "Rich Girl", "first-release-date": "",
                "artist-credit": [ { "name": "Daryl Hall", "joinphrase": " & " }, { "name": "John Oates" } ] },
              { "title": "no id, skipped" }
            ] }
            """));
        using var client = Create(handler);

        var recording = Assert.Single(await client.SearchRecordingsAsync("Rich Girl", "Daryl Hall & John Oates", Ct));

        Assert.Equal("Daryl Hall & John Oates", recording.Artist);
        Assert.Equal(100, recording.Score);
        Assert.Null(recording.FirstReleaseDate);
    }

    [Fact]
    public async Task Search_query_quotes_values_and_asks_for_100_results()
    {
        var handler = new StubHandler(_ => Json("""{ "recordings": [] }"""));
        using var client = Create(handler);

        await client.SearchRecordingsAsync("Say \"Hi\"", @"AC\DC", Ct);

        var query = Uri.UnescapeDataString(handler.Requests.Single().RequestUri!.Query);
        Assert.Contains("""recording:"Say \"Hi\"" AND artist:"AC\\DC" """.TrimEnd(), query);
        Assert.Contains("limit=100", query);
        Assert.Contains("fmt=json", query);
    }

    [Fact]
    public async Task Requests_identify_the_app_with_contact_info()
    {
        var handler = new StubHandler(_ => Json("""{ "recordings": [] }"""));
        using var client = Create(handler);

        await client.LookupByIsrcAsync("X", Ct);

        Assert.True(handler.Requests.Single().Headers.TryGetValues("User-Agent", out var values));
        Assert.Equal(MusicBrainzClient.UserAgent, string.Join(" ", values));
        Assert.Contains("github.com/psandler/SpotifyByYear", MusicBrainzClient.UserAgent);
    }

    [Fact]
    public async Task Not_found_returns_no_recordings()
    {
        using var client = Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Empty(await client.LookupByIsrcAsync("UNKNOWN", Ct));
    }

    [Fact]
    public async Task Rate_limit_response_is_retried()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json("""{ "recordings": [ { "id": "r1", "title": "Rich Girl" } ] }"""));
        using var client = Create(handler);

        var recordings = await client.LookupByIsrcAsync("X", Ct);

        Assert.Single(recordings);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_number_of_attempts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = Create(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LookupByIsrcAsync("X", Ct));
        Assert.Equal(MusicBrainzClient.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task Requests_are_spaced_apart()
    {
        var handler = new StubHandler(_ => Json("""{ "recordings": [] }"""));
        using var client = new MusicBrainzClient(handler, minInterval: TimeSpan.FromMilliseconds(300), backoffUnit: TimeSpan.Zero);
        var stopwatch = Stopwatch.StartNew();

        await client.LookupByIsrcAsync("A", Ct);
        await client.LookupByIsrcAsync("B", Ct);

        Assert.True(stopwatch.ElapsedMilliseconds >= 250, $"Two requests took only {stopwatch.ElapsedMilliseconds} ms");
    }

    private static MusicBrainzClient Create(StubHandler handler) =>
        new(handler, minInterval: TimeSpan.Zero, backoffUnit: TimeSpan.FromMilliseconds(1));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
