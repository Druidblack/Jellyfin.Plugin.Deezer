using System.Globalization;
using System.Text;
using Jellyfin.Plugin.DeezerMetadata.Dto;

namespace Jellyfin.Plugin.DeezerMetadata.Services;

internal static class Matching
{
    // These values intentionally mirror MusicMetadataAggregator v0.3.2.
    public const double AutomaticThreshold = 82;

    public static bool IsExact(string? left, string? right)
        => Normalize(left) is { Length: > 0 } a && a == Normalize(right);

    public static bool IsClose(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    public static double ScoreAlbum(DeezerAlbum album, string name, string? artist, int? year)
    {
        var score = 0d;

        if (IsExact(album.Title, name))
        {
            score += 55;
        }
        else if (IsClose(album.Title, name))
        {
            score += 28;
        }

        if (string.IsNullOrWhiteSpace(artist))
        {
            score += 20;
        }
        else if (IsExact(album.Artist?.Name, artist))
        {
            score += 28;
        }
        else if (IsClose(album.Artist?.Name, artist))
        {
            score += 16;
        }

        var albumYear = TryGetYear(album.ReleaseDate);
        if (year.HasValue && albumYear.HasValue)
        {
            var delta = Math.Abs(year.Value - albumYear.Value);
            score += delta switch
            {
                0 => 12,
                1 => 5,
                _ => -8
            };
        }

        if (!string.IsNullOrWhiteSpace(album.Upc))
        {
            score += 2;
        }

        return Math.Clamp(score, 0, 100);
    }

    public static bool IsStrongAutomatic(DeezerAlbum album, string name, string? artist, int? year)
    {
        if (!IsExact(album.Title, name))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(artist)
            && !IsExact(album.Artist?.Name, artist)
            && !IsClose(album.Artist?.Name, artist))
        {
            return false;
        }

        return ScoreAlbum(album, name, artist, year) >= AutomaticThreshold;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static int? TryGetYear(string? value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Year
            : null;
}
