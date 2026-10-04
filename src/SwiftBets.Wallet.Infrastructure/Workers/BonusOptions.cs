using System.ComponentModel.DataAnnotations;

namespace SwiftBets.Wallet.Infrastructure.Workers;

public sealed class BonusOptions
{
    public const string SectionName = "Bonus";

    /// <summary>Runs the expiry job and consumes the deposit and settlement events that trigger awards.</summary>
    public bool Enabled { get; set; } = true;

    [Range(5, 86_400)]
    public int ExpiryIntervalSeconds { get; set; } = 60;

    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 200;
}
