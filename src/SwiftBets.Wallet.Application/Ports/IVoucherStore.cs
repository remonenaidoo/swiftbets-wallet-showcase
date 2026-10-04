using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Application.Ports;

/// <summary>Voucher reads, issue batches and the redeem attempt counters; money-moving writes go through <see cref="IWalletTransaction"/>.</summary>
public interface IVoucherStore
{
    Task<Voucher?> GetAsync(Guid voucherId, CancellationToken cancellationToken);

    Task<Voucher?> FindByHashAsync(byte[] codeHash, CancellationToken cancellationToken);

    Task<Voucher?> FindByIssueKeyAsync(string issueKey, CancellationToken cancellationToken);

    /// <summary>Vouchers the customer bought or redeemed, newest first.</summary>
    Task<IReadOnlyList<Voucher>> ListForUserAsync(Guid userId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Voucher>> SearchAsync(VoucherSearch search, CancellationToken cancellationToken);

    /// <summary>Records the batch under its key, or returns the batch already recorded with that key.</summary>
    Task<VoucherBatch> SaveBatchAsync(VoucherBatch batch, CancellationToken cancellationToken);

    Task<VoucherBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VoucherBatch>> ListBatchesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>When the first of <paramref name="subjects"/> still locked out is free again, or null when none is.</summary>
    Task<DateTimeOffset?> LockedUntilAsync(IReadOnlyList<string> subjects, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Counts a failed attempt in the subject's window and locks it out once the window holds <paramref name="maxFailures"/>.</summary>
    Task RecordFailureAsync(string subject, int maxFailures, DateTimeOffset now, TimeSpan window, TimeSpan lockout, CancellationToken cancellationToken);

    Task ClearFailuresAsync(string subject, CancellationToken cancellationToken);
}

/// <summary>Staff search: by code (its hash), the last four characters, a customer, a batch or a status; all optional.</summary>
public sealed record VoucherSearch(byte[]? CodeHash, string? Last4, Guid? UserId, Guid? BatchId, Domain.Vouchers.VoucherStatus? Status, int Limit);

/// <summary>A run of vouchers staff issued together for a retailer or a promotion.</summary>
public sealed record VoucherBatch(Guid BatchId, string Key, int Count, long Amount, string Currency, VoucherSource Source, string Note, string CreatedBy, DateTimeOffset CreatedAt);
