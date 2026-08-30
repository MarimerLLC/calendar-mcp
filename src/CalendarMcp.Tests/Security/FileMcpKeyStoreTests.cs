using CalendarMcp.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Security;

[TestClass]
public class FileMcpKeyStoreTests
{
    private string _directory = "";
    private string _keyFile = "";

    [TestInitialize]
    public void CreateTempDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "calendarmcp-keystore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _keyFile = Path.Combine(_directory, "mcp-keys.json");
    }

    [TestCleanup]
    public void RemoveTempDirectory()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private FileMcpKeyStore CreateStore(string? bootstrapSecret = null) =>
        new(NullLogger<FileMcpKeyStore>.Instance, _keyFile, bootstrapSecret);

    [TestMethod]
    public void Create_ReturnsPrefixedSecret()
    {
        var store = CreateStore();

        var (_, secret) = store.Create("laptop");

        Assert.IsTrue(secret.StartsWith(FileMcpKeyStore.SecretPrefix, StringComparison.Ordinal));
        // 32 random bytes base64url-encoded, unpadded.
        Assert.AreEqual(FileMcpKeyStore.SecretPrefix.Length + 43, secret.Length);
    }

    [TestMethod]
    public void Create_NeverWritesTheSecretToDisk()
    {
        // The whole point of hashing is that a leaked key file yields no working credentials.
        var store = CreateStore();

        var (_, secret) = store.Create("laptop");

        var onDisk = File.ReadAllText(_keyFile);
        Assert.IsFalse(onDisk.Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Create_ProducesDistinctSecrets()
    {
        var store = CreateStore();

        var (firstKey, firstSecret) = store.Create("one");
        var (secondKey, secondSecret) = store.Create("two");

        Assert.AreNotEqual(firstSecret, secondSecret);
        Assert.AreNotEqual(firstKey.Id, secondKey.Id);
    }

    [TestMethod]
    public void Create_RejectsBlankLabel()
    {
        var store = CreateStore();

        Assert.ThrowsExactly<ArgumentException>(() => store.Create("   "));
    }

    [TestMethod]
    public void Validate_AcceptsAFreshlyCreatedSecret()
    {
        var store = CreateStore();
        var (key, secret) = store.Create("laptop");

        var match = store.Validate(secret);

        Assert.IsNotNull(match);
        Assert.AreEqual(key.Id, match.Id);
    }

    [TestMethod]
    public void Validate_RejectsAnUnknownSecret()
    {
        var store = CreateStore();
        store.Create("laptop");

        Assert.IsNull(store.Validate(FileMcpKeyStore.SecretPrefix + "not-a-real-key"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_RejectsMissingSecret(string? presented)
    {
        var store = CreateStore();
        store.Create("laptop");

        Assert.IsNull(store.Validate(presented));
    }

    [TestMethod]
    public void Validate_RejectsARevokedSecret()
    {
        var store = CreateStore();
        var (key, secret) = store.Create("laptop");

        Assert.IsTrue(store.Revoke(key.Id));

        Assert.IsNull(store.Validate(secret));
    }

    [TestMethod]
    public void Validate_LeavesOtherKeysWorkingAfterOneIsRevoked()
    {
        var store = CreateStore();
        var (revokedKey, revokedSecret) = store.Create("old laptop");
        var (_, liveSecret) = store.Create("new laptop");

        store.Revoke(revokedKey.Id);

        Assert.IsNull(store.Validate(revokedSecret));
        Assert.IsNotNull(store.Validate(liveSecret));
    }

    [TestMethod]
    public void Validate_RecordsLastUsed()
    {
        var store = CreateStore();
        var (key, secret) = store.Create("laptop");
        Assert.IsNull(store.List().Single(k => k.Id == key.Id).LastUsedUtc);

        store.Validate(secret);

        Assert.IsNotNull(store.List().Single(k => k.Id == key.Id).LastUsedUtc);
    }

    [TestMethod]
    public void Revoke_ReturnsFalseForUnknownId()
    {
        var store = CreateStore();

        Assert.IsFalse(store.Revoke("k_nope"));
    }

    [TestMethod]
    public void Revoke_ReturnsFalseWhenAlreadyRevoked()
    {
        var store = CreateStore();
        var (key, _) = store.Create("laptop");

        Assert.IsTrue(store.Revoke(key.Id));
        Assert.IsFalse(store.Revoke(key.Id));
    }

    [TestMethod]
    public void List_RetainsRevokedKeysForAudit()
    {
        var store = CreateStore();
        var (key, _) = store.Create("laptop");
        store.Revoke(key.Id);

        var listed = store.List().Single();

        Assert.AreEqual(key.Id, listed.Id);
        Assert.IsFalse(listed.IsActive);
        Assert.IsNotNull(listed.RevokedUtc);
    }

    [TestMethod]
    public void Keys_SurviveAReload()
    {
        var (key, secret) = CreateStore().Create("laptop");

        // A new instance stands in for a server restart reading the same file.
        var reloaded = CreateStore();

        var match = reloaded.Validate(secret);
        Assert.IsNotNull(match);
        Assert.AreEqual(key.Id, match.Id);
    }

    [TestMethod]
    public void Revocation_SurvivesAReload()
    {
        var store = CreateStore();
        var (key, secret) = store.Create("laptop");
        store.Revoke(key.Id);

        Assert.IsNull(CreateStore().Validate(secret));
    }

    [TestMethod]
    public void Load_ThrowsOnACorruptFile()
    {
        // Silently starting with an empty store would let an operator believe revoked keys are
        // still being enforced, so a damaged file has to stop the server.
        File.WriteAllText(_keyFile, "{ this is not json");

        Assert.ThrowsExactly<InvalidOperationException>(() => CreateStore());
    }

    [TestMethod]
    public void Validate_IgnoresAKeyWithAMalformedHash()
    {
        File.WriteAllText(_keyFile, """
            { "keys": [ { "id": "k_bad", "label": "corrupt", "hash": "!!not-base64!!",
                          "createdUtc": "2026-01-01T00:00:00+00:00" } ] }
            """);

        var store = CreateStore();

        Assert.IsNull(store.Validate("anything"));
        Assert.AreEqual(1, store.List().Count);
    }

    [TestMethod]
    public void BootstrapSecret_Authenticates()
    {
        var store = CreateStore(bootstrapSecret: "from-the-environment");

        var match = store.Validate("from-the-environment");

        Assert.IsNotNull(match);
        Assert.AreEqual(FileMcpKeyStore.BootstrapKey.Id, match.Id);
    }

    [TestMethod]
    public void BootstrapSecret_IsNotListedAndCannotBeRevoked()
    {
        // It lives in the environment, so rotating it means changing the deployment, not the file.
        var store = CreateStore(bootstrapSecret: "from-the-environment");

        Assert.AreEqual(0, store.List().Count);
        Assert.IsFalse(store.Revoke(FileMcpKeyStore.BootstrapKey.Id));
        Assert.IsNotNull(store.Validate("from-the-environment"));
    }

    [TestMethod]
    public void HasUsableKey_IsFalseOnAnEmptyStore()
    {
        Assert.IsFalse(CreateStore().HasUsableKey);
    }

    [TestMethod]
    public void HasUsableKey_IsTrueWithABootstrapSecret()
    {
        Assert.IsTrue(CreateStore(bootstrapSecret: "from-the-environment").HasUsableKey);
    }

    [TestMethod]
    public void HasUsableKey_IsFalseWhenEveryKeyIsRevoked()
    {
        var store = CreateStore();
        var (key, _) = store.Create("laptop");
        store.Revoke(key.Id);

        Assert.IsFalse(store.HasUsableKey);
    }
}
