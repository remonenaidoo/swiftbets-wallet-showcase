using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Payments;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Wallet.Application.Promotions;

namespace SwiftBets.Wallet.Infrastructure.Messaging;

/// <summary>A successful deposit may earn a deposit match.</summary>
public sealed class DepositAwardConsumer(AwardHandler awards) : IEventHandler<DepositSucceededV1>
{
    public Task HandleAsync(ConsumedEvent<DepositSucceededV1> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var deposit = message.Envelope.Payload;
        return awards.OnDepositAsync(deposit.PaymentId, deposit.UserId, deposit.Amount.MinorUnits, deposit.Amount.Currency, deposit.CompletedAt, cancellationToken);
    }
}

/// <summary>The first settlement of a customer's first bet may earn a free bet.</summary>
public sealed class FirstBetAwardConsumer(AwardHandler awards) : IEventHandler<CouponSettledV2>
{
    public Task HandleAsync(ConsumedEvent<CouponSettledV2> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settled = message.Envelope.Payload;
        return awards.OnCouponSettledAsync(settled.CouponId, settled.PunterId, settled.SettlementVersion, settled.TotalStake.Currency, settled.SettledAt, cancellationToken);
    }
}
