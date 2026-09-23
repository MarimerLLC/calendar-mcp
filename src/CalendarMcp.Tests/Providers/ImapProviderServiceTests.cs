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

    // Non-default folder names prove the account's configuration is honored.
    private static readonly ImapProviderService.ImapAccountConfig CustomFolders = new(
        AccountId: "acc-1",
        ImapHost: "imap.example.com", ImapPort: 993,
        SmtpHost: "smtp.example.com", SmtpPort: 587,
        Username: "user", Password: "pw",
        InboxFolder: "INBOX",
        SentFolder: "Sent Items",
        TrashFolder: "Deleted Items",
        JunkFolder: "Junk E-mail");

    [TestMethod]
    [DataRow("inbox", "INBOX")]
    [DataRow("sentitems", "Sent Items")]
    [DataRow("trash", "Deleted Items")]
    [DataRow("deleteditems", "Deleted Items")]
    [DataRow("spam", "Junk E-mail")]
    [DataRow("junkemail", "Junk E-mail")]
    [DataRow("Trash", "Deleted Items")]
    public void ResolveConfiguredFolder_MapsAliasesToConfiguredFolders(string destination, string expected)
    {
        Assert.IsTrue(MailFolderAliases.TryParse(destination, out var folder));

        Assert.AreEqual(expected, ImapProviderService.ResolveConfiguredFolder(folder, CustomFolders));
    }

    [TestMethod]
    [DataRow("archive")]
    [DataRow("drafts")]
    public void ResolveConfiguredFolder_ReturnsNullForSpecialUseOnlyAliases(string destination)
    {
        // No providerConfig key for these; the provider falls back to SPECIAL-USE lookup.
        Assert.IsTrue(MailFolderAliases.TryParse(destination, out var folder));

        Assert.IsNull(ImapProviderService.ResolveConfiguredFolder(folder, CustomFolders));
    }
}
