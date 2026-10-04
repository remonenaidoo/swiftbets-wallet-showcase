using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Application.Ledger;

/// <summary>
/// A customer's accounts, one per currency. The first one takes the user id as its account id, so placement and payout,
/// which address the account by user id, keep working; later currencies get new ids.
/// </summary>
public sealed class AccountsHandler(IWalletStore store)
{
    /// <summary>The currency a customer's first account opens in, and the one an unopened wallet reports.</summary>
    public const string HomeCurrency = "ZAR";

    public Task<IReadOnlyList<Account>> ListAsync(Guid userId, CancellationToken cancellationToken) => store.ListAccountsAsync(userId, cancellationToken);

    /// <summary>
    /// An account's balance. A customer who has never deposited has no account row yet; callers address the first
    /// account by user id, so an unknown id with no accounts behind it is that customer, and their balance is zero.
    /// </summary>
    public async Task<Account> BalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await store.GetAccountAsync(accountId, cancellationToken) is { } account)
        {
            return account;
        }

        var owned = await store.ListAccountsAsync(accountId, cancellationToken);
        return owned.Count > 0 ? owned[0] : new Account(accountId, AccountKind.Punter, HomeCurrency, available: 0, reserved: 0, isBlacklisted: false);
    }

    public async Task<(Account? Account, WalletFailure? Failure)> OpenAsync(Guid userId, string currency, CancellationToken cancellationToken)
    {
        if (WellKnownAccounts.HouseFor(currency) is null)
        {
            return (null, WalletFailure.CurrencyMismatch);
        }

        // Two attempts: a concurrent open of the same or another first account can take the id between list and insert.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accounts = await store.ListAccountsAsync(userId, cancellationToken);
            if (accounts.FirstOrDefault(a => a.Currency == currency) is { } existing)
            {
                return (existing, null);
            }

            var accountId = accounts.Count == 0 ? userId : Guid.CreateVersion7();
            if (await store.OpenAccountAsync(accountId, userId, currency, cancellationToken) is { } opened)
            {
                return (opened, null);
            }
        }

        return (await store.ListAccountsAsync(userId, cancellationToken)).FirstOrDefault(a => a.Currency == currency) is { } raced
            ? (raced, null)
            : (null, WalletFailure.InvalidState);
    }
}
