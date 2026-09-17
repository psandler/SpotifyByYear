using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyByYear.Services;

public interface IMusicBrainzClient
{
    /// <summary>Recordings linked to an ISRC. Empty if MusicBrainz doesn't know the ISRC.</summary>
    Task<IReadOnlyList<MusicBrainzRecording>> LookupByIsrcAsync(string isrc, CancellationToken cancellationToken);

    /// <summary>Recording search by title and artist.</summary>
    Task<IReadOnlyList<MusicBrainzRecording>> SearchRecordingsAsync(string title, string artist, CancellationToken cancellationToken);
}
