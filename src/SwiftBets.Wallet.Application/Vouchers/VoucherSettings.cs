namespace SwiftBets.Wallet.Application.Vouchers;

/// <summary>
/// Voucher amounts and the redeem guard. CodeSecret derives every code from its issue key, so a repeated request gets
/// the same code back and staff can reprint a batch, while the database alone cannot produce a code.
/// </summary>
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
