namespace SwiftBets.Wallet.Domain;

/// <summary>An account's balances. Punter balances never go negative; the house and funding sides may. Moves return why they were refused, or null.</summary>
public sealed class Account
{
    public Account(Guid accountId, AccountKind kind, string currency, long available, long reserved, bool isBlacklisted, Guid? userId = null, long bonus = 0)
    {
        AccountId = accountId;
        UserId = userId ?? accountId;
        Bonus = bonus;
        Kind = kind;
        Currency = currency;
        Available = available;
        Reserved = reserved;
        IsBlacklisted = isBlacklisted;
    }

    public Guid AccountId { get; }

    /// <summary>The customer who owns a punter account; a customer has one account per currency.</summary>
    public Guid UserId { get; }

    public AccountKind Kind { get; }

    public string Currency { get; }

    public long Available { get; private set; }

    public long Reserved { get; private set; }

    /// <summary>Promotional money: stakeable where its offer allows, after cash, and never withdrawable.</summary>
    public long Bonus { get; private set; }

    public bool IsBlacklisted { get; }

    public WalletFailure? Reserve(long amount, string currency) =>
        Validate(amount, currency) ?? (Available < amount ? WalletFailure.InsufficientFunds : Apply(-amount, amount));

    /// <summary>Holds a stake from cash first and the rest from bonus money, if bonus money may be used; returns the bonus part.</summary>
    public (WalletFailure? Failure, long FromBonus) ReserveCashThenBonus(long amount, string currency, bool bonusUsable)
    {
        var taken = TakeCashThenBonus(amount, currency, bonusUsable);
        if (taken.Failure is null)
        {
            Reserved += amount;
        }

        return taken;
    }

    /// <summary>Pays straight out (a casino stake) from cash first, then bonus money if it may be used; returns the bonus part.</summary>
    public (WalletFailure? Failure, long FromBonus) PayCashThenBonus(long amount, string currency, bool bonusUsable) =>
        TakeCashThenBonus(amount, currency, bonusUsable);

    /// <summary>Holds a free bet's stake: the money comes from the promotions account, not the customer.</summary>
    public WalletFailure? HoldFreeBet(long amount, string currency) =>
        Validate(amount, currency) ?? Apply(0, amount);

    /// <summary>Gives a stake back split as it was taken: bonus to bonus, a free bet to promotions, the rest to cash.</summary>
    public WalletFailure? ReturnReservation(long amount, long toBonus, long toPromotions)
    {
        if (Reserved < amount || toBonus < 0 || toPromotions < 0 || toBonus + toPromotions > amount)
        {
            return WalletFailure.InvalidState;
        }

        Bonus += toBonus;
        return Apply(amount - toBonus - toPromotions, -amount);
    }

    /// <summary>Credits money split between cash and bonus; a blacklisted punter receives nothing.</summary>
    public WalletFailure? ReceiveSplit(long amount, string currency, long cash, long bonus)
    {
        var failure = Validate(amount, currency) ?? (Kind == AccountKind.Punter && IsBlacklisted ? WalletFailure.AccountBlacklisted : null);
        if (failure is not null || cash < 0 || bonus < 0 || cash + bonus > amount)
        {
            return failure ?? WalletFailure.InvalidState;
        }

        Bonus += bonus;
        return Apply(cash, 0);
    }

    /// <summary>Adds bonus money from an offer.</summary>
    public WalletFailure? ReceiveBonus(long amount, string currency)
    {
        if (Validate(amount, currency) is { } invalid)
        {
            return invalid;
        }

        Bonus += amount;
        return null;
    }

    /// <summary>Empties the bonus balance (forfeit, expiry or conversion) and returns what it held.</summary>
    public long TakeBonus()
    {
        var taken = Bonus;
        Bonus = 0;
        return taken;
    }

    /// <summary>Wagering is met: the bonus balance becomes cash; returns the amount converted.</summary>
    public long ConvertBonus()
    {
        var converted = TakeBonus();
        Available += converted;
        return converted;
    }

    public WalletFailure? ConsumeReservation(long amount) =>
        Reserved < amount ? WalletFailure.InvalidState : Apply(0, -amount);

    public WalletFailure? Receive(long amount, string currency) =>
        Validate(amount, currency) ?? (Kind == AccountKind.Punter && IsBlacklisted ? WalletFailure.AccountBlacklisted : Apply(amount, 0));

    public WalletFailure? Pay(long amount, string currency) =>
        Validate(amount, currency) ?? (Kind == AccountKind.Punter && Available < amount ? WalletFailure.InsufficientFunds : Apply(-amount, 0));

    private WalletFailure? Validate(long amount, string currency) =>
        amount <= 0 ? WalletFailure.InvalidAmount
        : !string.Equals(currency, Currency, StringComparison.Ordinal) ? WalletFailure.CurrencyMismatch
        : null;

    private (WalletFailure? Failure, long FromBonus) TakeCashThenBonus(long amount, string currency, bool bonusUsable)
    {
        if (Validate(amount, currency) is { } invalid)
        {
            return (invalid, 0);
        }

        var fromBonus = Math.Max(0, amount - Math.Max(0, Available));
        if (fromBonus > 0 && (!bonusUsable || Bonus < fromBonus))
        {
            return (WalletFailure.InsufficientFunds, 0);
        }

        Available -= amount - fromBonus;
        Bonus -= fromBonus;
        return (null, fromBonus);
    }

    private WalletFailure? Apply(long availableDelta, long reservedDelta)
    {
        Available += availableDelta;
        Reserved += reservedDelta;
        return null;
    }
}
