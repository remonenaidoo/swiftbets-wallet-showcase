namespace SwiftBets.Wallet.Domain;

/// <summary>The ledger invariants the reconciler checks; each drift names the one it found broken.</summary>
public enum DriftKind : byte
{
    /// <summary>A punter's available balance differs from the sum of its available-bucket entries.</summary>
    AvailableBalance = 1,

    /// <summary>A punter's reserved balance differs from the sum of its reserved-bucket entries.</summary>
    ReservedBalance = 2,

    /// <summary>A punter's reserved balance differs from the total of its held reservations.</summary>
    HeldReservations = 3,

    /// <summary>A posting's entries do not sum to zero, or it has fewer than two entries.</summary>
    UnbalancedPosting = 4,

    /// <summary>All entries across the ledger do not sum to zero.</summary>
    LedgerTotal = 5,

    /// <summary>A punter's bonus balance differs from the sum of its bonus-bucket entries.</summary>
    BonusBalance = 6,

    /// <summary>A voucher liability account's entries differ from the total of the vouchers still open in its currency.</summary>
    VoucherLiability = 7,
}
