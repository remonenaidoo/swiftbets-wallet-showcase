using System.Security.Claims;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Statements;
using SwiftBets.Wallet.Domain;

namespace SwiftBets.Wallet.Api.Endpoints;

/// <summary>The signed-in customer's balances and statement, through the gateway's /api/me/wallet routes.</summary>
public static class CustomerEndpoints
{
    public const int MaxStatementLines = 100;

    public static IEndpointRouteBuilder MapCustomerWallet(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/me/wallet").RequireAuthorization();

        me.MapGet("/accounts", async (ClaimsPrincipal user, HttpContext context, AccountsHandler accounts) =>
            UserId(user) is { } userId
                ? Results.Ok((await accounts.ListAsync(userId, context.RequestAborted)).Select(View))
                : Unauthenticated(context));

        me.MapGet("/statement", async (string? currency, long? before, int? limit, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user, HttpContext context, AccountsHandler accounts, IWalletStore store) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            if (RangeProblem(from, to) is { } problem)
            {
                return Error.Validation("invalid_range", problem).ToHttpResult(context);
            }

            var account = (await accounts.ListAsync(userId, context.RequestAborted)).FirstOrDefault(a => currency is null || a.Currency == currency);
            if (account is null)
            {
                return Error.NotFound("account_not_found", "You have no account in that currency.").ToHttpResult(context);
            }

            var lines = await store.StatementAsync(account.AccountId, before, Math.Clamp(limit ?? 50, 1, MaxStatementLines), from, to, context.RequestAborted);
            return Results.Ok(new
            {
                account = View(account),
                lines = lines.Select(l => new { l.Sequence, l.PostingId, kind = Describe(l.Kind), l.Amount, l.AvailableAfter, l.Reference, l.PostedAt }),
                next = lines.Count == 0 ? null : (long?)lines[^1].Sequence,
            });
        });

        // The whole range as a download, built here so every line comes straight from the ledger.
        me.MapGet("/statement.csv", async (string? currency, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user, HttpContext context, AccountsHandler accounts, IWalletStore store) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            if (RangeProblem(from, to) is { } problem)
            {
                return Error.Validation("invalid_range", problem).ToHttpResult(context);
            }

            var account = (await accounts.ListAsync(userId, context.RequestAborted)).FirstOrDefault(a => currency is null || a.Currency == currency);
            if (account is null)
            {
                return Error.NotFound("account_not_found", "You have no account in that currency.").ToHttpResult(context);
            }

            var lines = new List<StatementLine>();
            long? before = null;
            while (lines.Count < MaxCsvLines)
            {
                var page = await store.StatementAsync(account.AccountId, before, 1_000, from, to, context.RequestAborted);
                lines.AddRange(page);
                if (page.Count < 1_000)
                {
                    break;
                }

                before = page[^1].Sequence;
            }

            var name = $"swiftbets-statement-{account.Currency}-{(from ?? lines.LastOrDefault()?.PostedAt)?.UtcDateTime:yyyy-MM-dd}-to-{(to ?? DateTimeOffset.UtcNow).UtcDateTime:yyyy-MM-dd}.csv";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(StatementCsv.Write(lines.Take(MaxCsvLines), account.Currency, l => Describe(l.Kind))), "text/csv; charset=utf-8", name);
        });

        return endpoints;
    }

    /// <summary>A download holds at most this many lines; a narrower range gets the rest.</summary>
    public const int MaxCsvLines = 10_000;

    private static string? RangeProblem(DateTimeOffset? from, DateTimeOffset? to) =>
        from is { } f && to is { } t && t <= f ? "to must be after from." : null;

    private static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // A token without a subject is a service token, not a customer.
    private static IResult Unauthenticated(HttpContext context) => Results.Unauthorized();

    private static object View(Account a) => new { a.AccountId, a.Currency, a.Available, a.Reserved, a.Bonus };

    private static string Describe(PostingKind kind) => kind switch
    {
        PostingKind.Reserve => "bet",
        PostingKind.Release => "betReturned",
        PostingKind.Credit => "winnings",
        PostingKind.Debit => "correction",
        PostingKind.TopUp => "topUp",
        PostingKind.Deposit => "deposit",
        PostingKind.WithdrawalHold => "withdrawal",
        PostingKind.WithdrawalReturned => "withdrawalReturned",
        _ => kind.ToString(),
    };
}
