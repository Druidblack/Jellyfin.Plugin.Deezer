using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.DeezerMetadata.Services;

internal static partial class DeezerParsing
{
    public static bool TryExtractAlbumId(string? text, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (long.TryParse(value, out id) && id > 0)
        {
            return true;
        }

        var match = DeezerAlbumUrlRegex().Match(value);
        if (!match.Success)
        {
            match = DeezerExplicitIdRegex().Match(value);
        }

        return match.Success && long.TryParse(match.Groups[1].Value, out id) && id > 0;
    }

    public static bool TryExtractArtistId(string? text, out long id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (long.TryParse(value, out id) && id > 0)
        {
            return true;
        }

        var match = DeezerArtistUrlRegex().Match(value);
        if (!match.Success)
        {
            match = DeezerArtistExplicitIdRegex().Match(value);
        }

        return match.Success && long.TryParse(match.Groups[1].Value, out id) && id > 0;
    }

    [GeneratedRegex(@"(?:https?://)?(?:www\.)?deezer\.com/(?:[a-z]{2}/)?album/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerAlbumUrlRegex();

    [GeneratedRegex(@"^(?:deezer(?::album)?|DeezerAlbum)[:=](\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerExplicitIdRegex();

    [GeneratedRegex(@"(?:https?://)?(?:www\.)?deezer\.com/(?:[a-z]{2}/)?artist/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerArtistUrlRegex();

    [GeneratedRegex(@"^(?:deezer(?::artist)?|DeezerArtist)[:=](\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DeezerArtistExplicitIdRegex();
}
