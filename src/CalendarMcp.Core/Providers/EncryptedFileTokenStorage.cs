using System.Security.Cryptography;
using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Token storage using AES-256 encryption for containerized environments.
/// Encryption key is provided via CALENDAR_MCP_ENCRYPTION_KEY environment variable.
/// Falls back to plaintext storage (with warning) if no key is configured.
/// </summary>
public class EncryptedFileTokenStorage : ITokenStorage
{
    private readonly ILogger<EncryptedFileTokenStorage> _logger;
    private readonly string _storageDirectory;
    private readonly byte[]? _encryptionKey;

    public EncryptedFileTokenStorage(ILogger<EncryptedFileTokenStorage> logger)
    {
        _logger = logger;
        _storageDirectory = Path.Combine(ConfigurationPaths.GetDataDirectory(), "tokens");
        Directory.CreateDirectory(_storageDirectory);

        var keyBase64 = Environment.GetEnvironmentVariable("CALENDAR_MCP_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(keyBase64))
        {
            try
            {
                _encryptionKey = Convert.FromBase64String(keyBase64);
                if (_encryptionKey.Length != 32)
                {
                    _logger.LogError("CALENDAR_MCP_ENCRYPTION_KEY must be a 32-byte (256-bit) base64-encoded key. " +
                        "Got {Length} bytes. Falling back to plaintext storage.", _encryptionKey.Length);
                    _encryptionKey = null;
                }
                else
                {
                    _logger.LogInformation("Token encryption enabled with AES-256.");
                }
            }
            catch (FormatException)
            {
                _logger.LogError("CALENDAR_MCP_ENCRYPTION_KEY is not valid base64. Falling back to plaintext storage.");
                _encryptionKey = null;
            }
        }
        else
        {
            _logger.LogWarning("No CALENDAR_MCP_ENCRYPTION_KEY configured. Tokens will be stored in plaintext. " +
                "Set this environment variable for production use.");
        }
    }

    public async Task<byte[]?> ReadTokenAsync(string accountId)
    {
        var filePath = GetTokenFilePath(accountId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var fileData = await File.ReadAllBytesAsync(filePath);

        if (_encryptionKey != null)
        {
            return Decrypt(fileData);
        }

        return fileData;
    }

    public async Task WriteTokenAsync(string accountId, byte[] tokenData)
    {
        var filePath = GetTokenFilePath(accountId);

        byte[] dataToWrite;
        if (_encryptionKey != null)
        {
            dataToWrite = Encrypt(tokenData);
        }
        else
        {
            dataToWrite = tokenData;
        }

        await File.WriteAllBytesAsync(filePath, dataToWrite);

        // Set restrictive file permissions on Linux/macOS
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _logger.LogDebug("Token stored for account {AccountId} ({Encrypted})",
            accountId, _encryptionKey != null ? "encrypted" : "plaintext");
    }

    public Task<bool> DeleteTokenAsync(string accountId)
    {
        var filePath = GetTokenFilePath(accountId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Token deleted for account {AccountId}", accountId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> ExistsAsync(string accountId)
    {
        return Task.FromResult(File.Exists(GetTokenFilePath(accountId)));
    }

    private string GetTokenFilePath(string accountId)
    {
        // Sanitize account ID to be file-system safe
        var safeName = string.Join("_", accountId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, $"{safeName}.token");
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey!;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // Prepend IV to ciphertext for storage
        var result = new byte[aes.IV.Length + ciphertext.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

        return result;
    }

    private byte[] Decrypt(byte[] ciphertextWithIv)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey!;

        // Extract IV from beginning of data
        var iv = new byte[aes.BlockSize / 8];
        Buffer.BlockCopy(ciphertextWithIv, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var ciphertext = new byte[ciphertextWithIv.Length - iv.Length];
        Buffer.BlockCopy(ciphertextWithIv, iv.Length, ciphertext, 0, ciphertext.Length);

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }
}
