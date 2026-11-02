using System.Globalization;
using Jellyfin.Plugin.DeezerMetadata.Dto;
using Jellyfin.Plugin.DeezerMetadata.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

public sealed class DeezerArtistProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeezerClient _client;
    private readonly ILogger<DeezerArtistProvider> _logger;

    public DeezerArtistProvider(IHttpClientFactory httpClientFactory, ILogger<DeezerArtistProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _client = new DeezerClient(httpClientFactory, logger);
    }

    public string Name => "Deezer";

    public int Order => 0;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.EnableArtistMetadata ?? true) || string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return [];
        }

        if (DeezerParsing.TryExtractArtistId(searchInfo.Name, out var explicitId))
        {
            var artist = await _client.GetArtistAsync(explicitId, cancellationToken).ConfigureAwait(false);
            return IsValid(artist) ? [ToSearchResult(artist!)] : [];
        }

        var artists = await _client.SearchArtistsAsync(searchInfo.Name, SearchLimit, cancellationToken).ConfigureAwait(false);
        return artists
            .Where(IsValid)
            .GroupBy(candidate => candidate.Id)
            .Select(group => group.First())
            .OrderByDescending(candidate => Matching.IsExact(candidate.Name, searchInfo.Name))
            .ThenByDescending(candidate => Matching.IsClose(candidate.Name, searchInfo.Name))
            .Take(SearchLimit)
            .Select(ToSearchResult)
            .ToArray();
    }

    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicArtist> { Item = new MusicArtist() };
        if (!(Plugin.Instance?.Configuration.EnableArtistMetadata ?? true))
        {
            return result;
        }

        DeezerArtist? artist = null;
        var savedId = GetProviderId(info, ProviderIds.Artist);
        if (TryParseId(savedId, out var id))
        {
            artist = await _client.GetArtistAsync(id, cancellationToken).ConfigureAwait(false);
        }
        else if (DeezerParsing.TryExtractArtistId(info.Name, out var explicitId))
        {
            artist = await _client.GetArtistAsync(explicitId, cancellationToken).ConfigureAwait(false);
        }
        else if ((Plugin.Instance?.Configuration.EnableAutomaticMatching ?? true) && !string.IsNullOrWhiteSpace(info.Name))
        {
            var candidates = await _client.SearchArtistsAsync(info.Name, SearchLimit, cancellationToken).ConfigureAwait(false);
            artist = candidates.FirstOrDefault(candidate => Matching.IsExact(candidate.Name, info.Name));
        }

        if (!IsValid(artist))
        {
            return result;
        }

        result.HasMetadata = true;
        result.Item.Name = artist!.Name!;
        result.Item.ProviderIds[ProviderIds.Artist] = artist.Id.ToString(CultureInfo.InvariantCulture);
        _logger.LogInformation("Deezer: resolved artist {Artist} as {DeezerId}", artist.Name, artist.Id);
        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient("Jellyfin.Plugin.DeezerMetadata.Images")
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private int SearchLimit => Math.Clamp(Plugin.Instance?.Configuration.MaxSearchResults ?? 15, 1, 50);

    private static RemoteSearchResult ToSearchResult(DeezerArtist artist)
    {
        var result = new RemoteSearchResult
        {
            Name = artist.Name ?? string.Empty,
            ImageUrl = BestImage(artist),
            SearchProviderName = "Deezer"
        };
        result.ProviderIds[ProviderIds.Artist] = artist.Id.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    private static string? GetProviderId(ArtistInfo info, string key)
        => info.ProviderIds.TryGetValue(key, out var value) ? value : null;

    private static bool IsValid(DeezerArtist? artist)
        => artist is { Id: > 0 } && !string.IsNullOrWhiteSpace(artist.Name);

    private static bool TryParseId(string? value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static string? BestImage(DeezerArtist artist)
        => artist.PictureXl ?? artist.PictureBig ?? artist.PictureMedium ?? artist.PictureSmall;
}
