using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

public sealed class DeezerArtistExternalId : IExternalId
{
    public string ProviderName => "Deezer";

    public string Key => ProviderIds.Artist;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Artist;

    public bool Supports(IHasProviderIds item) => item is MusicArtist;
}

public sealed class DeezerAlbumExternalId : IExternalId
{
    public string ProviderName => "Deezer";

    public string Key => ProviderIds.Album;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Album;

    public bool Supports(IHasProviderIds item) => item is MusicAlbum;
}
