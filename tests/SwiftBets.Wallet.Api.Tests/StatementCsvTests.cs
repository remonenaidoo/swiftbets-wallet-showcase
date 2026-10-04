using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Statements;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Api.Tests;

public sealed class StatementCsvTests
{
    [Fact]
    public void Lines_are_written_in_rands_with_iso_dates()
    {
        var line = new StatementLine(7, Guid.NewGuid(), PostingKind.Deposit, 12_345, -50, "pay_1", new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.FromHours(2)), 500, 1_500);

        var csv = StatementCsv.Write([line], "ZAR", _ => "deposit");

        csv.ShouldBe(StatementCsv.Header + "\r\n2026-10-01T10:30:00Z,deposit,pay_1,123.45,-0.50,5.00,15.00,ZAR\r\n");
    }

    [Fact]
    public void A_cell_that_starts_like_a_formula_is_neutralised_and_quoted_when_needed()
    {
        StatementCsv.Text("=HYPERLINK(\"x\",\"y\")").ShouldBe("\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"");
        StatementCsv.Text("@SUM(A1)").ShouldBe("'@SUM(A1)");
    }
}
