using Microsoft.Extensions.Logging;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Compliance;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Money;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.ResponsibleGambling;

namespace SwiftBets.Wallet.Infrastructure.Messaging;

/// <summary>Publishes directly, since the refused transaction rolled back and left nothing to enqueue with; a failure is only logged.</summary>
public sealed partial class KafkaLimitReachedNotifier(IEventPublisher publisher, TimeProvider time, ILogger<KafkaLimitReachedNotifier> logger) : ILimitReachedNotifier
{
    public async Task NotifyAsync(LimitHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var payload = new LimitReachedV1(hit.UserId, Kind(hit.Limit.Kind), Period(hit.Limit.Period), hit.Refused, new Money(hit.Attempted, hit.Currency), time.GetUtcNow());
        try
        {
            await publisher.PublishAsync(Topics.LimitReached, hit.UserId.ToString(),
                EventEnvelope<LimitReachedV1>.Create(payload, payload.ReachedAt, CorrelationContext.CorrelationId ?? CorrelationContext.NewId()), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogNotPublished(ex, hit.UserId);
        }
    }

    private static LimitKind Kind(SpendKind kind) => kind switch
    {
        SpendKind.Deposit => LimitKind.Deposit,
        SpendKind.Stake => LimitKind.Stake,
        _ => LimitKind.Loss,
    };

    private static LimitPeriod Period(SpendPeriod period) => period switch
    {
        SpendPeriod.Day => LimitPeriod.Day,
        SpendPeriod.Week => LimitPeriod.Week,
        _ => LimitPeriod.Month,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Limit-reached notice for {UserId} was not published")]
    private partial void LogNotPublished(Exception exception, Guid userId);
}
