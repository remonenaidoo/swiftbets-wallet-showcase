using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Domain.Tests;

public sealed class VoucherCodeTests
{
    private static readonly string Code = VoucherCode.FromSeed(Enumerable.Range(1, 32).Select(i => (byte)(i * 7)).ToArray());

    [Fact]
    public void A_code_typed_in_lower_case_with_spaces_reads_back_as_the_same_code()
    {
        var typed = VoucherCode.Format(Code).ToLowerInvariant().Replace('-', ' ');

        VoucherCode.Normalise(typed).ShouldBe(Code);
        Code.ShouldNotContain('0');
        Code.ShouldNotContain('O');
    }

    [Fact]
    public void One_wrong_character_fails_the_check_character()
    {
        var wrong = (Code[0] == 'A' ? 'B' : 'A') + Code[1..];

        VoucherCode.Normalise(wrong).ShouldBeNull();
    }
}
