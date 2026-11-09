using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers.Dav;

/// <summary>
/// Thin WebDAV/CalDAV/CardDAV HTTP client. Follows redirects manually so hosts
/// can be re-validated against <see cref="DavHostPolicy"/> before credentials are sent.
/// </summary>
public sealed class DavHttpClient
{
    public const string HttpClientName = "DavProvider";
    private const int MaxRedirects = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DavHttpClient> _logger;

    public DavHttpClient(IHttpClientFactory httpClientFactory, ILogger<DavHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<DavHttpResult> PropfindAsync(
        Uri uri,
        string username,
        string password,
        string bodyXml,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        int depth,
        CancellationToken cancellationToken) =>
        // StringContent's mediaType arg must be type/subtype only — charset comes from Encoding.
        SendAsync(new HttpMethod("PROPFIND"), uri, username, password, bodyXml, "application/xml",
            preset, kind, entryUri, depth, ifMatch: null, cancellationToken);

    public Task<DavHttpResult> ReportAsync(
        Uri uri,
        string username,
        string password,
        string bodyXml,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        CancellationToken cancellationToken) =>
        SendAsync(new HttpMethod("REPORT"), uri, username, password, bodyXml, "application/xml",
            preset, kind, entryUri, depth: 1, ifMatch: null, cancellationToken);

    public Task<DavHttpResult> GetAsync(
        Uri uri,
        string username,
        string password,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, uri, username, password, body: null, contentType: null,
            preset, kind, entryUri, depth: null, ifMatch: null, cancellationToken);

    public Task<DavHttpResult> PutAsync(
        Uri uri,
        string username,
        string password,
        string body,
        string contentType,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        string? ifMatch,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, uri, username, password, body, contentType,
            preset, kind, entryUri, depth: null, ifMatch, cancellationToken);

    public Task<DavHttpResult> DeleteAsync(
        Uri uri,
        string username,
        string password,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        string? ifMatch,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, uri, username, password, body: null, contentType: null,
            preset, kind, entryUri, depth: null, ifMatch, cancellationToken);

    private async Task<DavHttpResult> SendAsync(
        HttpMethod method,
        Uri uri,
        string username,
        string password,
        string? body,
        string? contentType,
        string? preset,
        DavServiceKind kind,
        Uri entryUri,
        int? depth,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var current = uri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!DavHostPolicy.IsAllowedHost(current, preset, kind, entryUri))
            {
                throw new InvalidOperationException(
                    $"Refusing to send DAV credentials to host '{current.Host}' " +
                    $"(preset={preset ?? "generic"}, service={kind}).");
            }

            using var request = new HttpRequestMessage(method, current);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/vcard"));

            if (depth is not null)
                request.Headers.TryAddWithoutValidation("Depth", depth.Value.ToString());

            if (!string.IsNullOrWhiteSpace(ifMatch))
                request.Headers.TryAddWithoutValidation("If-Match", QuoteEtag(ifMatch));

            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType ?? "application/xml").MediaType!);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            // Manual redirects so we can re-check the allowlist each hop.
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (method == HttpMethod.Put || method == HttpMethod.Delete)
                {
                    throw new InvalidOperationException(
                        $"DAV {method} to '{current}' returned redirect {response.StatusCode}; " +
                        "refusing to follow write redirects (outcome unknown).");
                }

                var location = response.Headers.Location
                    ?? throw new InvalidOperationException($"DAV redirect from '{current}' missing Location.");
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                _logger.LogDebug("DAV redirect {Status} {From} -> {To}", (int)response.StatusCode, current, next);
                current = next;
                continue;
            }

            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);
            string? etag = response.Headers.ETag?.Tag?.Trim('"');
            if (etag is null && response.Content?.Headers.TryGetValues("ETag", out var etagValues) == true)
                etag = etagValues.FirstOrDefault()?.Trim().Trim('"');

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            {
                _logger.LogWarning("DAV {Method} failed with {Status}", method, (int)response.StatusCode);
            }

            return new DavHttpResult(response.StatusCode, responseBody, etag, current);
        }

        throw new InvalidOperationException($"Too many DAV redirects (>{MaxRedirects}) starting at '{uri}'.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static string QuoteEtag(string etag) =>
        etag.StartsWith('"') ? etag : $"\"{etag}\"";

}

public sealed record DavHttpResult(
    HttpStatusCode StatusCode,
    string Body,
    string? Etag,
    Uri FinalUri)
{
    public bool IsSuccess =>
        ((int)StatusCode >= 200 && (int)StatusCode < 300) || StatusCode == HttpStatusCode.MultiStatus;
}
