using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Grpc.Wallet.V1;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using WalletBase = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet.WalletBase;

namespace SwiftBets.Wallet.Api.Grpc;

/// <summary>Internal money API for placement and payout. Every mutation is keyed; expected failures are typed replies, not status codes.</summary>
[Authorize(Policy = Roles.Service)]
public sealed class WalletGrpcService(
    ReserveFundsHandler reserve, SettleReservationHandler settle, TransferHandler transfer, AccountsHandler accounts, IWalletStore store, BonusLedgerHandler bonuses, TimeProvider time) : WalletBase
{
    public override async Task<ReservationReply> Reserve(ReserveRequest request, ServerCallContext context) =>
        WalletReplies.Reservation(await reserve.HandleAsync(Key(request.IdempotencyKey), Id(request.AccountId), Amount(request.Amount), request.Amount.Currency, request.Reference, Terms(request)));

    public override async Task<ReservationReply> Capture(ReservationCommand request, ServerCallContext context) =>
        WalletReplies.Reservation(await settle.CaptureAsync(Key(request.IdempotencyKey), Id(request.ReservationId), context.CancellationToken));

    public override async Task<ReservationReply> Release(ReservationCommand request, ServerCallContext context) =>
        WalletReplies.Reservation(await settle.ReleaseAsync(Key(request.IdempotencyKey), Id(request.ReservationId), context.CancellationToken));

    public override async Task<PostingReply> Credit(PostingRequest request, ServerCallContext context) =>
        WalletReplies.Posting(await transfer.CreditAsync(Key(request.IdempotencyKey), Id(request.AccountId), Amount(request.Amount), request.Amount.Currency, request.Reference));

    public override async Task<PostingReply> Debit(PostingRequest request, ServerCallContext context) =>
        WalletReplies.Posting(await transfer.DebitAsync(Key(request.IdempotencyKey), Id(request.AccountId), Amount(request.Amount), request.Amount.Currency, request.Reference, request.Reason));

    public override async Task<BalanceReply> GetBalance(GetBalanceRequest request, ServerCallContext context) =>
        new() { Balance = WalletReplies.Balance(await accounts.BalanceAsync(Id(request.AccountId), context.CancellationToken)) };

    public override async Task<ReservationReply> GetReservation(GetReservationRequest request, ServerCallContext context)
    {
        var reservation = request.ReservationId is { Length: > 0 } id
            ? await store.GetReservationAsync(Id(id), context.CancellationToken)
            : await store.FindReservationByKeyAsync(Key(request.IdempotencyKey), context.CancellationToken);
        return reservation is null
            ? new ReservationReply { Failure = WalletReplies.Failure(Domain.WalletFailure.ReservationNotFound) }
            : new ReservationReply { Reservation = WalletReplies.Map(reservation) };
    }

    public override async Task<PostingReply> Deposit(PostingRequest request, ServerCallContext context) =>
        WalletReplies.Posting(await transfer.DepositAsync(Key(request.IdempotencyKey), Id(request.AccountId), Amount(request.Amount), request.Amount.Currency, request.Reference));

    public override async Task<ReservationReply> HoldWithdrawal(ReserveRequest request, ServerCallContext context) =>
        WalletReplies.Reservation(await reserve.HoldWithdrawalAsync(Key(request.IdempotencyKey), Id(request.AccountId), Amount(request.Amount), request.Amount.Currency, request.Reference));

    public override async Task<ReservationReply> CompleteWithdrawal(ReservationCommand request, ServerCallContext context) =>
        WalletReplies.Reservation(await settle.CompleteWithdrawalAsync(Key(request.IdempotencyKey), Id(request.ReservationId), context.CancellationToken));

    public override async Task<BalanceReply> OpenAccount(OpenAccountRequest request, ServerCallContext context)
    {
        var (account, failure) = await accounts.OpenAsync(Id(request.UserId), Currency(request.Currency), context.CancellationToken);
        return account is null
            ? new BalanceReply { Failure = WalletReplies.Failure(failure!.Value) }
            : new BalanceReply { Balance = WalletReplies.Balance(account) };
    }

    public override async Task<AccountsReply> ListAccounts(ListAccountsRequest request, ServerCallContext context)
    {
        var reply = new AccountsReply();
        reply.Accounts.AddRange((await accounts.ListAsync(Id(request.UserId), context.CancellationToken)).Select(WalletReplies.Balance));
        return reply;
    }

    /// <summary>Bonus money for a reward (payments' refer-a-friend, D199): from the promotions account, with its wagering terms.</summary>
    public override async Task<PostingReply> GrantBonus(GrantBonusRequest request, ServerCallContext context) =>
        WalletReplies.Posting(await bonuses.GrantAsync(Key(request.IdempotencyKey), Id(request.AccountId), request.Amount.Currency, BonusTerms(request, time.GetUtcNow()), request.Reference));

    /// <summary>The grant's terms; out-of-range terms are a caller bug, so InvalidArgument rather than a typed refusal.</summary>
    public static BonusGrantTerms BonusTerms(GrantBonusRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var amount = Amount(request.Amount);
        if (request.WageringMultiplier is < 0 or > 50 || request.MinOddsHundredths is < 101 or > 100_000 || request.SportsPercent is < 0 or > 100
            || request.CasinoPercent is < 0 or > 100 || request.ValidDays is < 1 or > 90)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "bonus terms are out of range."));
        }

        return new BonusGrantTerms(amount, request.WageringMultiplier, request.MinOddsHundredths, (byte)request.SportsPercent, (byte)request.CasinoPercent,
            now.AddDays(request.ValidDays), null);
    }

    /// <summary>A stake's product, lowest price and free bet; a caller that sends none gets a plain sports stake (cash only).</summary>
    private static StakeTerms Terms(ReserveRequest request) => new(
        request.Product == "casino" ? Domain.Bonus.StakeProduct.Casino : Domain.Bonus.StakeProduct.Sport,
        Math.Max(0, request.OddsHundredths),
        request.FreeBetId is { Length: > 0 } freeBet ? Id(freeBet) : null);

    private static string Currency(string currency) =>
        currency is { Length: 3 } ? currency : throw new RpcException(new Status(StatusCode.InvalidArgument, "currency must be an ISO 4217 code."));

    private static string Key(string key) =>
        key is { Length: > 0 and <= 200 } ? key : throw new RpcException(new Status(StatusCode.InvalidArgument, "idempotency_key is required (max 200 chars)."));

    private static Guid Id(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{value}' is not a valid id."));

    private static long Amount(Money? money) =>
        money is { MinorUnits: > 0, Currency.Length: 3 } ? money.MinorUnits : throw new RpcException(new Status(StatusCode.InvalidArgument, "amount must be positive with an ISO currency."));
}
