using CalendarMcp.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Security;

[TestClass]
public class AdminUserStoreTests
{
    private string _directory = "";
    private string _file = "";

    [TestInitialize]
    public void CreateTempDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "calendarmcp-adminusers-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "admin-users.json");
    }

    [TestCleanup]
    public void RemoveTempDirectory()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private AdminUserStore CreateStore() => new(NullLogger<AdminUserStore>.Instance, _file);

    [TestMethod]
    public void RecordSignIn_ReportsTheFirstSignIn()
    {
        var store = CreateStore();

        Assert.AreEqual(
            AdminSignInResult.FirstSignIn,
            store.RecordSignIn("someone@example.com", "google", "sub-1"));
    }

    [TestMethod]
    public void RecordSignIn_RecognizesTheSameSubjectAgain()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");

        Assert.AreEqual(
            AdminSignInResult.Recognized,
            store.RecordSignIn("someone@example.com", "google", "sub-1"));
    }

    [TestMethod]
    public void RecordSignIn_RefusesADifferentSubjectForTheSameAddress()
    {
        // The allow-list is by email, and an address can be reassigned to a different person.
        // Binding the subject on first sign-in is what stops that becoming console access.
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");

        Assert.AreEqual(
            AdminSignInResult.SubjectMismatch,
            store.RecordSignIn("someone@example.com", "google", "sub-2"));
    }

    [TestMethod]
    public void RecordSignIn_DoesNotOverwriteABoundSubjectOnMismatch()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");
        store.RecordSignIn("someone@example.com", "google", "sub-2");

        Assert.AreEqual("sub-1", store.Find("someone@example.com")!.Subject);
    }

    [TestMethod]
    public void RecordSignIn_AdoptsASubjectWhenNoneWasBound()
    {
        // A record written before subject binding, or by a provider that omitted the claim,
        // should be adopted rather than locked out.
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "microsoft", subject: null);

        Assert.AreEqual(
            AdminSignInResult.Recognized,
            store.RecordSignIn("someone@example.com", "microsoft", "sub-1"));
        Assert.AreEqual("sub-1", store.Find("someone@example.com")!.Subject);
    }

    [TestMethod]
    public void RecordSignIn_AcceptsAMissingSubjectAgainstABoundOne()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");

        Assert.AreEqual(
            AdminSignInResult.Recognized,
            store.RecordSignIn("someone@example.com", "google", subject: null));
        Assert.AreEqual("sub-1", store.Find("someone@example.com")!.Subject);
    }

    [TestMethod]
    public void RecordSignIn_NormalizesTheAddress()
    {
        var store = CreateStore();
        store.RecordSignIn("  SomeOne@Example.COM ", "google", "sub-1");

        Assert.IsNotNull(store.Find("someone@example.com"));
        Assert.AreEqual("someone@example.com", store.List().Single().Email);
    }

    [TestMethod]
    public void RecordSignIn_KeepsFirstSeenAndAdvancesLastSeen()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");
        var first = store.Find("someone@example.com")!;

        store.RecordSignIn("someone@example.com", "google", "sub-1");
        var second = store.Find("someone@example.com")!;

        Assert.AreEqual(first.FirstSeenUtc, second.FirstSeenUtc);
        Assert.IsTrue(second.LastSeenUtc >= first.LastSeenUtc);
    }

    [TestMethod]
    public void Records_SurviveAReload()
    {
        CreateStore().RecordSignIn("someone@example.com", "google", "sub-1");

        var reloaded = CreateStore();

        Assert.AreEqual(
            AdminSignInResult.SubjectMismatch,
            reloaded.RecordSignIn("someone@example.com", "google", "sub-2"));
    }

    [TestMethod]
    public void Remove_UnbindsTheSubject()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");

        Assert.IsTrue(store.Remove("someone@example.com"));
        Assert.IsNull(store.Find("someone@example.com"));
        Assert.AreEqual(
            AdminSignInResult.FirstSignIn,
            store.RecordSignIn("someone@example.com", "google", "sub-2"));
    }

    [TestMethod]
    public void Remove_ReturnsFalseForAnUnknownAddress()
    {
        Assert.IsFalse(CreateStore().Remove("nobody@example.com"));
    }

    [TestMethod]
    public void Load_ThrowsOnACorruptFile()
    {
        // Silently starting empty would unbind every subject, quietly downgrading the check.
        File.WriteAllText(_file, "{ not json");

        Assert.ThrowsExactly<InvalidOperationException>(() => CreateStore());
    }

    [TestMethod]
    public void Find_ReturnsNullForBlankInput()
    {
        var store = CreateStore();
        store.RecordSignIn("someone@example.com", "google", "sub-1");

        Assert.IsNull(store.Find(null));
        Assert.IsNull(store.Find("   "));
    }
}
