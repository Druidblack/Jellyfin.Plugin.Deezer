using Jellyfin.Plugin.DeezerMetadata.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

/// <summary>
/// Adds links from Jellyfin music items to the matching Deezer page.
/// </summary>
public sealed class DeezerExternalUrlProvider : IExternalUrlProvider
{
    public string Name => "Deezer";

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is MusicArtist
            && item.ProviderIds.TryGetValue(ProviderIds.Artist, out var rawArtistId)
            && DeezerParsing.TryExtractArtistId(rawArtistId, out var artistId))
        {
            yield return $"https://www.deezer.com/artist/{artistId}";
        }

        if (item is MusicAlbum
            && item.ProviderIds.TryGetValue(ProviderIds.Album, out var rawAlbumId)
            && DeezerParsing.TryExtractAlbumId(rawAlbumId, out var albumId))
        {
            yield return $"https://www.deezer.com/album/{albumId}";
        }
    }
}
