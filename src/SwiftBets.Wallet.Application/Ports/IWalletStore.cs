using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Ports;

public interface IWalletStore
{
    Task<IWalletTransaction> BeginAsync();

    Task<Account?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<Reservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<Reservation?> FindReservationByKeyAsync(string reserveIdempotencyKey, CancellationToken cancellationToken);

    /// <summary>A blacklisted punter keeps their balance but receives no credits; payouts to them are dead-lettered.</summary>
    Task<bool> SetBlacklistedAsync(Guid accountId, bool isBlacklisted, CancellationToken cancellationToken);

    Task<IReadOnlyList<Account>> ListAccountsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Opens an empty punter account; null when the id or the (user, currency) pair is already taken.</summary>
    Task<Account?> OpenAccountAsync(Guid accountId, Guid userId, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// The account's available-balance movements, newest first, before the <paramref name="before"/> sequence and posted from
    /// <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive). The balance after each is over the whole ledger.
    /// </summary>
    Task<IReadOnlyList<StatementLine>> StatementAsync(Guid accountId, long? before, int limit, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

/// <summary>One movement of an account's available balance and the balance after it.</summary>
public sealed record StatementLine(long Sequence, Guid PostingId, PostingKind Kind, long Amount, long AvailableAfter, string Reference, DateTimeOffset PostedAt);
