namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Sign-in configuration for the Blazor admin console, bound from the top-level
/// <c>AdminAuth</c> section (a sibling of <c>CalendarMcp</c>, not a child of it, so the
/// server's own identity settings stay separate from the mailbox accounts it serves).
/// </summary>
public class AdminAuthConfiguration
{
    /// <summary>
    /// Email addresses permitted to sign in. An entry beginning with <c>@</c> matches a whole
    /// domain. An empty list means nobody is allowed yet and the first successful sign-in must
    /// go through the claim flow.
    /// </summary>
    public List<string> AllowedEmails { get; set; } = [];

    /// <summary>
    /// Whether the admin token may be used as a password on the console login page.
    ///
    /// Null (the default) resolves to true while no OIDC provider is configured and false once
    /// one is, so the break-glass path exists exactly while it is needed. Set explicitly to
    /// override — <c>true</c> to keep it after configuring a provider, <c>false</c> to remove
    /// it before configuring one.
    /// </summary>
    public bool? AllowTokenLogin { get; set; }

    /// <summary>
    /// OIDC providers keyed by scheme name (<c>google</c>, <c>microsoft</c>). A provider whose
    /// entry is missing or incomplete is treated as unconfigured and offered to nobody.
    /// </summary>
    public Dictionary<string, AdminAuthProviderConfiguration> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the provider entry for a scheme, or null when absent.</summary>
    public AdminAuthProviderConfiguration? GetProvider(string scheme) =>
        Providers.TryGetValue(scheme, out var provider) ? provider : null;

    /// <summary>Scheme names that are fully configured and can be offered on the login page.</summary>
    public IEnumerable<string> ConfiguredProviderSchemes() =>
        Providers.Where(p => p.Value.IsConfigured).Select(p => p.Key);

    /// <summary>
    /// Resolves the tri-state <see cref="AllowTokenLogin"/> against whether any provider is
    /// configured.
    /// </summary>
    public bool IsTokenLoginAllowed() =>
        AllowTokenLogin ?? !ConfiguredProviderSchemes().Any();
}

/// <summary>
/// One OIDC provider's client registration. The operator creates this in their own Google
/// Cloud or Entra tenant; nothing is shipped with the product.
/// </summary>
public class AdminAuthProviderConfiguration
{
    /// <summary>
    /// OIDC issuer, e.g. <c>https://accounts.google.com</c> or
    /// <c>https://login.microsoftonline.com/common/v2.0</c>. Discovery does the rest.
    /// </summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Human-readable name for the login button. Falls back to the scheme name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// True only when every value needed to complete a redirect flow is present. Partial
    /// configuration is treated as absent so a half-finished setup fails closed rather than
    /// producing an opaque error at the provider.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
