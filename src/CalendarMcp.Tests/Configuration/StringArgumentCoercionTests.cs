using System.IO.Pipelines;
using System.Text.Json;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Tests.Configuration;

/// <summary>
/// Clients sometimes send booleans and numbers as strings ("true", "50"). The end-to-end tests run
/// a real MCP server and client over in-memory pipes, so they cover the SDK's binding as well as
/// the filter.
/// </summary>
[TestClass]
public class StringArgumentCoercionTests
{
    [McpServerToolType]
    public sealed class ProbeTool
    {
        [McpServerTool(Name = "probe")]
        public static string Probe(
            bool flag,
            bool? maybe = null,
            string text = "",
            int count = 0,
            int? limit = null,
            double ratio = 0) =>
            $"flag={flag};maybe={maybe?.ToString() ?? "null"};text={text};count={count};" +
            $"limit={limit?.ToString() ?? "null"};ratio={ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private const string Defaults = "text=;count=0;limit=null;ratio=0";

    [TestMethod]
    [DataRow("true", "True")]
    [DataRow("false", "False")]
    [DataRow("TRUE", "True")]
    [DataRow(" False ", "False")]
    public async Task StringBoolean_IsBoundAsBoolean(string sent, string expected)
    {
        var result = await CallProbeAsync(new() { ["flag"] = sent, ["maybe"] = sent });

        Assert.IsFalse(result.IsError == true, Text(result));
        Assert.AreEqual($"flag={expected};maybe={expected};{Defaults}", Text(result));
    }

    [TestMethod]
    public async Task StringNumbers_AreBoundAsNumbers()
    {
        var result = await CallProbeAsync(new()
        {
            ["flag"] = true, ["count"] = "50", ["limit"] = " -3 ", ["ratio"] = "0.25"
        });

        Assert.IsFalse(result.IsError == true, Text(result));
        Assert.AreEqual("flag=True;maybe=null;text=;count=50;limit=-3;ratio=0.25", Text(result));
    }

    [TestMethod]
    public async Task RealValues_StillWork()
    {
        var result = await CallProbeAsync(new() { ["flag"] = true, ["count"] = 7 });

        Assert.AreEqual("flag=True;maybe=null;text=;count=7;limit=null;ratio=0", Text(result));
    }

    [TestMethod]
    public async Task StringParameter_KeepsItsText()
    {
        var result = await CallProbeAsync(new() { ["flag"] = false, ["text"] = "true" });

        Assert.AreEqual("flag=False;maybe=null;text=true;count=0;limit=null;ratio=0", Text(result));
    }

    [TestMethod]
    [DataRow("flag", "yes")]
    [DataRow("count", "fifty")]
    [DataRow("count", "2.5")]
    [DataRow("count", "99999999999")]
    [DataRow("count", "")]
    public async Task UnusableStrings_StillFail(string parameter, string sent)
    {
        var arguments = new Dictionary<string, object?> { ["flag"] = true, [parameter] = sent };

        var result = await CallProbeAsync(arguments);

        Assert.IsTrue(result.IsError == true, Text(result));
    }

    [TestMethod]
    public void Coerce_OnlyRewritesBooleanAndNumberProperties()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "flag":{"type":"boolean"},
              "count":{"type":"integer"},
              "limit":{"type":["integer","null"]},
              "either":{"type":["boolean","string"]},
              "name":{"type":"string"}}}
            """).RootElement;
        var arguments = new Dictionary<string, JsonElement>
        {
            ["flag"] = JsonSerializer.SerializeToElement("true"),
            ["count"] = JsonSerializer.SerializeToElement("50"),
            ["limit"] = JsonSerializer.SerializeToElement("7"),
            ["either"] = JsonSerializer.SerializeToElement("true"),
            ["name"] = JsonSerializer.SerializeToElement("50"),
            ["unknown"] = JsonSerializer.SerializeToElement("true")
        };

        var result = StringArgumentCoercion.Coerce(schema, arguments, out var names);

        CollectionAssert.AreEquivalent(new[] { "flag", "count", "limit" }, names);
        Assert.AreEqual(JsonValueKind.True, result["flag"].ValueKind);
        Assert.AreEqual(50, result["count"].GetInt32());
        Assert.AreEqual(7, result["limit"].GetInt32());
        Assert.AreEqual(JsonValueKind.String, result["either"].ValueKind);
        Assert.AreEqual(JsonValueKind.String, result["name"].ValueKind);
        Assert.AreEqual(JsonValueKind.String, result["unknown"].ValueKind);
        // The caller's dictionary is not modified.
        Assert.AreEqual(JsonValueKind.String, arguments["flag"].ValueKind);
    }

    [TestMethod]
    [DataRow("[1]")]
    [DataRow("1 2")]
    [DataRow("\"5\"")]
    [DataRow("0x10")]
    public void Coerce_NonNumberText_IsLeftAlone(string sent)
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"count":{"type":"integer"}}}""").RootElement;
        var arguments = new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(sent) };

        var result = StringArgumentCoercion.Coerce(schema, arguments, out var names);

        Assert.AreSame(arguments, result);
        Assert.AreEqual(0, names.Count);
    }

    [TestMethod]
    public void Coerce_NothingToChange_ReturnsSameDictionary()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"flag":{"type":"boolean"}}}""").RootElement;
        var arguments = new Dictionary<string, JsonElement> { ["flag"] = JsonSerializer.SerializeToElement(true) };

        var result = StringArgumentCoercion.Coerce(schema, arguments, out var names);

        Assert.AreSame(arguments, result);
        Assert.AreEqual(0, names.Count);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static async Task<CallToolResult> CallProbeAsync(Dictionary<string, object?> arguments)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithTools<ProbeTool>()
            .WithStringScalarArguments();
        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<McpServer>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serverTask = server.RunAsync(cts.Token);

        await using (var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cts.Token))
        {
            var result = await client.CallToolAsync("probe", arguments, cancellationToken: cts.Token);
            await cts.CancelAsync();
            try { await serverTask; } catch (OperationCanceledException) { }
            return result;
        }
    }
}
