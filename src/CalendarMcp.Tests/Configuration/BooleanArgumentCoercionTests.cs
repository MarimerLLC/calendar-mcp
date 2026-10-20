using System.IO.Pipelines;
using System.Text.Json;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Tests.Configuration;

/// <summary>
/// Clients sometimes send booleans as the strings "true"/"false". These tests run a real MCP
/// server and client over in-memory pipes, so they cover the SDK's binding as well as the filter.
/// </summary>
[TestClass]
public class BooleanArgumentCoercionTests
{
    [McpServerToolType]
    public sealed class ProbeTool
    {
        [McpServerTool(Name = "probe")]
        public static string Probe(
            bool flag,
            bool? maybe = null,
            string text = "",
            int count = 0) =>
            $"flag={flag};maybe={maybe?.ToString() ?? "null"};text={text};count={count}";
    }

    [TestMethod]
    [DataRow("true", "True")]
    [DataRow("false", "False")]
    [DataRow("TRUE", "True")]
    [DataRow(" False ", "False")]
    public async Task StringBoolean_IsBoundAsBoolean(string sent, string expected)
    {
        var result = await CallProbeAsync(new() { ["flag"] = sent, ["maybe"] = sent });

        Assert.IsFalse(result.IsError == true, Text(result));
        Assert.AreEqual($"flag={expected};maybe={expected};text=;count=0", Text(result));
    }

    [TestMethod]
    public async Task RealBoolean_StillWorks()
    {
        var result = await CallProbeAsync(new() { ["flag"] = true });

        Assert.AreEqual("flag=True;maybe=null;text=;count=0", Text(result));
    }

    [TestMethod]
    public async Task StringParameter_KeepsTheWordTrue()
    {
        var result = await CallProbeAsync(new() { ["flag"] = false, ["text"] = "true" });

        Assert.AreEqual("flag=False;maybe=null;text=true;count=0", Text(result));
    }

    [TestMethod]
    public async Task OtherStrings_ForABoolean_StillFail()
    {
        var result = await CallProbeAsync(new() { ["flag"] = "yes" });

        Assert.IsTrue(result.IsError == true);
    }

    [TestMethod]
    public void Coerce_LeavesNonBooleanPropertiesAlone()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "flag":{"type":"boolean"},
              "either":{"type":["boolean","string"]},
              "count":{"type":"integer"}}}
            """).RootElement;
        var arguments = new Dictionary<string, JsonElement>
        {
            ["flag"] = JsonSerializer.SerializeToElement("true"),
            ["either"] = JsonSerializer.SerializeToElement("true"),
            ["count"] = JsonSerializer.SerializeToElement("true"),
            ["unknown"] = JsonSerializer.SerializeToElement("true")
        };

        var result = BooleanArgumentCoercion.Coerce(schema, arguments, out var names);

        CollectionAssert.AreEqual(new[] { "flag" }, names);
        Assert.AreEqual(JsonValueKind.True, result["flag"].ValueKind);
        Assert.AreEqual(JsonValueKind.String, result["either"].ValueKind);
        Assert.AreEqual(JsonValueKind.String, result["count"].ValueKind);
        Assert.AreEqual(JsonValueKind.String, result["unknown"].ValueKind);
        // The caller's dictionary is not modified.
        Assert.AreEqual(JsonValueKind.String, arguments["flag"].ValueKind);
    }

    [TestMethod]
    public void Coerce_NothingToChange_ReturnsSameDictionary()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"flag":{"type":"boolean"}}}""").RootElement;
        var arguments = new Dictionary<string, JsonElement> { ["flag"] = JsonSerializer.SerializeToElement(true) };

        var result = BooleanArgumentCoercion.Coerce(schema, arguments, out var names);

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
            .WithBooleanStringArguments();
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
