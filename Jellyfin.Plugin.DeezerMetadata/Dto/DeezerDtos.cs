using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.DeezerMetadata.Dto;

internal sealed class DeezerSearchResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

internal sealed class DeezerArtist
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("picture_small")]
    public string? PictureSmall { get; set; }

    [JsonPropertyName("picture_medium")]
    public string? PictureMedium { get; set; }

    [JsonPropertyName("picture_big")]
    public string? PictureBig { get; set; }

    [JsonPropertyName("picture_xl")]
    public string? PictureXl { get; set; }

    [JsonPropertyName("nb_album")]
    public int? AlbumCount { get; set; }

    [JsonPropertyName("nb_fan")]
    public int? FanCount { get; set; }
}

internal sealed class DeezerAlbum
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("upc")]
    public string? Upc { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("cover_small")]
    public string? CoverSmall { get; set; }

    [JsonPropertyName("cover_medium")]
    public string? CoverMedium { get; set; }

    [JsonPropertyName("cover_big")]
    public string? CoverBig { get; set; }

    [JsonPropertyName("cover_xl")]
    public string? CoverXl { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("record_type")]
    public string? RecordType { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("nb_tracks")]
    public int? TrackCount { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("fans")]
    public int? FanCount { get; set; }

    [JsonPropertyName("genres")]
    public DeezerGenreContainer? Genres { get; set; }

    [JsonPropertyName("artist")]
    public DeezerArtist? Artist { get; set; }
}

internal sealed class DeezerGenreContainer
{
    [JsonPropertyName("data")]
    public List<DeezerGenre> Data { get; set; } = [];
}

internal sealed class DeezerGenre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class DeezerErrorEnvelope
{
    [JsonPropertyName("error")]
    public DeezerError? Error { get; set; }
}

internal sealed class DeezerError
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }
}
