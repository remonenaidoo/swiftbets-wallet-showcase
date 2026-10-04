namespace SwiftBets.Wallet.Domain;

/// <summary>Held money. A stake is captured into the house; a withdrawal is paid out to the funding account.</summary>
public sealed class Reservation(Guid reservationId, Guid accountId, long amount, string currency, string reference, ReservationState state, ReservationPurpose purpose = ReservationPurpose.Stake, long bonusAmount = 0, Guid? freeBetId = null)
{
    public Guid ReservationId { get; } = reservationId;

    public Guid AccountId { get; } = accountId;

    public long Amount { get; } = amount;

    public string Currency { get; } = currency;

    public string Reference { get; } = reference;

    public ReservationState State { get; private set; } = state;

    public ReservationPurpose Purpose { get; } = purpose;

    /// <summary>The part of a stake taken from bonus money; it goes back to bonus if the stake is released.</summary>
    public long BonusAmount { get; } = bonusAmount;

    /// <summary>Set when the whole stake is a free bet, paid from the promotions account rather than the customer.</summary>
    public Guid? FreeBetId { get; } = freeBetId;

    public WalletFailure? MoveTo(ReservationState target)
    {
        if (State != ReservationState.Held)
        {
            return WalletFailure.InvalidState;
        }

        State = target;
        return null;
    }
}
