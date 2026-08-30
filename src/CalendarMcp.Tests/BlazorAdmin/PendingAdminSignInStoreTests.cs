using CalendarMcp.HttpServer.BlazorAdmin;

namespace CalendarMcp.Tests.BlazorAdmin;

/// <summary>
/// The holding pen for identities a provider has verified but that are not yet authorized.
/// Its job is to make an unauthorized identity usable exactly once, and only briefly.
/// </summary>
[TestClass]
public class PendingAdminSignInStoreTests
{
    [TestMethod]
    public void Peek_ReturnsTheStoredIdentity()
    {
        var store = new PendingAdminSignInStore();

        var token = store.Add("someone@example.com", "google", "sub-1");
        var pending = store.Peek(token);

        Assert.IsNotNull(pending);
        Assert.AreEqual("someone@example.com", pending.Email);
        Assert.AreEqual("google", pending.Provider);
        Assert.AreEqual("sub-1", pending.Subject);
    }

    [TestMethod]
    public void Peek_DoesNotConsume()
    {
        // A mistyped claim code must not cost the whole provider round trip.
        var store = new PendingAdminSignInStore();
        var token = store.Add("someone@example.com", "google", "sub-1");

        Assert.IsNotNull(store.Peek(token));
        Assert.IsNotNull(store.Peek(token));
        Assert.IsNotNull(store.Consume(token));
    }

    [TestMethod]
    public void Consume_ReturnsTheIdentityOnlyOnce()
    {
        var store = new PendingAdminSignInStore();
        var token = store.Add("someone@example.com", "google", "sub-1");

        Assert.IsNotNull(store.Consume(token));
        Assert.IsNull(store.Consume(token));
        Assert.IsNull(store.Peek(token));
    }

    [TestMethod]
    public void Tokens_AreDistinctPerEntry()
    {
        var store = new PendingAdminSignInStore();

        var first = store.Add("first@example.com", "google", "sub-1");
        var second = store.Add("second@example.com", "google", "sub-2");

        Assert.AreNotEqual(first, second);
        Assert.AreEqual("first@example.com", store.Peek(first)!.Email);
        Assert.AreEqual("second@example.com", store.Peek(second)!.Email);
    }

    [TestMethod]
    public void Tokens_AreLongEnoughToNotBeGuessable()
    {
        // The token is the only thing standing between a stranger and the claim page for
        // somebody else's verified identity.
        var store = new PendingAdminSignInStore();

        var token = store.Add("someone@example.com", "google", "sub-1");

        Assert.AreEqual(32, token.Length, "expected 16 random bytes as hex");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not-a-real-token")]
    public void UnknownTokens_YieldNothing(string? token)
    {
        var store = new PendingAdminSignInStore();
        store.Add("someone@example.com", "google", "sub-1");

        Assert.IsNull(store.Peek(token));
        Assert.IsNull(store.Consume(token));
    }

    [TestMethod]
    public void Add_AcceptsAMissingSubject()
    {
        // Entra does not always supply one; the claim flow still has to work.
        var store = new PendingAdminSignInStore();

        var token = store.Add("someone@example.com", "microsoft", subject: null);

        Assert.IsNull(store.Peek(token)!.Subject);
    }
}
