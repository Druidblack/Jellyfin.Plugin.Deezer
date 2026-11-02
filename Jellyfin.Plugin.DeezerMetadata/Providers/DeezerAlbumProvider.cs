using System.Globalization;
using Jellyfin.Plugin.DeezerMetadata.Dto;
using Jellyfin.Plugin.DeezerMetadata.Services;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerMetadata.Providers;

public sealed class DeezerAlbumProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeezerClient _client;
    private readonly ILogger<DeezerAlbumProvider> _logger;

    public DeezerAlbumProvider(IHttpClientFactory httpClientFactory, ILogger<DeezerAlbumProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _client = new DeezerClient(httpClientFactory, logger);
    }

    public string Name => "Deezer";

    public int Order => 0;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.EnableAlbumMetadata ?? true))
        {
            return [];
        }

        var artistIdentifyLeak = HasArtistIdentifyLeak(searchInfo);
        var trustedAlbumName = GetTrustedAlbumName(searchInfo);
        if (string.IsNullOrWhiteSpace(trustedAlbumName))
        {
            return [];
        }

        // A Deezer album URL/id typed directly into an album Identify dialog is explicit.
        // Do not interpret the transient artist name injected by Jellyfin during recursive
        // artist Identify as an album lookup value.
        if (!artistIdentifyLeak && DeezerParsing.TryExtractAlbumId(searchInfo.Name, out var explicitId))
        {
            var explicitAlbum = await _client.GetAlbumAsync(explicitId, cancellationToken).ConfigureAwait(false);
            return IsValid(explicitAlbum) ? [ToSearchResult(explicitAlbum!)] : [];
        }

        var artist = searchInfo.AlbumArtists.FirstOrDefault();
        var expectedArtistId = GetArtistProviderId(searchInfo);
        var candidates = new List<DeezerAlbum>();

        // Keep a valid existing id in manual results, but never trust a stale/wrong id
        // merely because it is already stored on the album.
        var storedId = GetProviderId(searchInfo, ProviderIds.Album);
        if (TryParseId(storedId, out var storedAlbumId))
        {
            var storedAlbum = await _client.GetAlbumAsync(storedAlbumId, cancellationToken).ConfigureAwait(false);
            if (IsValid(storedAlbum) && IsStrongAlbumMatch(storedAlbum!, searchInfo, expectedArtistId))
            {
                candidates.Add(storedAlbum!);
            }
        }

        candidates.AddRange(await _client.SearchAlbumsAsync(trustedAlbumName, artist, SearchLimit, cancellationToken).ConfigureAwait(false));

        var mapped = candidates
            .Where(IsValid)
            .GroupBy(candidate => candidate.Id)
            .Select(group => group.First())
            .OrderByDescending(candidate => ScoreAlbum(candidate, searchInfo, trustedAlbumName, artist, expectedArtistId))
            .Take(SearchLimit)
            .Select(ToSearchResult)
            .ToArray();

        _logger.LogDebug(
            "Deezer: manual album search for {Album} / {Artist} returned {Count} result(s){LeakSuffix}",
            trustedAlbumName,
            artist,
            mapped.Length,
            artistIdentifyLeak ? " after ignoring leaked artist SearchResult" : string.Empty);

        return mapped;
    }

    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicAlbum> { Item = new MusicAlbum() };
        if (!(Plugin.Instance?.Configuration.EnableAlbumMetadata ?? true))
        {
            return result;
        }

        var artistIdentifyLeak = HasArtistIdentifyLeak(info);
        var expectedArtistId = GetArtistProviderId(info);
        var trustedAlbumName = GetTrustedAlbumName(info);

        if (artistIdentifyLeak)
        {
            _logger.LogDebug(
                "Deezer: detected artist Identify SearchResult leaking into album lookup for {Path}: transient name '{Name}' will be ignored; trusted album name is '{TrustedAlbumName}'",
                info.Path,
                info.Name,
                trustedAlbumName);
        }

        DeezerAlbum? album = null;

        // Explicit album URL/id entered in an album Identify dialog takes precedence.
        if (!artistIdentifyLeak && DeezerParsing.TryExtractAlbumId(info.Name, out var explicitId))
        {
            album = await _client.GetAlbumAsync(explicitId, cancellationToken).ConfigureAwait(false);
        }

        // Never blindly trust an existing DeezerAlbum id. A wrong id produced by an older
        // plugin version would otherwise become permanent and keep getting reused forever.
        if (album is null)
        {
            var savedId = GetProviderId(info, ProviderIds.Album);
            if (TryParseId(savedId, out var id))
            {
                var savedAlbum = await _client.GetAlbumAsync(id, cancellationToken).ConfigureAwait(false);
                if (IsValid(savedAlbum) && IsStrongAlbumMatch(savedAlbum!, info, expectedArtistId))
                {
                    album = savedAlbum;
                }
                else if (IsValid(savedAlbum))
                {
                    _logger.LogDebug(
                        "Deezer: stored album id {DeezerId} ({StoredTitle}) no longer matches '{Album}'; searching for a replacement",
                        id,
                        savedAlbum!.Title,
                        trustedAlbumName);
                }
            }
        }

        if (album is null
            && (Plugin.Instance?.Configuration.EnableAutomaticMatching ?? true)
            && !string.IsNullOrWhiteSpace(trustedAlbumName))
        {
            album = await FindBestAutomaticAlbumAsync(info, trustedAlbumName, expectedArtistId, cancellationToken).ConfigureAwait(false);
        }

        if (!IsValid(album))
        {
            return result;
        }

        result.HasMetadata = true;
        Populate(result.Item, album!);
        _logger.LogInformation("Deezer: resolved album {Album} as {DeezerId}", album!.Title, album.Id);
        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient("Jellyfin.Plugin.DeezerMetadata.Images")
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    private async Task<DeezerAlbum?> FindBestAutomaticAlbumAsync(
        AlbumInfo info,
        string trustedAlbumName,
        long? expectedArtistId,
        CancellationToken cancellationToken)
    {
        var artist = info.AlbumArtists.FirstOrDefault();
        var candidates = await _client.SearchAlbumsAsync(trustedAlbumName, artist, SearchLimit, cancellationToken).ConfigureAwait(false);

        var best = candidates
            .Where(IsValid)
            .Where(candidate => IsStrongAlbumMatch(candidate, info, expectedArtistId))
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreAlbum(candidate, info, trustedAlbumName, artist, expectedArtistId)
            })
            .Where(pair => pair.Score >= Matching.AutomaticThreshold)
            .OrderByDescending(pair => pair.Score)
            .FirstOrDefault();

        if (best is null)
        {
            _logger.LogDebug(
                "Deezer: no strong automatic album match for {Album} ({Artist}); provider id will not be changed",
                trustedAlbumName,
                artist);
            return null;
        }

        return await _client.GetAlbumAsync(best.Candidate.Id, cancellationToken).ConfigureAwait(false) ?? best.Candidate;
    }

    private int SearchLimit => Math.Clamp(Plugin.Instance?.Configuration.MaxSearchResults ?? 15, 1, 50);

    private static RemoteSearchResult ToSearchResult(DeezerAlbum album)
    {
        var result = new RemoteSearchResult
        {
            Name = album.Title ?? string.Empty,
            SearchProviderName = "Deezer",
            ImageUrl = BestImage(album)
        };

        var releaseDate = ParseDate(album.ReleaseDate);
        if (releaseDate.HasValue)
        {
            result.PremiereDate = releaseDate;
            result.ProductionYear = releaseDate.Value.Year;
        }

        if (!string.IsNullOrWhiteSpace(album.Artist?.Name))
        {
            var artists = new[] { new RemoteSearchResult { Name = album.Artist.Name } };
            result.AlbumArtist = artists[0];
            result.Artists = artists;
        }

        result.ProviderIds[ProviderIds.Album] = album.Id.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    private static void Populate(MusicAlbum item, DeezerAlbum album)
    {
        item.Name = album.Title!;
        item.ProviderIds[ProviderIds.Album] = album.Id.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(album.Artist?.Name))
        {
            item.AlbumArtists = [album.Artist.Name];
        }

        var releaseDate = ParseDate(album.ReleaseDate);
        if (releaseDate.HasValue)
        {
            item.PremiereDate = releaseDate;
            item.ProductionYear = releaseDate.Value.Year;
        }

        if (album.Genres?.Data is { Count: > 0 })
        {
            item.Genres = album.Genres.Data
                .Select(genre => genre.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(album.Label))
        {
            item.Studios = [album.Label];
        }
    }

    private static bool IsStrongAlbumMatch(DeezerAlbum album, AlbumInfo info, long? expectedArtistId)
    {
        if (!TitleMatches(album.Title, info))
        {
            return false;
        }

        if (expectedArtistId.HasValue && album.Artist is { Id: > 0 })
        {
            return album.Artist.Id == expectedArtistId.Value;
        }

        var expectedArtists = info.AlbumArtists
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Matching.Normalize)
            .Where(name => name.Length > 0)
            .ToArray();

        if (expectedArtists.Length == 0)
        {
            return true;
        }

        var actualArtist = Matching.Normalize(album.Artist?.Name);
        return actualArtist.Length > 0 && expectedArtists.Contains(actualArtist, StringComparer.Ordinal);
    }

    private static double ScoreAlbum(
        DeezerAlbum album,
        AlbumInfo info,
        string trustedAlbumName,
        string? artist,
        long? expectedArtistId)
    {
        var score = Matching.ScoreAlbum(album, trustedAlbumName, artist, info.Year);
        if (expectedArtistId.HasValue && album.Artist?.Id == expectedArtistId.Value)
        {
            score = Math.Min(100, score + 12);
        }

        return score;
    }

    private static bool TitleMatches(string? actualTitle, AlbumInfo info)
    {
        if (string.IsNullOrWhiteSpace(actualTitle))
        {
            return false;
        }

        return ExpectedTitles(info)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Any(title => Matching.IsExact(actualTitle, title));
    }

    private static IEnumerable<string> ExpectedTitles(AlbumInfo info)
    {
        // SongInfo.Album belongs to the child audio items and survives an artist Identify.
        // Jellyfin may recursively reuse the artist SearchResult for child albums, replacing
        // AlbumInfo.Name with the artist name. Prefer the stable song album tags first.
        var songAlbumTitles = info.SongInfos
            .Select(song => song.Album)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Cast<string>()
            .GroupBy(title => Matching.Normalize(title), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .ToArray();

        foreach (var title in songAlbumTitles)
        {
            yield return title;
        }

        if (!HasArtistIdentifyLeak(info) && !string.IsNullOrWhiteSpace(info.Name))
        {
            yield return info.Name;
            var withoutYear = StripTrailingYear(info.Name, info.Year);
            if (!string.Equals(withoutYear, info.Name, StringComparison.Ordinal))
            {
                yield return withoutYear;
            }
        }

        if (!string.IsNullOrWhiteSpace(info.OriginalTitle))
        {
            yield return info.OriginalTitle;
        }

        if (!string.IsNullOrWhiteSpace(info.Path))
        {
            var path = info.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                yield return folderName;
                var withoutYear = StripTrailingYear(folderName, info.Year);
                if (!string.Equals(withoutYear, folderName, StringComparison.Ordinal))
                {
                    yield return withoutYear;
                }
            }
        }
    }

    private static string? GetTrustedAlbumName(AlbumInfo info)
    {
        var songTitle = info.SongInfos
            .Select(song => song.Album)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Cast<string>()
            .GroupBy(title => Matching.Normalize(title), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(songTitle))
        {
            return songTitle;
        }

        if (!HasArtistIdentifyLeak(info) && !string.IsNullOrWhiteSpace(info.Name))
        {
            return StripTrailingYear(info.Name, info.Year);
        }

        if (!string.IsNullOrWhiteSpace(info.OriginalTitle))
        {
            return info.OriginalTitle;
        }

        if (!string.IsNullOrWhiteSpace(info.Path))
        {
            var path = info.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                return StripTrailingYear(folderName, info.Year);
            }
        }

        return HasArtistIdentifyLeak(info) ? null : info.Name;
    }

    private static bool HasArtistIdentifyLeak(AlbumInfo info)
        => info.ProviderIds.ContainsKey(ProviderIds.Artist)
            && !info.ProviderIds.ContainsKey(ProviderIds.Album);

    private static long? GetArtistProviderId(AlbumInfo info)
    {
        if (info.ArtistProviderIds.TryGetValue(ProviderIds.Artist, out var artistId)
            && TryParseId(artistId, out var parsedArtistId))
        {
            return parsedArtistId;
        }

        // During the Jellyfin artist-Identify leak the artist provider id can appear in
        // the album's own ProviderIds. It is still useful as an exact artist constraint.
        if (HasArtistIdentifyLeak(info)
            && info.ProviderIds.TryGetValue(ProviderIds.Artist, out var leakedArtistId)
            && TryParseId(leakedArtistId, out parsedArtistId))
        {
            return parsedArtistId;
        }

        return null;
    }

    private static string StripTrailingYear(string value, int? year)
    {
        if (!year.HasValue)
        {
            return value;
        }

        var yearText = year.Value.ToString(CultureInfo.InvariantCulture);
        var trimmed = value.Trim();
        string[] suffixes = [$" ({yearText})", $" [{yearText}]", $" - {yearText}"];
        foreach (var suffix in suffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^suffix.Length].TrimEnd();
            }
        }

        return value;
    }

    private static string? GetProviderId(AlbumInfo info, string key)
        => info.ProviderIds.TryGetValue(key, out var value) ? value : null;

    private static bool IsValid(DeezerAlbum? album)
        => album is { Id: > 0 } && !string.IsNullOrWhiteSpace(album.Title);

    private static bool TryParseId(string? value, out long id)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;

    private static string? BestImage(DeezerAlbum album)
        => album.CoverXl ?? album.CoverBig ?? album.CoverMedium ?? album.CoverSmall;
}
