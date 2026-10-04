using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;
using SwiftBets.Wallet.Domain.Bonus;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Infrastructure.Persistence;

internal sealed record AccountRow(Guid AccountId, byte Kind, string Currency, long Available, long Reserved, bool IsBlacklisted, Guid? UserId, long Bonus)
{
    public Account ToDomain() => new(AccountId, (AccountKind)Kind, Currency, Available, Reserved, IsBlacklisted, UserId, Bonus);
}

internal sealed record ReservationRow(Guid ReservationId, Guid AccountId, long Amount, string Currency, string Reference, byte State, byte Purpose, long BonusAmount, Guid? FreeBetId)
{
    public Reservation ToDomain() => new(ReservationId, AccountId, Amount, Currency, Reference, (ReservationState)State, (ReservationPurpose)Purpose, BonusAmount, FreeBetId);
}

internal sealed record GrantRow(Guid GrantId, Guid AccountId, Guid? PromotionId, long Amount, long WageringTarget, long Wagered, int MinOddsHundredths, byte SportsPercent, byte CasinoPercent, DateTimeOffset ExpiresAt, byte State)
{
    public BonusGrant ToDomain() => new(GrantId, AccountId, PromotionId, Amount, WageringTarget, Wagered, MinOddsHundredths, SportsPercent, CasinoPercent, ExpiresAt, (BonusState)State);
}

internal sealed record FreeBetRow(Guid FreeBetId, Guid AccountId, Guid? PromotionId, long Amount, int MinOddsHundredths, DateTimeOffset ExpiresAt, byte State)
{
    public FreeBet ToDomain() => new(FreeBetId, AccountId, PromotionId, Amount, MinOddsHundredths, ExpiresAt, (FreeBetState)State);
}

internal sealed record StakeRow(string StakeKey, Guid AccountId, string Reference, byte Product, int OddsHundredths, long CashAmount, long BonusAmount, Guid? GrantId, Guid? FreeBetId, long FreeBetAmount, long FreeBetRetained)
{
    public BonusStake ToDomain() => new(StakeKey, AccountId, Reference, (StakeProduct)Product, OddsHundredths, CashAmount, BonusAmount, GrantId, FreeBetId, FreeBetAmount, FreeBetRetained);
}

internal sealed record StatementRow(long Sequence, Guid PostingId, byte Kind, long Amount, long AvailableAfter, string Reference, DateTimeOffset PostedAt, long BonusAmount, long BonusAfter)
{
    public StatementLine ToLine() => new(Sequence, PostingId, (PostingKind)Kind, Amount, AvailableAfter, Reference, PostedAt, BonusAmount, BonusAfter);
}

internal sealed record PostingRow(Guid PostingId, byte Kind, Guid AccountId, long Amount, Guid? ReservationId);

internal sealed record SpendRow(byte Period, long Staked, long Won, long Deposited)
{
    public SpendTotals ToTotals() => new(Staked, Won, Deposited);
}
