using System.Security.Cryptography;
using System.Text;
using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api;

// finding 3: POST create endpoints honor Idempotency-Key — stored response is replayed without
// re-running the handler. Task-2 §7: the store is the idempotency table (IIdempotencyStore over
// ErpDbContext); semantics byte-identical to the Task-1 in-memory map — stored (status, body)
// keyed (tenant, entity, key) and replayed verbatim.
//
// Review C1 fix: with a key present the whole handler runs inside ONE explicit transaction —
// reserve the key, run the handler (every service SaveChanges auto-enlists in the open
// transaction), store the response, commit. The reservation, the business write and the stored
// response therefore commit atomically: a crash cannot leave work done with no key recorded, and
// a concurrent same-key request blocks on the unique PK, then replays the winner's response.
//
// Task-5 (FINANCE-011 AC1/AC2): Wrap(ctx, payload, run) additionally stores a SHA-256 of the
// request payload. Same key + same payload replays the stored reference; same key + a different
// payload returns 409 IDEMPOTENCY_CONFLICT and creates nothing. Rows stored without a hash
// (endpoints that pass no payload, or data written before this existed) keep the plain replay.
public static class Idem
{
    // web defaults + enum strings — the canonical serialization for stored responses AND for the
    // bid content digest (028 AC1), so both sides of a replay compare like for like
    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static System.Text.Json.JsonSerializerOptions Opts => JsonOptions;

    // existing call sites — no payload hash, replay-anyway semantics unchanged
    public static IResult Wrap(HttpContext ctx, Func<(object body, int status)> run) =>
        Wrap(ctx, payload: null, run);

    public static IResult Wrap(HttpContext ctx, object? payload, Func<(object body, int status)> run)
    {
        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            var fresh = run();
            return Envelope.From(fresh.body, fresh.status);
        }
        var services = ctx.RequestServices;
        var scope = services.GetRequiredService<EntityContext>();
        var store = services.GetRequiredService<IIdempotencyStore>();
        var db = services.GetRequiredService<ErpDbContext>();
        var hash = HashOf(payload);
        if (store.TryGet(scope.TenantId, scope.EntityId, key) is { } stored)
            return Conflicts(stored.RequestHash, hash)
                ? ConflictResult()
                : Results.Content(stored.Body, "application/json", statusCode: stored.Status);

        using var tx = db.Database.BeginTransaction();
        try
        {
            store.Reserve(scope.TenantId, scope.EntityId, key, hash);
            var (body, status) = run();
            var json = System.Text.Json.JsonSerializer.Serialize(body, Opts);
            store.Complete(scope.TenantId, scope.EntityId, key, status, json, hash);
            tx.Commit();
            return Results.Content(json, "application/json", statusCode: status);
        }
        catch (IdempotencyKeyTakenException)
        {
            // race lost — not an error: the winner has committed (PG blocked our insert until
            // then), so replay its stored response — unless it carried a different payload.
            tx.Rollback();
            db.ChangeTracker.Clear();
            var winner = store.TryGet(scope.TenantId, scope.EntityId, key);
            if (winner is null)
                throw new InvalidOperationException("Idempotency key race lost but no stored response was found.");
            return Conflicts(winner.RequestHash, hash) ? ConflictResult()
                : Results.Content(winner.Body, "application/json", statusCode: winner.Status);
        }
        // any other exception: disposing the uncommitted transaction rolls the reservation and any
        // partial business writes back together.
    }

    // 011 AC2 fires only when BOTH the stored row and this request carry a hash — hashless rows
    // (legacy endpoints) keep replaying whatever payload arrives.
    private static bool Conflicts(string? storedHash, string? incomingHash) =>
        storedHash is not null && incomingHash is not null &&
        !string.Equals(storedHash, incomingHash, StringComparison.Ordinal);

    private static IResult ConflictResult() =>
        Results.Json(Envelope.FailBody("IDEMPOTENCY_CONFLICT",
            "This Idempotency-Key was already used with a different request payload."), statusCode: 409);

    // public since task-6: the bid receipt digest (ERP-PROCUREMENT-028 AC1) uses the same
    // SHA-256-of-canonical-json scheme as the idempotency store — one code path, one scheme
    public static string? HashOf(object? payload)
    {
        if (payload is null) return null;
        var json = System.Text.Json.JsonSerializer.Serialize(payload, Opts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
