using System.Security.Cryptography;
using System.Text;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Application.Vouchers;

/// <summary>Derives a voucher's code from its issue key with a keyed hash, so only the service holding the secret can produce it.</summary>
public sealed class VoucherCodes(VoucherSettings settings)
{
    public string For(string issueKey) =>
        VoucherCode.FromSeed(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.CodeSecret), Encoding.UTF8.GetBytes(issueKey)));
}
