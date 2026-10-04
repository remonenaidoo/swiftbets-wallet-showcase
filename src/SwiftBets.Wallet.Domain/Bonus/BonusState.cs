namespace SwiftBets.Wallet.Domain.Bonus;

public enum BonusState : byte
{
    Active = 1,
    Converted = 2,
    Forfeited = 3,
    Expired = 4,
}
