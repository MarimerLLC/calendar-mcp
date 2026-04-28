using CalendarMcp.Core.Providers;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class ImapProviderServiceTests
{
    [TestMethod]
    public void FormatEmailId_ProducesParseableId()
    {
        var id = ImapProviderService.FormatEmailId("INBOX", 1234567890u, 4567u);

        Assert.AreEqual("INBOX/1234567890/4567", id);
    }

    [TestMethod]
    public void ParseEmailId_RoundTripsSimpleFolder()
    {
        var id = ImapProviderService.FormatEmailId("INBOX", 1234567890u, 4567u);

        var (folder, uidValidity, uid) = ImapProviderService.ParseEmailId(id);

        Assert.AreEqual("INBOX", folder);
        Assert.AreEqual(1234567890u, uidValidity);
        Assert.AreEqual(4567u, uid);
    }

    [TestMethod]
    public void ParseEmailId_PreservesFolderWithInternalSlashes()
    {
        // Gmail folder names like "[Gmail]/Trash" contain a literal slash that
        // must survive parsing — only the trailing two slashes delimit the IDs.
        var id = ImapProviderService.FormatEmailId("[Gmail]/Trash", 999u, 42u);

        var (folder, uidValidity, uid) = ImapProviderService.ParseEmailId(id);

        Assert.AreEqual("[Gmail]/Trash", folder);
        Assert.AreEqual(999u, uidValidity);
        Assert.AreEqual(42u, uid);
    }

    [TestMethod]
    [DataRow("not-an-id")]
    [DataRow("INBOX/abc/4567")]
    [DataRow("INBOX/1234/notanumber")]
    public void ParseEmailId_RejectsInvalidFormats(string bad)
    {
        Assert.ThrowsException<FormatException>(() => ImapProviderService.ParseEmailId(bad));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ParseEmailId_RejectsEmptyOrWhitespace(string bad)
    {
        Assert.ThrowsException<ArgumentException>(() => ImapProviderService.ParseEmailId(bad));
    }
}
