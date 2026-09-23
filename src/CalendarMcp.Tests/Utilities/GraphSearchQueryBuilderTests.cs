using CalendarMcp.Core.Utilities;

namespace CalendarMcp.Tests.Utilities;

[TestClass]
public class GraphSearchQueryBuilderTests
{
    [TestMethod]
    public void Build_PlainQuery_WrapsInQuotes()
    {
        var result = GraphSearchQueryBuilder.Build("hello world");
        Assert.AreEqual("\"hello world\"", result);
    }

    [TestMethod]
    public void Build_AlreadyQuotedQuery_DoesNotDoubleQuote()
    {
        var result = GraphSearchQueryBuilder.Build("\"RockBot Identity Test - Final\"");
        Assert.AreEqual("\"RockBot Identity Test - Final\"", result);
    }

    [TestMethod]
    public void Build_QuotedQueryWithSurroundingWhitespace_DoesNotDoubleQuote()
    {
        var result = GraphSearchQueryBuilder.Build("  \" invoice \"  ");
        Assert.AreEqual("\"invoice\"", result);
    }

    [TestMethod]
    public void Build_EmbeddedQuotes_AreEscaped()
    {
        var result = GraphSearchQueryBuilder.Build("from \"Bob\" invoice");
        Assert.AreEqual("\"from \\\"Bob\\\" invoice\"", result);
    }

    [TestMethod]
    public void Build_Backslash_IsEscaped()
    {
        var result = GraphSearchQueryBuilder.Build(@"C:\temp");
        Assert.AreEqual("\"C:\\\\temp\"", result);
    }
}
