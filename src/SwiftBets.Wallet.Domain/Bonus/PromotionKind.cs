namespace SwiftBets.Wallet.Domain.Bonus;

public enum PromotionKind : byte
{
    /// <summary>A percentage of a deposit as bonus money, up to a cap, with wagering.</summary>
    DepositMatch = 1,

    /// <summary>A free bet when the customer's first bet settles.</summary>
    FirstBetFreeBet = 2,
}
