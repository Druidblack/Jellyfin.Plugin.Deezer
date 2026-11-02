using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.DeezerMetadata.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerMetadata.Services;

internal sealed class DeezerClient
{
    private const string BaseUrl = "https://api.deezer.com";
    private const string ClientName = "Jellyfin.Plugin.DeezerMetadata.Deezer";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public DeezerClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<DeezerArtist?> GetArtistAsync(long id, CancellationToken cancellationToken)
        => GetAsync<DeezerArtist>($"/artist/{id}", cancellationToken);

    public Task<DeezerAlbum?> GetAlbumAsync(long id, CancellationToken cancellationToken)
        => GetAsync<DeezerAlbum>($"/album/{id}", cancellationToken);

    public Task<DeezerAlbum?> GetAlbumByUpcAsync(string upc, CancellationToken cancellationToken)
        => GetAsync<DeezerAlbum>($"/album/upc:{Uri.EscapeDataString(NormalizeUpc(upc))}", cancellationToken);

    public async Task<IReadOnlyList<DeezerArtist>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var response = await GetAsync<DeezerSearchResponse<DeezerArtist>>(
            $"/search/artist?q={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit, 1, 50)}",
            cancellationToken).ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<IReadOnlyList<DeezerAlbum>> SearchAlbumsAsync(
        string album,
        string? artist,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchText = string.IsNullOrWhiteSpace(artist) ? album : $"{album} {artist}";
        var response = await GetAsync<DeezerSearchResponse<DeezerAlbum>>(
            $"/search/album?q={Uri.EscapeDataString(searchText)}&limit={Math.Clamp(limit, 1, 50)}",
            cancellationToken).ConfigureAwait(false);
        return response?.Data ?? [];
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(Plugin.Instance?.Configuration.RequestTimeoutSeconds ?? 20, 5, 120);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var client = _httpClientFactory.CreateClient(ClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                request.Headers.UserAgent.ParseAdd("Jellyfin-DeezerMetadata/0.2.3");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                var payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (ShouldRetry(response.StatusCode, attempt))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(attempt * 350), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogDebug("Deezer request {Path} returned HTTP {StatusCode}", path, (int)response.StatusCode);
                    return default;
                }

                var apiError = TryReadApiError(payload);
                if (apiError is not null)
                {
                    _logger.LogDebug("Deezer API error for {Path}: {Code} {Message}", path, apiError.Code, apiError.Message);
                    return default;
                }

                return JsonSerializer.Deserialize<T>(payload, JsonOptions);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastException = ex;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Unable to parse Deezer response for {Path}", path);
                return default;
            }

            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 350), cancellationToken).ConfigureAwait(false);
            }
        }

        if (lastException is not null)
        {
            _logger.LogDebug(lastException, "Deezer request failed after retries for {Path}", path);
        }

        return default;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, int attempt)
        => attempt < 3 && ((int)statusCode == 429 || (int)statusCode >= 500);

    private static DeezerError? TryReadApiError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DeezerErrorEnvelope>(payload, JsonOptions)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeUpc(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length > 12 ? digits[^12..] : digits.PadLeft(12, '0');
    }
}
