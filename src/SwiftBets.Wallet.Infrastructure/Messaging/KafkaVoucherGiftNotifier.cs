using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Notifications;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Infrastructure.Messaging;

/// <summary>
/// Asks notifications to email a bought voucher to its recipient. The notification id comes from the voucher, so a
/// repeated send is dropped by notifications. A failure is logged: the buyer still has the code to pass on.
/// </summary>
public sealed partial class KafkaVoucherGiftNotifier(IEventPublisher publisher, TimeProvider time, ILogger<KafkaVoucherGiftNotifier> logger) : IVoucherGiftNotifier
{
    public const string Template = "wallet.voucher-gift";

    public async Task SendAsync(Voucher voucher, string code)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var payload = new NotificationRequestedV1(
            new Guid(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"voucher-gift:{voucher.VoucherId}"))[..16]),
            voucher.PurchasedBy ?? Guid.Empty,
            NotificationChannel.Email,
            Template,
            voucher.RecipientEmail ?? string.Empty,
            new Dictionary<string, string>
            {
                ["code"] = VoucherCode.Format(code),
                ["amount"] = $"{voucher.Currency} {(voucher.Amount / 100m).ToString("0.00", CultureInfo.InvariantCulture)}",
                ["expires"] = voucher.ExpiresAt.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
            },
            false,
            time.GetUtcNow());
        try
        {
            await publisher.PublishAsync(Topics.NotificationRequested, payload.UserId.ToString(),
                EventEnvelope<NotificationRequestedV1>.Create(payload, payload.RequestedAt, CorrelationContext.CorrelationId ?? CorrelationContext.NewId()), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogNotPublished(ex, voucher.VoucherId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gift email for voucher {VoucherId} was not requested")]
    private partial void LogNotPublished(Exception exception, Guid voucherId);
}
