using CalendarMcp.Core.Providers;

namespace CalendarMcp.Tests.Providers;

[TestClass]
public class MailFolderAliasesTests
{
    [TestMethod]
    [DataRow("inbox", nameof(WellKnownMailFolder.Inbox))]
    [DataRow("archive", nameof(WellKnownMailFolder.Archive))]
    [DataRow("trash", nameof(WellKnownMailFolder.Trash))]
    [DataRow("deleteditems", nameof(WellKnownMailFolder.Trash))]
    [DataRow("spam", nameof(WellKnownMailFolder.Spam))]
    [DataRow("junkemail", nameof(WellKnownMailFolder.Spam))]
    [DataRow("drafts", nameof(WellKnownMailFolder.Drafts))]
    [DataRow("sentitems", nameof(WellKnownMailFolder.Sent))]
    [DataRow("TRASH", nameof(WellKnownMailFolder.Trash))]
    [DataRow("DeletedItems", nameof(WellKnownMailFolder.Trash))]
    [DataRow("JunkEmail", nameof(WellKnownMailFolder.Spam))]
    [DataRow("  spam  ", nameof(WellKnownMailFolder.Spam))]
    public void TryParse_RecognizesAliases(string destination, string expected)
    {
        Assert.IsTrue(MailFolderAliases.TryParse(destination, out var folder));
        Assert.AreEqual(expected, folder.ToString());
    }

    [TestMethod]
    [DataRow("Receipts")]
    [DataRow("[Gmail]/Trash")]
    [DataRow("AAMkADAwATMwMAItYjU0Ny1hZDgzLTAwAi0wMAoALgAAA==")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void TryParse_RejectsNonAliases(string? destination)
    {
        Assert.IsFalse(MailFolderAliases.TryParse(destination, out _));
    }

    [TestMethod]
    [DataRow("inbox", "inbox")]
    [DataRow("archive", "archive")]
    [DataRow("trash", "deleteditems")]
    [DataRow("deleteditems", "deleteditems")]
    [DataRow("spam", "junkemail")]
    [DataRow("junkemail", "junkemail")]
    [DataRow("drafts", "drafts")]
    [DataRow("sentitems", "sentitems")]
    [DataRow("Trash", "deleteditems")]
    [DataRow("SPAM", "junkemail")]
    public void ToGraphDestinationId_MapsAliasesToWellKnownNames(string destination, string expected)
    {
        Assert.AreEqual(expected, MailFolderAliases.ToGraphDestinationId(destination));
    }

    [TestMethod]
    [DataRow("inbox", "INBOX", false, null)]
    [DataRow("trash", "TRASH", true, null)]
    [DataRow("deleteditems", "TRASH", true, null)]
    [DataRow("spam", "SPAM", true, null)]
    [DataRow("junkemail", "SPAM", true, null)]
    [DataRow("drafts", "DRAFT", false, null)]
    [DataRow("sentitems", "SENT", false, null)]
    [DataRow("archive", null, false, "-in:inbox")]
    [DataRow("Label_123", "Label_123", false, null)]
    public void ToGmailListFilter_MapsFoldersToLabels(string folder, string? labelId, bool includeSpamTrash, string? query)
    {
        var filter = MailFolderAliases.ToGmailListFilter(folder);

        Assert.AreEqual(labelId, filter.LabelId);
        Assert.AreEqual(includeSpamTrash, filter.IncludeSpamTrash);
        Assert.AreEqual(query, filter.Query);
    }

    [TestMethod]
    public void ToGraphDestinationId_PassesFolderIdsThroughUnchanged()
    {
        // Graph folder IDs are case-sensitive; they must not be normalized.
        const string folderId = "AAMkADAwATMwMAItYjU0Ny1hZDgzLTAwAi0wMAoALgAAA==";

        Assert.AreEqual(folderId, MailFolderAliases.ToGraphDestinationId(folderId));
    }
}
