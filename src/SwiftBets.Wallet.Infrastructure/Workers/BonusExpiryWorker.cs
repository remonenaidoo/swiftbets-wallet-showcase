using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.Wallet.Application.Ledger;

namespace SwiftBets.Wallet.Infrastructure.Workers;

/// <summary>The bonus expiry job: forfeits expired bonuses, converts met ones, returns stray bonus money, expires free bets.</summary>
public sealed partial class BonusExpiryWorker(IServiceScopeFactory scopes, IOptions<BonusOptions> options, TimeProvider time, ILogger<BonusExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ExpiryIntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<BonusLedgerHandler>().MaintainAsync(options.Value.BatchSize, stoppingToken) is > 0 and var done)
                {
                    LogMaintained(done);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bonus expiry job closed or expired {Count} bonuses and free bets")]
    private partial void LogMaintained(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bonus expiry job failed; retrying next interval")]
    private partial void LogFailed(Exception exception);
}
