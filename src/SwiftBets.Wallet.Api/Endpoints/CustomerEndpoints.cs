using System.Security.Claims;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Wallet.Application.Ledger;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Promotions;
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

        // A statement is always a date range (D196): the last 30 days by default, at most 12 months, filtered by type in the wallet.
        me.MapGet("/statement", async (string? currency, long? before, int? limit, DateTimeOffset? from, DateTimeOffset? to, string? type, ClaimsPrincipal user, HttpContext context, AccountsHandler accounts, IWalletStore store, TimeProvider time) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            var window = StatementWindow.Resolve(from, to, time.GetUtcNow());
            if (window.Problem is { } problem)
            {
                return Error.Validation("invalid_range", problem).ToHttpResult(context);
            }

            if (!StatementWindow.TryKinds(type, out var kinds))
            {
                return Error.Validation("invalid_type", $"type must be one of {string.Join(", ", StatementWindow.Types.Keys)}.").ToHttpResult(context);
            }

            var account = (await accounts.ListAsync(userId, context.RequestAborted)).FirstOrDefault(a => currency is null || a.Currency == currency);
            if (account is null)
            {
                return Error.NotFound("account_not_found", "You have no account in that currency.").ToHttpResult(context);
            }

            var size = Math.Clamp(limit ?? 50, 1, MaxStatementLines);
            var lines = await store.StatementAsync(account.AccountId, before, size, window.From, window.To, context.RequestAborted, kinds);
            return Results.Ok(new
            {
                account = View(account),
                from = window.From,
                to = window.To,
                lines = lines.Select(l => new { l.Sequence, l.PostingId, kind = Describe(l), l.Amount, l.AvailableAfter, l.BonusAmount, l.BonusAfter, l.Reference, l.PostedAt }),
                next = lines.Count < size ? null : (long?)lines[^1].Sequence,
            });
        });

        // The range as a download, built here so every line comes straight from the ledger. Immediate only up to 90 days and
        // 10 000 lines; longer exports wait for object storage (D196).
        me.MapGet("/statement.csv", async (string? currency, DateTimeOffset? from, DateTimeOffset? to, string? type, ClaimsPrincipal user, HttpContext context, AccountsHandler accounts, IWalletStore store, TimeProvider time) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            var window = StatementWindow.Resolve(from, to, time.GetUtcNow());
            if (window.Problem is { } problem)
            {
                return Error.Validation("invalid_range", problem).ToHttpResult(context);
            }

            if (!StatementWindow.FitsImmediateCsv(window.From, window.To))
            {
                return Error.Validation("export_range_too_long", $"Exports over {StatementWindow.MaxCsvDays} days are coming soon. Pick {StatementWindow.MaxCsvDays} days or fewer.").ToHttpResult(context);
            }

            if (!StatementWindow.TryKinds(type, out var kinds))
            {
                return Error.Validation("invalid_type", $"type must be one of {string.Join(", ", StatementWindow.Types.Keys)}.").ToHttpResult(context);
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
                var page = await store.StatementAsync(account.AccountId, before, 1_000, window.From, window.To, context.RequestAborted, kinds);
                lines.AddRange(page);
                if (page.Count < 1_000)
                {
                    break;
                }

                before = page[^1].Sequence;
            }

            var name = $"swiftbets-statement-{account.Currency}-{window.From.UtcDateTime:yyyy-MM-dd}-to-{window.To.UtcDateTime:yyyy-MM-dd}.csv";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(StatementCsv.Write(lines.Take(MaxCsvLines), account.Currency, Describe)), "text/csv; charset=utf-8", name);
        });

        // The active bonus, its wagering and the free bets; forfeitOnWithdrawal is what a withdrawal request would forfeit.
        me.MapGet("/bonuses", async (string? currency, ClaimsPrincipal user, HttpContext context, AccountsHandler accounts, IBonusStore bonuses, IPromotionStore promotions, TimeProvider time) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            var account = (await accounts.ListAsync(userId, context.RequestAborted)).FirstOrDefault(a => currency is null || a.Currency == currency);
            if (account is null)
            {
                return Error.NotFound("account_not_found", "You have no account in that currency.").ToHttpResult(context);
            }

            var grant = await bonuses.ActiveGrantAsync(account.AccountId, context.RequestAborted);
            var freeBets = await bonuses.AvailableFreeBetsAsync(account.AccountId, time.GetUtcNow(), context.RequestAborted);
            var names = (await promotions.ListAsync(context.RequestAborted)).ToDictionary(r => r.Promotion.PromotionId, r => r.Promotion.Name);
            string? Name(Guid? id) => id is { } key && names.TryGetValue(key, out var name) ? name : null;
            return Results.Ok(new
            {
                account.Currency,
                bonusBalance = account.Bonus,
                forfeitOnWithdrawal = account.Bonus,
                bonus = grant is null ? null : new
                {
                    grant.GrantId, grant.Amount, grant.WageringTarget, grant.Wagered, minOdds = grant.MinOddsHundredths / 100m,
                    sportsPercent = (int)grant.SportsPercent, casinoPercent = (int)grant.CasinoPercent, grant.ExpiresAt, promotionName = Name(grant.PromotionId),
                },
                freeBets = freeBets.Select(f => new { f.FreeBetId, f.Amount, account.Currency, minOdds = f.MinOddsHundredths / 100m, f.ExpiresAt, promotionName = Name(f.PromotionId) }),
            });
        });

        me.MapGet("/offers", async (ClaimsPrincipal user, HttpContext context, CustomerOffersHandler offers) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            var list = await offers.ListAsync(userId, context.RequestAborted);
            return Results.Ok(new
            {
                list.MarketingOptedOut,
                list.Restricted,
                offers = list.Offers.Select(o => new
                {
                    o.Promotion.PromotionId, o.Promotion.Name, kind = PromotionEndpoints.KindName(o.Promotion.Kind), o.Promotion.Currency, o.Promotion.MatchPercent,
                    o.Promotion.MaxAward, o.Promotion.MinDeposit, o.Promotion.WageringMultiplier, minOdds = o.Promotion.MinOddsHundredths / 100m,
                    sportsPercent = (int)o.Promotion.SportsPercent, casinoPercent = (int)o.Promotion.CasinoPercent, o.Promotion.ValidDays, o.Promotion.OptInRequired,
                    o.OptedIn, o.Awarded, o.Promotion.StartsAt, o.Promotion.EndsAt, o.Promotion.Terms,
                }),
            });
        });

        me.MapPost("/offers/{promotionId:guid}/opt-in", async (Guid promotionId, ClaimsPrincipal user, HttpContext context, CustomerOffersHandler offers) =>
            UserId(user) is not { } userId ? Unauthenticated(context)
            : await offers.OptInAsync(userId, promotionId, context.RequestAborted) switch
            {
                null => Results.NoContent(),
                "not_found" => Error.NotFound("offer_not_found", "That offer is not running.").ToHttpResult(context),
                "restricted" => new Error("account_restricted", "Offers are not available on your account right now.", ErrorKind.Forbidden).ToHttpResult(context),
                _ => Error.Conflict("marketing_opted_out", "Turn offers back on to opt in.").ToHttpResult(context),
            });

        me.MapDelete("/offers/{promotionId:guid}/opt-in", async (Guid promotionId, ClaimsPrincipal user, HttpContext context, CustomerOffersHandler offers) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            await offers.OptOutAsync(userId, promotionId, context.RequestAborted);
            return Results.NoContent();
        });

        me.MapPut("/marketing", async (MarketingBody body, ClaimsPrincipal user, HttpContext context, CustomerOffersHandler offers) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Unauthenticated(context);
            }

            await offers.SetMarketingOptedOutAsync(userId, body.OptedOut, context.RequestAborted);
            return Results.Ok(new { optedOut = body.OptedOut });
        });

        return endpoints;
    }

    public sealed record MarketingBody(bool OptedOut);

    /// <summary>A download holds at most this many lines; a narrower range gets the rest.</summary>
    public const int MaxCsvLines = 10_000;

    private static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // A token without a subject is a service token, not a customer.
    private static IResult Unauthenticated(HttpContext context) => Results.Unauthorized();

    private static object View(Account a) => new { a.AccountId, a.Currency, a.Available, a.Reserved, a.Bonus };

    private static string Describe(StatementLine line) =>
        line.Kind == PostingKind.Reserve && line.Amount == 0 && line.BonusAmount == 0 ? "freeBet" : Describe(line.Kind);

    private static string Describe(PostingKind kind) => kind switch
    {
        PostingKind.BonusGranted => "bonusGranted",
        PostingKind.BonusConverted => "bonusConverted",
        PostingKind.BonusForfeited => "bonusForfeited",
        PostingKind.Reserve => "bet",
        PostingKind.Release => "betReturned",
        PostingKind.Credit => "winnings",
        PostingKind.Debit => "correction",
        PostingKind.TopUp => "topUp",
        PostingKind.Deposit => "deposit",
        PostingKind.WithdrawalHold => "withdrawal",
        PostingKind.WithdrawalReturned => "withdrawalReturned",
        PostingKind.VoucherPurchase => "voucherBought",
        PostingKind.VoucherRedeem => "voucherRedeemed",
        PostingKind.VoucherVoid => "voucherRefunded",
        _ => kind.ToString(),
    };
}
