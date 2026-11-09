namespace Adjutant.HttpServer.BlazorAdmin;

/// <summary>
/// Where to send the browser after a console sign-in. Only a path on this server is accepted: the
/// value arrives in a query string, and following an absolute or protocol-relative URL from there
/// would make the sign-in pages an open redirect.
/// </summary>
public static class LocalReturnUrl
{
    /// <summary>Where a sign-in lands when it wasn't sent from anywhere in particular.</summary>
    public const string Default = "/admin/ui";

    /// <summary>
    /// Returns <paramref name="returnUrl"/> when it is a local path (starts with a single <c>/</c>,
    /// no backslash, no control characters), otherwise <see cref="Default"/>.
    /// </summary>
    public static string Sanitize(string? returnUrl) =>
        IsLocal(returnUrl) ? returnUrl! : Default;

    /// <summary>True when <paramref name="returnUrl"/> is a path on this server.</summary>
    public static bool IsLocal(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
            return false;

        // "//host" and "/\host" are both read by browsers as another origin.
        if (returnUrl.Length > 1 && returnUrl[1] is '/' or '\\')
            return false;

        foreach (var c in returnUrl)
        {
            if (char.IsControl(c) || c == '\\')
                return false;
        }

        return true;
    }

    /// <summary>
    /// The query string that carries <paramref name="returnUrl"/> to another sign-in page, or an
    /// empty string when it would only repeat the default.
    /// </summary>
    public static string Query(string parameterName, string? returnUrl)
    {
        var sanitized = Sanitize(returnUrl);
        return sanitized == Default ? "" : $"?{parameterName}={Uri.EscapeDataString(sanitized)}";
    }
}
