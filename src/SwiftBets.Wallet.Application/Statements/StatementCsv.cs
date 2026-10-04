using System.Globalization;
using System.Text;
using SwiftBets.Wallet.Application.Ports;

namespace SwiftBets.Wallet.Application.Statements;

/// <summary>A customer's statement as CSV: ISO 8601 UTC dates, amounts in major units (rands), text cells safe to open in a spreadsheet.</summary>
public static class StatementCsv
{
    public const string Header = "date,type,reference,amount,balance,currency";

    public static string Write(IEnumerable<StatementLine> lines, string currency, Func<StatementLine, string> describe)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(describe);
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var line in lines)
        {
            csv.Append(line.PostedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(',')
                .Append(Text(describe(line))).Append(',')
                .Append(Text(line.Reference)).Append(',')
                .Append(Major(line.Amount)).Append(',')
                .Append(Major(line.AvailableAfter)).Append(',')
                .Append(Text(currency)).Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>Cents to rands with two decimals and a plain minus; numbers are ours, so they are never escaped.</summary>
    public static string Major(long minorUnits) => (minorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A text cell: a leading formula character gets a quote prefix (formula injection), then RFC 4180 quoting.</summary>
    public static string Text(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '|' or '%')
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
