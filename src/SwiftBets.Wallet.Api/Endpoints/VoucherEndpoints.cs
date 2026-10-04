using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Wallet.Application.Ports;
using SwiftBets.Wallet.Application.Vouchers;
using SwiftBets.Wallet.Domain.Vouchers;

namespace SwiftBets.Wallet.Api.Endpoints;

/// <summary>Cash vouchers: customers buy and redeem under /me/wallet/vouchers; staff issue, search and void under /admin/wallet/vouchers (D168).</summary>
public static class VoucherEndpoints
{
    public const string ReadPermission = "payments.read";
    public const string WritePermission = "payments.approve";

    public static IEndpointRouteBuilder MapVouchers(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/me/wallet/vouchers").RequireAuthorization();

        me.MapGet(string.Empty, async (ClaimsPrincipal user, HttpContext context, IVoucherStore store, VoucherHandler vouchers, TimeProvider time) =>
            UserId(user) is { } userId
                ? Results.Ok((await store.ListForUserAsync(userId, 100, context.RequestAborted)).Select(v => CustomerView(v, vouchers.CodeFor(v, userId), userId, time.GetUtcNow())))
                : Results.Unauthorized());

        me.MapGet("/{voucherId:guid}", async (Guid voucherId, ClaimsPrincipal user, HttpContext context, IVoucherStore store, VoucherHandler vouchers, TimeProvider time) =>
            UserId(user) is not { } userId ? Results.Unauthorized()
            : await store.GetAsync(voucherId, context.RequestAborted) is { } v && (v.PurchasedBy == userId || v.RedeemedBy == userId)
                ? Results.Ok(CustomerView(v, vouchers.CodeFor(v, userId), userId, time.GetUtcNow()))
                : Error.NotFound("voucher_not_found", "No voucher of yours with that id.").ToHttpResult(context));

        me.MapPost(string.Empty, async (BuyBody body, ClaimsPrincipal user, HttpContext context, VoucherHandler vouchers, TimeProvider time) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Results.Unauthorized();
            }

            if (RequestKey(context) is not { } key)
            {
                return Error.Validation("idempotency_key_required", "Send an Idempotency-Key header.").ToHttpResult(context);
            }

            var outcome = await vouchers.BuyAsync(userId, key, body.Amount, body.Currency ?? "ZAR", body.RecipientEmail, context.RequestAborted);
            return outcome.Voucher is { } v ? Results.Ok(CustomerView(v, outcome.Code, userId, time.GetUtcNow())) : Refusal(outcome, context);
        });

        me.MapPost("/redeem", async (RedeemBody body, ClaimsPrincipal user, HttpContext context, VoucherHandler vouchers, TimeProvider time) =>
        {
            if (UserId(user) is not { } userId)
            {
                return Results.Unauthorized();
            }

            var outcome = await vouchers.RedeemAsync(userId, body.Code, AddressSubject(context), context.RequestAborted);
            return outcome.Voucher is { } v ? Results.Ok(CustomerView(v, null, userId, time.GetUtcNow())) : Refusal(outcome, context);
        });

        var admin = endpoints.MapGroup("/admin/wallet/vouchers");

        admin.MapGet(string.Empty, async (string? q, string? status, Guid? batchId, IVoucherStore store, TimeProvider time, HttpContext context) =>
        {
            VoucherStatus? parsed = status switch
            {
                null or "" => null,
                "active" or "expired" => VoucherStatus.Active,
                "redeemed" => VoucherStatus.Redeemed,
                "voided" => VoucherStatus.Voided,
                _ => (VoucherStatus)0,
            };
            if (parsed == 0)
            {
                return Error.Validation("invalid_status", "Status is active, expired, redeemed or voided.").ToHttpResult(context);
            }

            var term = q?.Trim();
            var code = VoucherCode.Normalise(term);
            var search = new VoucherSearch(
                code is null ? null : VoucherCode.Hash(code),
                code is null && term is { Length: 4 } ? term.ToUpperInvariant() : null,
                code is null && Guid.TryParse(term, out var userId) ? userId : null,
                batchId,
                parsed,
                200);
            if (code is null && search.Last4 is null && search.UserId is null && !string.IsNullOrEmpty(term))
            {
                return Results.Ok(Array.Empty<object>());
            }

            var now = time.GetUtcNow();
            var found = await store.SearchAsync(search, context.RequestAborted);
            return Results.Ok(found.Where(v => status is not ("active" or "expired") || v.StatusAt(now) == status).Select(v => StaffView(v, now)));
        }).RequireAuthorization(ReadPermission);

        admin.MapGet("/{voucherId:guid}", async (Guid voucherId, IVoucherStore store, TimeProvider time, HttpContext context) =>
            await store.GetAsync(voucherId, context.RequestAborted) is { } v
                ? Results.Ok(StaffView(v, time.GetUtcNow()))
                : Error.NotFound("voucher_not_found", "No voucher with that id.").ToHttpResult(context))
            .RequireAuthorization(ReadPermission);

        admin.MapPost("/{voucherId:guid}/void", async (Guid voucherId, ReasonBody body, ClaimsPrincipal user, VoucherHandler vouchers, TimeProvider time, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 400)
            {
                return Error.Validation("reason_required", "Give a reason (up to 400 characters).").ToHttpResult(context);
            }

            var outcome = await vouchers.VoidAsync(voucherId, Operator(user), body.Reason.Trim(), context.RequestAborted);
            return outcome.Voucher is { } v ? Results.Ok(StaffView(v, time.GetUtcNow())) : Refusal(outcome, context);
        }).RequireAuthorization(WritePermission);

        admin.MapGet("/batches", async (IVoucherStore store, HttpContext context) =>
            Results.Ok((await store.ListBatchesAsync(100, context.RequestAborted)).Select(BatchView)))
            .RequireAuthorization(ReadPermission);

        admin.MapPost("/batches", async (BatchBody body, ClaimsPrincipal user, VoucherHandler vouchers, TimeProvider time, HttpContext context) =>
        {
            if (RequestKey(context) is not { } key)
            {
                return Error.Validation("idempotency_key_required", "Send an Idempotency-Key header.").ToHttpResult(context);
            }

            if (string.IsNullOrWhiteSpace(body.Note) || body.Note.Length > 200)
            {
                return Error.Validation("note_required", "Say who or what the batch is for (up to 200 characters).").ToHttpResult(context);
            }

            var source = body.Source switch { "retail" => VoucherSource.Retail, "promotion" => VoucherSource.Promotion, _ => VoucherSource.Purchase };
            var (batch, refusal) = await vouchers.IssueBatchAsync(Operator(user), key, body.Count, body.Amount, body.Currency ?? "ZAR", source, body.Note, context.RequestAborted);
            return batch is null ? Refusal(refusal!, context) : Results.Ok(IssuedView(batch, time.GetUtcNow()));
        }).RequireAuthorization(WritePermission);

        // Reprinting a batch shows every code again, so it needs the write permission.
        admin.MapGet("/batches/{batchId:guid}/codes", async (Guid batchId, string? format, VoucherHandler vouchers, TimeProvider time, HttpContext context) =>
        {
            if (await vouchers.BatchCodesAsync(batchId, context.RequestAborted) is not { } batch)
            {
                return Error.NotFound("batch_not_found", "No batch with that id.").ToHttpResult(context);
            }

            if (format != "csv")
            {
                return Results.Ok(IssuedView(batch, time.GetUtcNow()));
            }

            var csv = new StringBuilder("code,amount,currency,expires,status\n");
            foreach (var (v, code) in batch.Vouchers)
            {
                csv.Append(CultureInfo.InvariantCulture, $"{VoucherCode.Format(code)},{v.Amount / 100m:0.00},{v.Currency},{v.ExpiresAt:yyyy-MM-dd},{v.StatusAt(time.GetUtcNow())}\n");
            }

            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"vouchers-{batchId}.csv");
        }).RequireAuthorization(WritePermission);

        return endpoints;
    }

    private static object CustomerView(Voucher v, string? code, Guid userId, DateTimeOffset now) => new
    {
        v.VoucherId,
        code = code is null ? null : VoucherCode.Format(code),
        v.Last4,
        v.Amount,
        v.Currency,
        status = v.StatusAt(now),
        bought = v.PurchasedBy == userId,
        redeemed = v.RedeemedBy == userId,
        v.RecipientEmail,
        v.CreatedAt,
        v.ExpiresAt,
        v.RedeemedAt,
    };

    /// <summary>Staff see who and when, and the last four characters; codes only through a batch reprint.</summary>
    private static object StaffView(Voucher v, DateTimeOffset now) => new
    {
        v.VoucherId,
        v.Last4,
        v.Amount,
        v.Currency,
        source = v.Source.ToString().ToLowerInvariant(),
        status = v.StatusAt(now),
        v.PurchasedBy,
        v.BatchId,
        v.CreatedAt,
        v.ExpiresAt,
        v.RedeemedBy,
        v.RedeemedAt,
        v.VoidedBy,
        v.VoidReason,
        v.VoidedAt,
    };

    private static object BatchView(VoucherBatch b) => new
    {
        b.BatchId, b.Count, b.Amount, b.Currency, source = b.Source.ToString().ToLowerInvariant(), b.Note, b.CreatedBy, b.CreatedAt,
    };

    private static object IssuedView(IssuedBatch batch, DateTimeOffset now) => new
    {
        batch = BatchView(batch.Batch),
        vouchers = batch.Vouchers.Select(x => new { x.Voucher.VoucherId, code = VoucherCode.Format(x.Code), status = x.Voucher.StatusAt(now), x.Voucher.ExpiresAt }),
    };

    private static IResult Refusal(VoucherOutcome outcome, HttpContext context)
    {
        if (outcome.RetryAfter is { } until)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: outcome.Message, extensions: new Dictionary<string, object?> { ["code"] = outcome.Error });
        }

        return (outcome.Error switch
        {
            "not_found" => Error.NotFound("voucher_not_found", outcome.Message!),
            "invalid_amount" or "invalid_email" or "invalid_count" or "invalid_source" or "account_not_found" => Error.Validation(outcome.Error, outcome.Message!),
            _ => Error.BusinessRule(outcome.Error!, outcome.Message!),
        }).ToHttpResult(context);
    }

    private static string? RequestKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"].ToString() is { Length: > 0 and <= 100 } key ? key : null;

    /// <summary>The caller's address, hashed: Cloudflare's header first, then the first forwarded hop, then the socket.</summary>
    public static string AddressSubject(HttpContext context)
    {
        var address = context.Request.Headers["CF-Connecting-IP"].ToString() is { Length: > 0 } cf ? cf
            : context.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim() is { Length: > 0 } forwarded ? forwarded
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return "ip:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address)))[..32];
    }

    private static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string Operator(ClaimsPrincipal user) => user.FindFirstValue("sub") ?? "unknown";

    public sealed record BuyBody(long Amount, string? Currency, string? RecipientEmail);

    public sealed record RedeemBody(string? Code);

    public sealed record ReasonBody(string? Reason);

    public sealed record BatchBody(int Count, long Amount, string? Currency, string? Source, string? Note);
}
