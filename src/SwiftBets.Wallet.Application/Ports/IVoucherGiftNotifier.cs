using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Application.Ports;

/// <summary>Emails a bought voucher to the recipient the buyer named; best effort, after the purchase has committed.</summary>
public interface IVoucherGiftNotifier
{
    Task SendAsync(Voucher voucher, string code);
}
