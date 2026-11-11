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

    /// <summary>Cash and bonus movements, newest first, before a sequence and within [from, to); the balances after are stored on the entries.</summary>
    Task<IReadOnlyList<StatementLine>> StatementAsync(Guid accountId, long? before, int limit, DateTimeOffset? from, DateTimeOffset? to, IReadOnlyCollection<PostingKind>? kinds, CancellationToken cancellationToken);
}

/// <summary>One posting's movement of an account's cash and bonus balances, and both balances after it.</summary>
public sealed record StatementLine(long Sequence, Guid PostingId, PostingKind Kind, long Amount, long AvailableAfter, string Reference, DateTimeOffset PostedAt, long BonusAmount = 0, long BonusAfter = 0);
