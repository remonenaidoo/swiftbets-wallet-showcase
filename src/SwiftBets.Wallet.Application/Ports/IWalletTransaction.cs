using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Application.Ports;

public interface IWalletTransaction : IAsyncDisposable
{
    /// <summary>Looks the key up without locking; a concurrent duplicate is caught by the unique index on save.</summary>
    Task<StoredPosting?> FindPostingAsync(string idempotencyKey);

    Task<Account?> LockAccountAsync(Guid accountId);

    Task<Reservation?> LockReservationAsync(Guid reservationId);

    /// <summary>Opens an empty punter account if none exists and returns it locked.</summary>
    Task<Account> OpenPunterAccountAsync(Guid accountId, string currency);

    /// <exception cref="DuplicateIdempotencyKeyException">Another transaction committed the same key.</exception>
    Task SaveAsync(Posting posting, StoredPosting record, IReadOnlyList<Account> changedAccounts, Reservation? reservation, bool isNewReservation);

    /// <summary>The account's totals for the periods containing <paramref name="now"/>; call it with the account locked.</summary>
    Task<SpendCounters> GetSpendAsync(Guid accountId, DateTimeOffset now);

    /// <summary>Adds to the day, week and month totals containing <paramref name="at"/>; amounts may be negative.</summary>
    Task AddSpendAsync(Guid accountId, DateTimeOffset at, long staked, long won, long deposited);

    Task<DateTimeOffset> ReservedAtAsync(Guid reservationId);

    /// <summary>Stores a new voucher under its code's hash, in the same transaction as the posting that funds it.</summary>
    Task InsertVoucherAsync(Voucher voucher, byte[] codeHash);

    /// <summary>Reads a voucher with an update lock held to the end of the transaction, so a redeem and a void cannot both win.</summary>
    Task<Voucher?> LockVoucherAsync(Guid voucherId);

    Task UpdateVoucherAsync(Voucher voucher);

    /// <summary>The account's active bonus grant; call it with the account locked, which also guards the grant.</summary>
    Task<BonusGrant?> ActiveGrantAsync(Guid accountId);

    Task<BonusGrant?> GetGrantAsync(Guid grantId);

    Task SaveGrantAsync(BonusGrant grant, bool isNew);

    Task<FreeBet?> LockFreeBetAsync(Guid freeBetId);

    Task SaveFreeBetAsync(FreeBet freeBet, bool isNew);

    /// <summary>The sports stake a coupon reference was placed with, when an offer was involved.</summary>
    Task<BonusStake?> FindStakeAsync(Guid accountId, string reference);

    Task<BonusStake?> LatestCasinoStakeAsync(Guid accountId);

    /// <summary>Inserts the stake, or (not new) records how much of its free bet has been kept back.</summary>
    Task SaveStakeAsync(BonusStake stake, bool isNew);

    Task CommitAsync();
}
