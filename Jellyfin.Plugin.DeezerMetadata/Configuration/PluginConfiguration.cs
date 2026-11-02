using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DeezerMetadata.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableArtistMetadata { get; set; } = true;

    public bool EnableAlbumMetadata { get; set; } = true;

    public bool EnableArtistImages { get; set; } = true;

    public bool EnableAlbumImages { get; set; } = true;

    public bool EnableAutomaticMatching { get; set; } = true;

    public int MaxSearchResults { get; set; } = 15;

    public int RequestTimeoutSeconds { get; set; } = 20;
}
