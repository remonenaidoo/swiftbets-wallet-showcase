namespace SwiftBets.Wallet.Domain;

public enum PostingKind : byte
{
    TopUp = 1,
    Reserve = 2,
    Capture = 3,
    Release = 4,
    Credit = 5,
    Debit = 6,
    Deposit = 7,
    WithdrawalHold = 8,
    WithdrawalPaid = 9,
    WithdrawalReturned = 10,
    BonusGranted = 11,
    BonusConverted = 12,
    BonusForfeited = 13,
    VoucherPurchase = 14,
    VoucherIssue = 15,
    VoucherRedeem = 16,
    VoucherVoid = 17,
}
