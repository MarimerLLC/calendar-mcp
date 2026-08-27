using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Tools;
using CalendarMcp.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Rocks;

namespace CalendarMcp.Tests.Tools;

/// <summary>
/// End-to-end checks that per-account permissions actually gate the tools. Two behaviours
/// matter and differ: an explicitly named account that lacks a permission is an error, while a
/// fan-out over "all accounts" silently skips the ones that opt out.
/// </summary>
[TestClass]
public class PermissionEnforcementTests
{
    private static AccountInfo Scoped(string id, AccountPermission granted, string provider = "microsoft365") =>
        TestData.CreateAccount(
            id: id,
            provider: provider,
            permissions: AccountPermissions.None.With(granted, true));

    [TestMethod]
    public async Task GetEmails_ExplicitAccountWithoutEmailRead_Throws()
    {
        var account = Scoped("cal-only", AccountPermission.CalendarRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("cal-only")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new GetEmailsTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.GetEmails("cal-only"));

        StringAssert.Contains(ex.Message, "does not permit reading email");
        StringAssert.Contains(ex.Message, "calendarRead", "the message should name what IS permitted");
        regExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_FanOut_SkipsAccountsWithoutEmailRead()
    {
        var allowed = TestData.CreateAccount(id: "acc-allowed", provider: "microsoft365");
        var denied = Scoped("acc-denied", AccountPermission.CalendarRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([allowed, denied]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.GetEmailsAsync("acc-allowed", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<EmailMessage>>(
                [TestData.CreateEmail(id: "e1", accountId: "acc-allowed")]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new GetEmailsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var doc = JsonDocument.Parse(await tool.GetEmails());

        // Only the permitted account was queried — the denied one never reached the provider.
        Assert.AreEqual(1, doc.RootElement.GetProperty("emails").GetArrayLength());
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetEmails_FanOutWithNoPermittedAccounts_Throws()
    {
        var denied = Scoped("acc-denied", AccountPermission.CalendarRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([denied]));

        var tool = new GetEmailsTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<GetEmailsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.GetEmails());

        StringAssert.Contains(ex.Message, "No accounts permit reading email");
        regExp.Verify();
    }

    [TestMethod]
    public async Task DeleteEmail_EmailReadRevoked_Throws()
    {
        // Mailbox management rides on emailRead, so a send-only account cannot delete.
        var account = Scoped("send-only", AccountPermission.EmailSend);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("send-only")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new DeleteEmailTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<DeleteEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.DeleteEmail("send-only", "e1"));

        StringAssert.Contains(ex.Message, "does not permit reading email");
        regExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_ReadOnlyAccount_Throws()
    {
        var account = Scoped("read-only", AccountPermission.EmailRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("read-only")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new SendEmailTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.SendEmail(["to@example.com"], "Subject", "Body", accountId: "read-only"));

        StringAssert.Contains(ex.Message, "does not permit sending email");
        regExp.Verify();
    }

    [TestMethod]
    public async Task SendEmail_SmartRouting_SkipsDomainMatchThatCannotSend()
    {
        // The domain match is read-only, so routing must fall through to an account that can send
        // rather than failing outright.
        var readOnlyMatch = TestData.CreateAccount(
            id: "acc-readonly",
            provider: "microsoft365",
            domains: ["example.com"],
            permissions: AccountPermissions.None.With(AccountPermission.EmailRead, true));
        var sender = TestData.CreateAccount(id: "acc-sender", provider: "google", domains: ["other.com"]);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountsByDomain("example.com").ReturnValue([readOnlyMatch]);
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([readOnlyMatch, sender]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.SendEmailAsync("acc-sender", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<List<string>?>(), Arg.Any<IReadOnlyList<OutboundEmailAttachment>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("msg-1"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("google").ReturnValue(provExp.Instance());

        var tool = new SendEmailTool(regExp.Instance(), factExp.Instance(),
            new TestAttachmentStore(), NullLogger<SendEmailTool>.Instance);

        var doc = JsonDocument.Parse(await tool.SendEmail(["someone@example.com"], "Subject", "Body"));

        Assert.AreEqual("acc-sender", doc.RootElement.GetProperty("accountUsed").GetString());
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task ListCalendars_ExplicitAccountWithoutCalendarRead_Throws()
    {
        var account = Scoped("mail-only", AccountPermission.EmailRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("mail-only")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new ListCalendarsTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<ListCalendarsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.ListCalendars("mail-only"));

        StringAssert.Contains(ex.Message, "does not permit reading calendars");
        regExp.Verify();
    }

    [TestMethod]
    public async Task ListCalendars_FanOut_SkipsAccountsWithoutCalendarRead()
    {
        var allowed = TestData.CreateAccount(id: "acc-allowed", provider: "microsoft365");
        var denied = Scoped("acc-denied", AccountPermission.EmailRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetEnabledAccounts().ReturnValue([allowed, denied]);

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.ListCalendarsAsync("acc-allowed", Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult<IEnumerable<CalendarInfo>>(
                [TestData.CreateCalendar(id: "cal-1", accountId: "acc-allowed")]));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("microsoft365").ReturnValue(provExp.Instance());

        var tool = new ListCalendarsTool(regExp.Instance(), factExp.Instance(),
            NullLogger<ListCalendarsTool>.Instance);

        var doc = JsonDocument.Parse(await tool.ListCalendars());

        Assert.AreEqual(1, doc.RootElement.GetProperty("calendars").GetArrayLength());
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_CalendarWriteRevoked_Throws()
    {
        var account = Scoped("read-only-cal", AccountPermission.CalendarRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("read-only-cal")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new CreateEventTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<CreateEventTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => tool.CreateEvent("Standup", DateTime.UtcNow, DateTime.UtcNow.AddHours(1),
                accountId: "read-only-cal"));

        StringAssert.Contains(ex.Message, "does not permit modifying calendars");
        regExp.Verify();
    }

    [TestMethod]
    public async Task CreateEvent_NoAccountId_PicksFirstAccountThatPermitsWrites()
    {
        var readOnly = Scoped("acc-readonly", AccountPermission.CalendarRead);
        var writable = TestData.CreateAccount(id: "acc-writable", provider: "google");

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([readOnly, writable]));

        var provExp = new IProviderServiceCreateExpectations();
        provExp.Setups.CreateEventAsync("acc-writable", Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>(), Arg.Any<List<string>?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ReturnValue(Task.FromResult("evt-1"));

        var factExp = new IProviderServiceFactoryCreateExpectations();
        factExp.Setups.GetProvider("google").ReturnValue(provExp.Instance());

        var tool = new CreateEventTool(regExp.Instance(), factExp.Instance(),
            NullLogger<CreateEventTool>.Instance);

        var doc = JsonDocument.Parse(
            await tool.CreateEvent("Standup", DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));

        Assert.AreEqual("acc-writable", doc.RootElement.GetProperty("accountUsed").GetString());
        regExp.Verify();
        factExp.Verify();
        provExp.Verify();
    }

    [TestMethod]
    public async Task GetContacts_ExplicitAccountWithoutContactsRead_Throws()
    {
        var account = Scoped("no-contacts", AccountPermission.EmailRead);

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAccountAsync("no-contacts")
            .ReturnValue(Task.FromResult<AccountInfo?>(account));

        var tool = new GetContactsTool(regExp.Instance(),
            new IProviderServiceFactoryCreateExpectations().Instance(),
            NullLogger<GetContactsTool>.Instance);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(() => tool.GetContacts("no-contacts"));

        StringAssert.Contains(ex.Message, "does not permit reading contacts");
        regExp.Verify();
    }

    [TestMethod]
    public async Task ListAccounts_ReportsEffectivePermissions()
    {
        var account = TestData.CreateAccount(
            id: "acc-1",
            provider: "imap",
            permissions: AccountPermissions.All.With(AccountPermission.EmailSend, false));

        var regExp = new IAccountRegistryCreateExpectations();
        regExp.Setups.GetAllAccountsAsync()
            .ReturnValue(Task.FromResult<IEnumerable<AccountInfo>>([account]));

        var tool = new ListAccountsTool(regExp.Instance(), NullLogger<ListAccountsTool>.Instance);

        var doc = JsonDocument.Parse(await tool.ListAccounts());
        var permissions = doc.RootElement.GetProperty("accounts")[0].GetProperty("permissions");

        Assert.IsTrue(permissions.GetProperty("emailRead").GetBoolean());
        Assert.IsFalse(permissions.GetProperty("emailSend").GetBoolean(), "revoked by the operator");
        Assert.IsFalse(permissions.GetProperty("calendarRead").GetBoolean(), "IMAP has no calendar");
        regExp.Verify();
    }
}
