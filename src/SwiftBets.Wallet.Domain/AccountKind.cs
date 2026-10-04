namespace SwiftBets.Wallet.Domain;

public enum AccountKind : byte
{
    Punter = 1,
    House = 2,
    Funding = 3,

    /// <summary>The cost of offers: bonus money and free bets are paid from here and forfeits return to it.</summary>
    Promotions = 4,

    /// <summary>Money owed to whoever holds an unredeemed voucher.</summary>
    VoucherLiability = 5,
}
