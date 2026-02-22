using FocusGuard.Core.Statistics;
using Xunit;

namespace FocusGuard.Core.Tests.Statistics;

public class CsvExporterTests
{
    [Fact]
    public void EscapeCsv_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", CsvExporter.EscapeCsv(""));
    }

    [Fact]
    public void EscapeCsv_SimpleString_ReturnsUnchanged()
    {
        Assert.Equal("hello", CsvExporter.EscapeCsv("hello"));
    }

    [Fact]
    public void EscapeCsv_ContainsComma_WrapsInQuotes()
    {
        Assert.Equal("\"hello, world\"", CsvExporter.EscapeCsv("hello, world"));
    }

    [Fact]
    public void EscapeCsv_ContainsQuotes_EscapesQuotes()
    {
        Assert.Equal("\"say \"\"hello\"\"\"", CsvExporter.EscapeCsv("say \"hello\""));
    }

    [Fact]
    public void EscapeCsv_ContainsNewline_WrapsInQuotes()
    {
        Assert.Equal("\"line1\nline2\"", CsvExporter.EscapeCsv("line1\nline2"));
    }

    [Fact]
    public void EscapeCsv_StartsWithEquals_PrefixesSingleQuote()
    {
        Assert.Equal("'=SUM(A1)", CsvExporter.EscapeCsv("=SUM(A1)"));
    }

    [Fact]
    public void EscapeCsv_StartsWithPlus_PrefixesSingleQuote()
    {
        Assert.Equal("'+cmd", CsvExporter.EscapeCsv("+cmd"));
    }

    [Fact]
    public void EscapeCsv_StartsWithMinus_PrefixesSingleQuote()
    {
        Assert.Equal("'-cmd", CsvExporter.EscapeCsv("-cmd"));
    }

    [Fact]
    public void EscapeCsv_StartsWithAt_PrefixesSingleQuote()
    {
        Assert.Equal("'@SUM", CsvExporter.EscapeCsv("@SUM"));
    }

    [Fact]
    public void EscapeCsv_NullString_ReturnsEmpty()
    {
        Assert.Equal("", CsvExporter.EscapeCsv(null!));
    }

    [Fact]
    public void EscapeCsv_GuidString_ReturnsUnchanged()
    {
        var guid = "12345678-1234-1234-1234-123456789012";
        Assert.Equal(guid, CsvExporter.EscapeCsv(guid));
    }

    [Fact]
    public void EscapeCsv_DateString_ReturnsUnchanged()
    {
        var date = "2025-01-15 10:30:00";
        Assert.Equal(date, CsvExporter.EscapeCsv(date));
    }
}
