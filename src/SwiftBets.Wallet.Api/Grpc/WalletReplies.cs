using SwiftBets.Contracts.Grpc.Wallet.V1;
using SwiftBets.Wallet.Application.Ledger;
using Account = SwiftBets.Wallet.Domain.Account;
using DomainReservation = SwiftBets.Wallet.Domain.Reservation;
using ProtoReservation = SwiftBets.Contracts.Grpc.Wallet.V1.Reservation;

namespace SwiftBets.Wallet.Api.Grpc;

internal static class WalletReplies
{
    public static ReservationReply Reservation(WalletOutcome outcome) =>
        outcome.Failure is { } failure
            ? new ReservationReply { Failure = Failure(failure, outcome.Detail) }
            : new ReservationReply { WasApplied = outcome.WasApplied, Reservation = Map(outcome.Reservation!) };

    public static PostingReply Posting(WalletOutcome outcome) =>
        outcome.Failure is { } failure
            ? new PostingReply { Failure = Failure(failure, outcome.Detail) }
            : new PostingReply { WasApplied = outcome.WasApplied, Posting = new SwiftBets.Contracts.Grpc.Wallet.V1.Posting { PostingId = outcome.PostingId.ToString(), Balance = Balance(outcome.Account!) } };

    public static Balance Balance(Account account) => new()
    {
        AccountId = account.AccountId.ToString(),
        Available = new Money { MinorUnits = account.Available, Currency = account.Currency },
        Reserved = new Money { MinorUnits = account.Reserved, Currency = account.Currency },
        Bonus = new Money { MinorUnits = account.Bonus, Currency = account.Currency },
        UserId = account.UserId.ToString(),
    };

    public static ProtoReservation Map(DomainReservation reservation) => new()
    {
        ReservationId = reservation.ReservationId.ToString(),
        AccountId = reservation.AccountId.ToString(),
        Amount = new Money { MinorUnits = reservation.Amount, Currency = reservation.Currency },
        Reference = reservation.Reference,
        BonusAmount = new Money { MinorUnits = reservation.BonusAmount, Currency = reservation.Currency },
        FreeBetId = reservation.FreeBetId?.ToString() ?? string.Empty,
        State = reservation.State switch
        {
            Domain.ReservationState.Held => SwiftBets.Contracts.Grpc.Wallet.V1.ReservationState.Held,
            Domain.ReservationState.Captured => SwiftBets.Contracts.Grpc.Wallet.V1.ReservationState.Captured,
            _ => SwiftBets.Contracts.Grpc.Wallet.V1.ReservationState.Released,
        },
    };

    public static SwiftBets.Contracts.Grpc.Wallet.V1.WalletFailure Failure(Domain.WalletFailure failure, string? detail = null) => new()
    {
        Code = failure switch
        {
            Domain.WalletFailure.InsufficientFunds => WalletFailureCode.InsufficientFunds,
            Domain.WalletFailure.AccountNotFound => WalletFailureCode.AccountNotFound,
            Domain.WalletFailure.AccountBlacklisted => WalletFailureCode.AccountBlacklisted,
            Domain.WalletFailure.ReservationNotFound => WalletFailureCode.ReservationNotFound,
            Domain.WalletFailure.InvalidState => WalletFailureCode.InvalidState,
            Domain.WalletFailure.CurrencyMismatch => WalletFailureCode.CurrencyMismatch,
            Domain.WalletFailure.IdempotencyConflict => WalletFailureCode.IdempotencyConflict,
            Domain.WalletFailure.LimitExceeded => WalletFailureCode.LimitExceeded,
            Domain.WalletFailure.AccountRestricted => WalletFailureCode.AccountRestricted,
            _ => WalletFailureCode.Unspecified,
        },
        Message = detail ?? failure.ToString(),
    };
}
