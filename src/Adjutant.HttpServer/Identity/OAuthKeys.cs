using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Adjutant.Core.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace Adjutant.HttpServer.Identity;

/// <summary>
/// The certificates that sign and encrypt OAuth tokens.
///
/// An operator can supply them (<c>Adjutant:Mcp:SigningCertificate</c> and
/// <c>EncryptionCertificate</c>, each a base64 PKCS#12 with its private key). Otherwise they are
/// generated on first start and kept in <c>{data}/oauth-keys</c>, encrypted with the server's
/// existing DataProtection keyring, so a self-hosted server needs no extra secrets. Losing them only
/// means connected apps sign in again.
/// </summary>
internal static class OAuthKeys
{
    public const string DirectoryName = "oauth-keys";

    // Pre-rename name, kept so signing keys already on disk still decrypt.
    private const string ProtectorPurpose = "CalendarMcp.OAuthKeys.v1";
    private static readonly TimeSpan Validity = TimeSpan.FromDays(365 * 20);

    /// <summary>The signing and encryption certificates, from configuration or the data directory.</summary>
    public static (X509Certificate2 Signing, X509Certificate2 Encryption) Load(McpEndpointConfiguration config, string dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(config.SigningCertificate) || !string.IsNullOrWhiteSpace(config.EncryptionCertificate))
        {
            if (string.IsNullOrWhiteSpace(config.SigningCertificate) || string.IsNullOrWhiteSpace(config.EncryptionCertificate))
            {
                throw new InvalidOperationException(
                    "Set both Adjutant:Mcp:SigningCertificate and Adjutant:Mcp:EncryptionCertificate, or neither " +
                    "to have the server generate its own.");
            }

            return (FromBase64(config.SigningCertificate, "SigningCertificate"),
                    FromBase64(config.EncryptionCertificate, "EncryptionCertificate"));
        }

        // A standalone provider over the same keyring directory and application name as the
        // server's own DataProtection, so the files can be read whichever one wrote them. It is
        // needed because OpenIddict takes its certificates while services are still being registered.
        var protector = DataProtectionProvider
            .Create(
                new DirectoryInfo(ConfigurationPaths.GetDataProtectionKeysDirectory()),
                builder => builder.SetApplicationName(DataProtectionExtensions.ApplicationName))
            .CreateProtector(ProtectorPurpose);

        var directory = Path.Combine(dataDirectory, DirectoryName);
        return (LoadOrCreate(directory, "signing", X509KeyUsageFlags.DigitalSignature, protector),
                LoadOrCreate(directory, "encryption", X509KeyUsageFlags.KeyEncipherment, protector));
    }

    /// <summary>Reads a stored certificate, or generates and stores one when there is none.</summary>
    internal static X509Certificate2 LoadOrCreate(
        string directory, string name, X509KeyUsageFlags usage, IDataProtector protector)
    {
        var path = Path.Combine(directory, $"{name}.pfx.protected");
        if (File.Exists(path))
        {
            try
            {
                return LoadPkcs12(protector.Unprotect(File.ReadAllBytes(path)));
            }
            catch (CryptographicException ex)
            {
                // The keyring changed under the file. Starting with an unusable key would fail every
                // sign-in with an opaque error; refusing to start says exactly what to fix.
                throw new InvalidOperationException(
                    $"Could not decrypt the OAuth {name} key at '{path}' (the DataProtection keyring changed?). " +
                    "Delete the file to generate a new one; connected MCP apps will have to sign in again.", ex);
            }
        }

        var pfx = Generate($"CN=Adjutant OAuth {name}", usage);
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protector.Protect(pfx));
        File.Move(temp, path, overwrite: true);
        return LoadPkcs12(pfx);
    }

    /// <summary>A self-signed RSA certificate with the given key usage, exported as PKCS#12.</summary>
    internal static byte[] Generate(string subject, X509KeyUsageFlags usage)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // OpenIddict checks the key usage: DigitalSignature to sign, KeyEncipherment to encrypt.
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));

        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.Add(Validity));
        return certificate.Export(X509ContentType.Pfx);
    }

    private static X509Certificate2 FromBase64(string base64, string setting)
    {
        try
        {
            return LoadPkcs12(Convert.FromBase64String(base64));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"Adjutant:Mcp:{setting} must be a base64 PKCS#12 certificate with its private key and no password.", ex);
        }
    }

    private static X509Certificate2 LoadPkcs12(byte[] pfx) =>
        X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.EphemeralKeySet);
}
