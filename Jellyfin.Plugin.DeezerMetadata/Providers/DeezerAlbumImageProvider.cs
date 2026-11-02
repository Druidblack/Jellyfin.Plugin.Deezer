using System.Globalization;
using Jellyfin.Plugin.DeezerMetadata.Dto;
using Jellyfin.Plugin.DeezerMetadata.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

public sealed class DeezerAlbumImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeezerClient _client;
    private readonly ILogger<DeezerAlbumImageProvider> _logger;

    public DeezerAlbumImageProvider(IHttpClientFactory httpClientFactory, ILogger<DeezerAlbumImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _client = new DeezerClient(httpClientFactory, logger);
    }

    public string Name => "Deezer";

    public int Order => 0;

    public bool Supports(BaseItem item) => item is MusicAlbum;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.EnableAlbumImages ?? true) || item is not MusicAlbum albumItem)
        {
            return [];
        }

        DeezerAlbum? album = null;
        var savedId = albumItem.GetProviderId(ProviderIds.Album);
        if (TryParseId(savedId, out var id))
        {
            album = await _client.GetAlbumAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else if ((Plugin.Instance?.Configuration.EnableAutomaticMatching ?? true) && !string.IsNullOrWhiteSpace(albumItem.Name))
        {
            var artist = albumItem.AlbumArtists.FirstOrDefault();
            var candidates = await _client.SearchAlbumsAsync(albumItem.Name, artist, SearchLimit, cancellationToken).ConfigureAwait(false);
            var best = candidates
                .Where(IsValid)
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = Matching.ScoreAlbum(candidate, albumItem.Name, artist, albumItem.ProductionYear)
                })
                .Where(pair => pair.Score >= Matching.AutomaticThreshold
                    && Matching.IsStrongAutomatic(pair.Candidate, albumItem.Name, artist, albumItem.ProductionYear))
                .OrderByDescending(pair => pair.Score)
                .FirstOrDefault();

            if (best is not null)
            {
                album = await _client.GetAlbumAsync(best.Candidate.Id, cancellationToken).ConfigureAwait(false) ?? best.Candidate;
            }
        }

        var image = ToImage(album);
        if (image is null)
        {
            return [];
        }

        _logger.LogDebug("Deezer: resolved cover for {Album} from album {DeezerId}", albumItem.Name, album!.Id);
        return [image];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient("Jellyfin.Plugin.DeezerMetadata.Images")
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private int SearchLimit => Math.Clamp(Plugin.Instance?.Configuration.MaxSearchResults ?? 15, 1, 50);

    private static RemoteImageInfo? ToImage(DeezerAlbum? album)
    {
        if (!IsValid(album))
        {
            return null;
        }

        var url = album!.CoverXl ?? album.CoverBig ?? album.CoverMedium ?? album.CoverSmall;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new RemoteImageInfo
        {
            ProviderName = "Deezer",
            Url = url,
            ThumbnailUrl = album.CoverMedium ?? album.CoverSmall ?? url,
            Width = album.CoverXl is not null ? 1000 : album.CoverBig is not null ? 500 : album.CoverMedium is not null ? 250 : 56,
            Height = album.CoverXl is not null ? 1000 : album.CoverBig is not null ? 500 : album.CoverMedium is not null ? 250 : 56,
            Type = ImageType.Primary
        };
    }

    private static bool IsValid(DeezerAlbum? album)
        => album is { Id: > 0 } && !string.IsNullOrWhiteSpace(album.Title);

    private static bool TryParseId(string? value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
