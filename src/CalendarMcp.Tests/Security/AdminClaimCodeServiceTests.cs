using CalendarMcp.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace CalendarMcp.Tests.Security;

[TestClass]
public class AdminClaimCodeServiceTests
{
    private string _directory = "";
    private string _file = "";

    [TestInitialize]
    public void CreateTempDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "calendarmcp-claimcode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, AdminClaimCodeService.FileName);
    }

    [TestCleanup]
    public void RemoveTempDirectory()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private AdminClaimCodeService CreateService() => new(NullLogger<AdminClaimCodeService>.Instance, _file);

    [TestMethod]
    public void IsActive_IsFalseBeforeACodeIsIssued()
    {
        Assert.IsFalse(CreateService().IsActive);
    }

    [TestMethod]
    public void Issue_ProducesGroupedCodeAndActivates()
    {
        var service = CreateService();

        var code = service.Issue();

        Assert.IsTrue(service.IsActive);
        Assert.AreEqual(19, code.Length);
        CollectionAssert.AreEqual(new[] { 4, 9, 14 }, code.Select((c, i) => (c, i)).Where(x => x.c == '-').Select(x => x.i).ToArray());
    }

    [TestMethod]
    public void Issue_AvoidsAmbiguousCharacters()
    {
        // The code gets read off a terminal and typed into a browser, so the pairs that look
        // alike in a monospace font are left out: O/0 and I/1. L stays in — uppercase L is not
        // confusable with 1, and validation uppercases anything typed as lowercase.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = CreateService().Issue();
            foreach (var ambiguous in "OI01")
                Assert.IsFalse(code.Contains(ambiguous), $"'{ambiguous}' appeared in {code}");
        }
    }

    [TestMethod]
    public void Issue_WritesTheCodeToDisk()
    {
        var code = CreateService().Issue();

        Assert.IsTrue(File.Exists(_file));
        Assert.AreEqual(code, File.ReadAllText(_file).Trim());
    }

    [TestMethod]
    public void Issue_ProducesADifferentCodeEachTime()
    {
        Assert.AreNotEqual(CreateService().Issue(), CreateService().Issue());
    }

    [TestMethod]
    public void Validate_AcceptsTheIssuedCode()
    {
        var service = CreateService();
        var code = service.Issue();

        Assert.IsTrue(service.Validate(code));
    }

    [TestMethod]
    public void Validate_AcceptsLowercaseAndMissingDashes()
    {
        var service = CreateService();
        var code = service.Issue();

        Assert.IsTrue(service.Validate(code.ToLowerInvariant()));
        Assert.IsTrue(service.Validate(code.Replace("-", "")));
        Assert.IsTrue(service.Validate($"  {code.ToLowerInvariant().Replace("-", "")}  "));
    }

    [TestMethod]
    public void Validate_RejectsAWrongCode()
    {
        var service = CreateService();
        service.Issue();

        Assert.IsFalse(service.Validate("AAAA-BBBB-CCCC-DDDD"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Validate_RejectsBlankInput(string? presented)
    {
        var service = CreateService();
        service.Issue();

        Assert.IsFalse(service.Validate(presented));
    }

    [TestMethod]
    public void Validate_RejectsEverythingBeforeACodeIsIssued()
    {
        Assert.IsFalse(CreateService().Validate("AAAA-BBBB-CCCC-DDDD"));
    }

    [TestMethod]
    public void Consume_InvalidatesTheCodeAndDeletesTheFile()
    {
        // Single use is the point: a code left valid after claiming would let a second identity
        // claim the server too.
        var service = CreateService();
        var code = service.Issue();

        service.Consume();

        Assert.IsFalse(service.IsActive);
        Assert.IsFalse(service.Validate(code));
        Assert.IsFalse(File.Exists(_file));
    }

    [TestMethod]
    public void Consume_IsIdempotent()
    {
        var service = CreateService();
        service.Issue();

        service.Consume();
        service.Consume();

        Assert.IsFalse(service.IsActive);
    }

    [TestMethod]
    public void Issue_ReplacesAnOutstandingCode()
    {
        var service = CreateService();
        var first = service.Issue();
        var second = service.Issue();

        Assert.IsFalse(service.Validate(first));
        Assert.IsTrue(service.Validate(second));
    }
}
