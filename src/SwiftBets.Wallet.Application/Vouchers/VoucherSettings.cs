namespace SwiftBets.Wallet.Application.Vouchers;

/// <summary>CodeSecret derives every code from its issue key, so retries and reprints get the same code but the database alone cannot.</summary>
public sealed record VoucherSettings(
    string CodeSecret,
    long MinAmount = 1_000,
    long MaxAmount = 500_000,
    int MaxBatch = 500,
    int UserFailures = 5,
    int IpFailures = 20,
    TimeSpan? Window = null,
    TimeSpan? Lockout = null)
{
    public TimeSpan FailureWindow => Window ?? TimeSpan.FromMinutes(15);

    public TimeSpan LockoutPeriod => Lockout ?? TimeSpan.FromHours(1);
}
