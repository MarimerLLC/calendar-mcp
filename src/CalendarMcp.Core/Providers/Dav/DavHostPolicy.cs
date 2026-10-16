using System.Text.RegularExpressions;

namespace CalendarMcp.Core.Providers.Dav;

/// <summary>
/// Which DAV service a URL belongs to (calendar vs contacts). Used to keep
/// iCloud CalDAV and CardDAV redirect allowlists separate.
/// </summary>
public enum DavServiceKind
{
    CalDav,
    CardDav
}

/// <summary>
/// Decides whether an HTTPS host is safe to send Basic Auth credentials to.
/// iCloud uses a strict shard allowlist; other presets restrict redirects to the
/// configured entry host.
/// </summary>
public static partial class DavHostPolicy
{
    public const string PresetIcloud = "icloud";
    public const string PresetIcloudCn = "icloud-cn";
    public const string PresetNextcloud = "nextcloud";
    public const string PresetFastmail = "fastmail";
    public const string PresetGeneric = "generic";

    public const string IcloudCalDavEntry = "https://caldav.icloud.com/";
    public const string IcloudCardDavEntry = "https://contacts.icloud.com/";
    public const string IcloudCnCalDavEntry = "https://caldav.icloud.com.cn/";
    public const string IcloudCnCardDavEntry = "https://contacts.icloud.com.cn/";
    public const string FastmailDavEntry = "https://dav.fastmail.com/";

    /// <summary>
    /// Applies known presets onto missing caldav/carddav URLs.
    /// </summary>
    public static void ApplyPreset(string? preset, IDictionary<string, string> config)
    {
        var p = (preset ?? Get(config, "preset") ?? PresetGeneric).ToLowerInvariant();

        switch (p)
        {
            case PresetIcloud:
                SetIfMissing(config, "caldavUrl", IcloudCalDavEntry);
                SetIfMissing(config, "carddavUrl", IcloudCardDavEntry);
                SetIfMissing(config, "preset", PresetIcloud);
                break;
            case PresetIcloudCn:
                SetIfMissing(config, "caldavUrl", IcloudCnCalDavEntry);
                SetIfMissing(config, "carddavUrl", IcloudCnCardDavEntry);
                SetIfMissing(config, "preset", PresetIcloudCn);
                break;
            case PresetFastmail:
                SetIfMissing(config, "caldavUrl", FastmailDavEntry);
                SetIfMissing(config, "carddavUrl", FastmailDavEntry);
                SetIfMissing(config, "preset", PresetFastmail);
                break;
            case PresetNextcloud:
                // Nextcloud needs the operator-supplied server base (…/remote.php/dav/).
                SetIfMissing(config, "preset", PresetNextcloud);
                break;
            default:
                SetIfMissing(config, "preset", PresetGeneric);
                break;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="uri"/> may receive credentials for the given preset.
    /// </summary>
    public static bool IsAllowedHost(Uri uri, string? preset, DavServiceKind kind, Uri? entryUri = null)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.IdnHost;
        var p = (preset ?? PresetGeneric).ToLowerInvariant();

        return p switch
        {
            PresetIcloud => IsIcloudHost(host, kind, china: false),
            PresetIcloudCn => IsIcloudHost(host, kind, china: true),
            _ => entryUri is not null
                && string.Equals(host, entryUri.IdnHost, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static bool IsIcloudHost(string host, DavServiceKind kind, bool china)
    {
        var h = host.ToLowerInvariant();
        if (china)
        {
            return kind switch
            {
                DavServiceKind.CalDav =>
                    h is "caldav.icloud.com.cn" || IcloudCnCalShard().IsMatch(h),
                DavServiceKind.CardDav =>
                    h is "contacts.icloud.com.cn" || IcloudCnCardShard().IsMatch(h),
                _ => false
            };
        }

        return kind switch
        {
            DavServiceKind.CalDav =>
                h is "caldav.icloud.com" || IcloudCalShard().IsMatch(h),
            DavServiceKind.CardDav =>
                h is "contacts.icloud.com" || IcloudCardShard().IsMatch(h),
            _ => false
        };
    }

    private static string? Get(IDictionary<string, string> config, string key)
    {
        foreach (var kv in config)
        {
            if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                return kv.Value;
        }
        return null;
    }

    private static void SetIfMissing(IDictionary<string, string> config, string key, string value)
    {
        if (Get(config, key) is null)
            config[key] = value;
    }

    [GeneratedRegex(@"^p[0-9]{1,3}-caldav\.icloud\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IcloudCalShard();

    [GeneratedRegex(@"^p[0-9]{1,3}-contacts\.icloud\.com$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IcloudCardShard();

    [GeneratedRegex(@"^p[0-9]{1,3}-caldav\.icloud\.com\.cn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IcloudCnCalShard();

    [GeneratedRegex(@"^p[0-9]{1,3}-contacts\.icloud\.com\.cn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IcloudCnCardShard();
}
