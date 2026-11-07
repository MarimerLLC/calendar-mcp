using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIddict.Abstractions;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// OAuth Dynamic Client Registration (RFC 7591), which MCP now deprecates in favour of client ID
/// metadata documents but older clients (and VS Code) still use. Registers public clients only: any
/// requested authentication method is replaced with <c>none</c> (RFC 7591 lets the server substitute
/// values), so PKCE is what protects the code. Rate limited per client IP, because anyone can call
/// it, and clients that are never used are deleted after a day (<see cref="IdentityCleanup"/>).
/// Ported from MarimerLLC/scheduler.
/// </summary>
internal static class DynamicClientRegistration
{
    public const string Path = "/connect/register";
    public const string UnnamedClient = "Unnamed MCP client";
    public const string ClientIdPrefix = "dcr_";

    /// <summary>Application property: when the client registered (ISO 8601), so an unused one can be deleted.</summary>
    public const string RegisteredAtProperty = "registered_at";

    public static void MapDynamicClientRegistration(this WebApplication app) =>
        app.MapPost(Path, RegisterAsync).DisableAntiforgery();

    private static async Task<IResult> RegisterAsync(
        HttpRequest request,
        IOpenIddictApplicationManager applications,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        RegistrationRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<RegistrationRequest>(cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Error("The request body must be JSON client metadata.");
        }
        if (body is null)
            return Error("The request body must be JSON client metadata.");

        if (body.GrantTypes is { } grants && !grants.Contains("authorization_code"))
            return Error("Only the authorization_code grant (with refresh_token) is supported.");
        if (body.ResponseTypes is { } responses && !responses.Contains("code"))
            return Error("Only the code response type is supported.");

        var (redirectUris, redirectError) = ClientRegistration.ParseRedirectUris(body.RedirectUris);
        if (redirectError is not null)
            return Error(redirectError, "invalid_redirect_uri");

        var clientId = ClientIdPrefix + Guid.NewGuid().ToString("N");
        var name = ClientRegistration.CleanName(body.ClientName) ?? UnnamedClient;
        var descriptor = ClientRegistration.Descriptor(clientId, name, redirectUris, ClientRegistration.Dynamic);
        var now = time.GetUtcNow();
        descriptor.Properties[RegisteredAtProperty] = ClientRegistration.Timestamp(now);
        await applications.CreateAsync(descriptor, cancellationToken);

        return Results.Json(new RegistrationResponse
        {
            ClientId = clientId,
            ClientIdIssuedAt = now.ToUnixTimeSeconds(),
            ClientName = name,
            RedirectUris = [.. redirectUris.Select(u => u.OriginalString)],
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            TokenEndpointAuthMethod = "none",
            ApplicationType = descriptor.ApplicationType!,
        }, statusCode: StatusCodes.Status201Created);
    }

    private static IResult Error(string description, string error = "invalid_client_metadata") =>
        Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);

    private sealed class RegistrationRequest
    {
        [JsonPropertyName("client_name")] public string? ClientName { get; set; }
        [JsonPropertyName("redirect_uris")] public List<string?>? RedirectUris { get; set; }
        [JsonPropertyName("grant_types")] public List<string>? GrantTypes { get; set; }
        [JsonPropertyName("response_types")] public List<string>? ResponseTypes { get; set; }
    }

    private sealed class RegistrationResponse
    {
        [JsonPropertyName("client_id")] public required string ClientId { get; init; }
        [JsonPropertyName("client_id_issued_at")] public long ClientIdIssuedAt { get; init; }
        [JsonPropertyName("client_name")] public required string ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public required List<string> RedirectUris { get; init; }
        [JsonPropertyName("grant_types")] public required List<string> GrantTypes { get; init; }
        [JsonPropertyName("response_types")] public required List<string> ResponseTypes { get; init; }
        [JsonPropertyName("token_endpoint_auth_method")] public required string TokenEndpointAuthMethod { get; init; }
        [JsonPropertyName("application_type")] public required string ApplicationType { get; init; }
    }
}
