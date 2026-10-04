using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Promotions;
using SwiftBets.Wallet.Application.Reconciliation;
using SwiftBets.Wallet.Application.Vouchers;

namespace SwiftBets.Wallet.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddWalletApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PostingRunner>();
        services.AddScoped<ReserveFundsHandler>();
        services.AddScoped<SettleReservationHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<AccountsHandler>();
        services.AddScoped<ReconcileLedgerHandler>();
        services.AddSingleton<VoucherCodes>();
        services.AddScoped<VoucherHandler>();
        services.AddScoped<BonusLedgerHandler>();
        services.AddScoped<OfferEligibility>();
        services.AddScoped<PromotionAdminHandler>();
        services.AddScoped<CustomerOffersHandler>();
        services.AddScoped<AwardHandler>();
        return services;
    }
}
