using System.Globalization;
using Jellyfin.Plugin.DeezerMetadata.Dto;
using Jellyfin.Plugin.DeezerMetadata.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

public sealed class DeezerArtistImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeezerClient _client;

    public DeezerArtistImageProvider(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Logging.ILogger<DeezerArtistImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _client = new DeezerClient(httpClientFactory, logger);
    }

    public string Name => "Deezer";

    public int Order => 0;

    public bool Supports(BaseItem item) => item is MusicArtist;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.EnableArtistImages ?? true) || item is not MusicArtist artistItem)
        {
            return [];
        }

        DeezerArtist? artist = null;
        var savedId = artistItem.GetProviderId(ProviderIds.Artist);
        if (TryParseId(savedId, out var id))
        {
            artist = await _client.GetArtistAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else if ((Plugin.Instance?.Configuration.EnableAutomaticMatching ?? true) && !string.IsNullOrWhiteSpace(artistItem.Name))
        {
            var candidates = await _client.SearchArtistsAsync(artistItem.Name, SearchLimit, cancellationToken).ConfigureAwait(false);
            artist = candidates.FirstOrDefault(candidate => Matching.IsExact(candidate.Name, artistItem.Name));
        }

        var image = ToImage(artist);
        return image is null ? [] : [image];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient("Jellyfin.Plugin.DeezerMetadata.Images")
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private int SearchLimit => Math.Clamp(Plugin.Instance?.Configuration.MaxSearchResults ?? 15, 1, 50);

    private static RemoteImageInfo? ToImage(DeezerArtist? artist)
    {
        if (artist is not { Id: > 0 } || string.IsNullOrWhiteSpace(artist.Name))
        {
            return null;
        }

        var url = artist.PictureXl ?? artist.PictureBig ?? artist.PictureMedium ?? artist.PictureSmall;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new RemoteImageInfo
        {
            ProviderName = "Deezer",
            Url = url,
            ThumbnailUrl = artist.PictureMedium ?? artist.PictureSmall ?? url,
            Width = artist.PictureXl is not null ? 1000 : artist.PictureBig is not null ? 500 : artist.PictureMedium is not null ? 250 : 56,
            Height = artist.PictureXl is not null ? 1000 : artist.PictureBig is not null ? 500 : artist.PictureMedium is not null ? 250 : 56,
            Type = ImageType.Primary
        };
    }

    private static bool TryParseId(string? value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
