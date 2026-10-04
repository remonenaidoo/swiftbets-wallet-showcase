using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Promotions;
using SwiftBets.Wallet.Domain.Bonus;

namespace SwiftBets.Wallet.Api.Endpoints;

/// <summary>The console's promotion builder, award log and audit trail, through the gateway's /api/admin/offers routes (D153, D166).</summary>
public static class PromotionEndpoints
{
    public const string Read = "promotions.read";
    public const string Write = "promotions.write";

    public static IEndpointRouteBuilder MapPromotionAdmin(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/offers");

        admin.MapGet("", async (IPromotionStore store, CancellationToken cancellationToken) =>
            Results.Ok((await store.ListAsync(cancellationToken)).Select(View)))
            .RequireAuthorization(Read);

        admin.MapGet("/{promotionId:guid}", async (Guid promotionId, IPromotionStore store, HttpContext context) =>
            await store.GetAsync(promotionId, context.RequestAborted) is { } record ? Results.Ok(View(record)) : NotFound(context))
            .RequireAuthorization(Read);

        admin.MapPost("", (PromotionBody body, PromotionAdminHandler handler, HttpContext context) => SaveAsync(null, body, handler, context))
            .RequireAuthorization(Write);

        admin.MapPut("/{promotionId:guid}", (Guid promotionId, PromotionBody body, PromotionAdminHandler handler, HttpContext context) => SaveAsync(promotionId, body, handler, context))
            .RequireAuthorization(Write);

        admin.MapGet("/{promotionId:guid}/awards", async (Guid promotionId, IPromotionStore store, CancellationToken cancellationToken) =>
            Results.Ok((await store.AwardsAsync(promotionId, cancellationToken)).Select(a => new
            {
                a.AwardId, a.UserId, outcome = a.Outcome == AwardOutcome.Awarded ? "awarded" : "refused", a.Reason, a.Amount, a.TriggerKey, a.AwardedAt,
            })))
            .RequireAuthorization(Read);

        admin.MapGet("/{promotionId:guid}/audit", async (Guid promotionId, IPromotionStore store, CancellationToken cancellationToken) =>
            Results.Ok((await store.AuditAsync(promotionId, cancellationToken)).Select(a => new { a.Actor, a.Action, a.Detail, a.At })))
            .RequireAuthorization(Read);

        return endpoints;
    }

    public sealed record PromotionBody(
        string? Name, string? Kind, string? Currency, int MatchPercent, long MaxAward, long MinDeposit, int WageringMultiplier, decimal MinOdds,
        int SportsPercent, int CasinoPercent, int ValidDays, bool OptInRequired, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Terms, bool IsActive);

    public static object View(PromotionRecord record) => new
    {
        record.Promotion.PromotionId,
        record.Promotion.Name,
        kind = KindName(record.Promotion.Kind),
        record.Promotion.Currency,
        record.Promotion.MatchPercent,
        record.Promotion.MaxAward,
        record.Promotion.MinDeposit,
        record.Promotion.WageringMultiplier,
        minOdds = record.Promotion.MinOddsHundredths / 100m,
        sportsPercent = (int)record.Promotion.SportsPercent,
        casinoPercent = (int)record.Promotion.CasinoPercent,
        record.Promotion.ValidDays,
        record.Promotion.OptInRequired,
        record.Promotion.StartsAt,
        record.Promotion.EndsAt,
        record.Promotion.Terms,
        record.Promotion.IsActive,
        record.CreatedAt,
        record.UpdatedAt,
        record.AwardCount,
    };

    public static string KindName(PromotionKind kind) => kind == PromotionKind.DepositMatch ? "depositMatch" : "firstBetFreeBet";

    private static async Task<IResult> SaveAsync(Guid? promotionId, PromotionBody body, PromotionAdminHandler handler, HttpContext context)
    {
        PromotionKind? kind = body.Kind switch { "depositMatch" => PromotionKind.DepositMatch, "firstBetFreeBet" => PromotionKind.FirstBetFreeBet, _ => null };
        if (kind is null || body.SportsPercent is < 0 or > 100 || body.CasinoPercent is < 0 or > 100)
        {
            return Error.Validation("validation", kind is null ? "Choose what the offer gives." : "Contributions must be 0 to 100 percent.").ToHttpResult(context);
        }

        var draft = new Promotion(Guid.Empty, body.Name ?? string.Empty, kind.Value, body.Currency ?? string.Empty, body.MatchPercent, body.MaxAward, body.MinDeposit,
            body.WageringMultiplier, (int)decimal.Round(body.MinOdds * 100, MidpointRounding.AwayFromZero), (byte)body.SportsPercent, (byte)body.CasinoPercent,
            body.ValidDays, body.OptInRequired, body.StartsAt, body.EndsAt, body.Terms ?? string.Empty, body.IsActive);
        var (saved, problem) = await handler.SaveAsync(promotionId, draft, context.User.FindFirst("sub")?.Value ?? "operator", context.RequestAborted);
        return problem is not null ? Error.Validation("validation", problem).ToHttpResult(context)
            : saved is null ? NotFound(context)
            : promotionId is null ? Results.Created($"/admin/offers/{saved.Promotion.PromotionId}", View(saved))
            : Results.Ok(View(saved));
    }

    private static IResult NotFound(HttpContext context) => Error.NotFound("offer_not_found", "There is no such offer.").ToHttpResult(context);
}
