namespace SwiftBets.Wallet.Domain.Vouchers;

public enum VoucherStatus : byte
{
    Active = 1,
    Redeemed = 2,
    Voided = 3,
}

/// <summary>Where a voucher's money came from: a customer's balance, cash at a retailer, or the house for a promotion.</summary>
public enum VoucherSource : byte
{
    Purchase = 1,
    Retail = 2,
    Promotion = 3,
}

/// <summary>
/// A single-use cash voucher. Its money sits in the voucher liability account of its currency until it is redeemed
/// into a punter account or voided back to <see cref="SourceAccountId"/>. <see cref="IssueKey"/> derives the code.
/// </summary>
public sealed record Voucher(
    Guid VoucherId,
    string IssueKey,
    string Last4,
    long Amount,
    string Currency,
    VoucherSource Source,
    Guid SourceAccountId,
    Guid? PurchasedBy,
    Guid? BatchId,
    string? RecipientEmail,
    VoucherStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    Guid? RedeemedBy = null,
    DateTimeOffset? RedeemedAt = null,
    string? VoidedBy = null,
    string? VoidReason = null,
    DateTimeOffset? VoidedAt = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>What the customer and staff see: open vouchers past their date show as expired.</summary>
    public string StatusAt(DateTimeOffset now) => Status switch
    {
        VoucherStatus.Active when IsExpired(now) => "expired",
        VoucherStatus.Active => "active",
        VoucherStatus.Redeemed => "redeemed",
        _ => "voided",
    };
}
