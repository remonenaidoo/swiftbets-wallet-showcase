using SwiftBets.Wallet.Application.Statements;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Api.Tests;

/// <summary>A statement is always a bounded range (D196).</summary>
public sealed class StatementWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_dates_means_the_last_30_days()
    {
        var window = StatementWindow.Resolve(null, null, Now);

        window.Problem.ShouldBeNull();
        window.To.ShouldBe(Now);
        window.From.ShouldBe(Now.AddDays(-30));
    }

    [Fact]
    public void Twelve_months_is_allowed_and_a_day_more_is_refused()
    {
        StatementWindow.Resolve(Now.AddMonths(-12), Now, Now).Problem.ShouldBeNull();
        StatementWindow.Resolve(Now.AddMonths(-12).AddDays(-1), Now, Now).Problem.ShouldNotBeNull();
    }

    [Fact]
    public void A_known_type_maps_to_its_ledger_kinds_and_an_unknown_one_is_refused()
    {
        StatementWindow.TryKinds("wins", out var wins).ShouldBeTrue();
        wins.ShouldBe([PostingKind.Credit]);
        StatementWindow.TryKinds("casino", out _).ShouldBeFalse();
    }

    [Fact]
    public void An_immediate_csv_covers_90_days_and_not_more()
    {
        StatementWindow.FitsImmediateCsv(Now.AddDays(-90), Now).ShouldBeTrue();
        StatementWindow.FitsImmediateCsv(Now.AddDays(-91), Now).ShouldBeFalse();
    }
}
