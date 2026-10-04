using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Contracts.Compliance;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Payments;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Infrastructure.Persistence;
using SwiftBets.Wallet.Infrastructure.ResponsibleGambling;
using SwiftBets.Wallet.Infrastructure.Workers;

namespace SwiftBets.Wallet.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddWalletInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSqlServerPersistence(Required(configuration, "ConnectionStrings:SbWallet"));
        services.AddKafkaMessaging(configuration);
        services.AddSqlServerOutbox(configuration);
        services.AddFaultInjection(configuration);
        services.AddSingleton<IWalletStore, SqlWalletStore>();
        services.AddSingleton<ILimitReachedNotifier, Messaging.KafkaLimitReachedNotifier>();
        services.AddSingleton<IReconciliationStore, SqlReconciliationStore>();
        services.AddCompactedState<RestrictionsChangedV1>(Topics.RestrictionsChanged);
        services.AddSingleton<IGamblingRules, CompactedGamblingRules>();
        services.AddSingleton<IBonusStore, SqlBonusStore>();
        services.AddSingleton<IPromotionStore, SqlPromotionStore>();
        services.AddValidatedOptions<BonusOptions>(configuration, BonusOptions.SectionName);
        if (configuration.GetValue($"{BonusOptions.SectionName}:{nameof(BonusOptions.Enabled)}", true))
        {
            services.AddHostedService<BonusExpiryWorker>();
            services.AddKafkaConsumer<DepositSucceededV1, Messaging.DepositAwardConsumer>(Topics.DepositSucceeded, "wallet.promotions-deposit", startAtLatest: true);
            services.AddKafkaConsumer<CouponSettledV2, Messaging.FirstBetAwardConsumer>(Topics.CouponSettledV2, "wallet.promotions-first-bet", startAtLatest: true);
        }

        services.AddValidatedOptions<ReconciliationOptions>(configuration, ReconciliationOptions.SectionName);
        if (configuration.GetValue($"{ReconciliationOptions.SectionName}:{nameof(ReconciliationOptions.Enabled)}", true))
        {
            services.AddHostedService<LedgerReconciliationWorker>();
        }

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
