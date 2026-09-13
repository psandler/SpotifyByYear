namespace SpotifyByYear.Models;

/// <summary>A playlist as shown in the selection list.</summary>
public sealed record PlaylistSummary(string Id, string Name, int TrackCount);
