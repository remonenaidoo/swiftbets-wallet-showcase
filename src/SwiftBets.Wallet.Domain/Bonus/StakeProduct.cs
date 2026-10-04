namespace SwiftBets.Wallet.Domain.Bonus;

/// <summary>What a stake is on; each bonus says how much of a stake on each counts towards its wagering.</summary>
public enum StakeProduct : byte
{
    Sport = 1,
    Casino = 2,
}
