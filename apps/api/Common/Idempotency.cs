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
public static class Idem
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IResult Wrap(HttpContext ctx, Func<(object body, int status)> run)
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
        if (store.TryGet(scope.TenantId, scope.EntityId, key) is { } stored)
            return Results.Content(stored.Body, "application/json", statusCode: stored.Status);

        using var tx = db.Database.BeginTransaction();
        try
        {
            store.Reserve(scope.TenantId, scope.EntityId, key);
            var (body, status) = run();
            var json = System.Text.Json.JsonSerializer.Serialize(body, Opts);
            store.Complete(scope.TenantId, scope.EntityId, key, status, json);
            tx.Commit();
            return Results.Content(json, "application/json", statusCode: status);
        }
        catch (IdempotencyKeyTakenException)
        {
            // race lost — not an error: the winner has committed (PG blocked our insert until
            // then), so replay its stored response.
            tx.Rollback();
            db.ChangeTracker.Clear();
            var winner = store.TryGet(scope.TenantId, scope.EntityId, key);
            return winner is not null
                ? Results.Content(winner.Value.Body, "application/json", statusCode: winner.Value.Status)
                : throw new InvalidOperationException("Idempotency key race lost but no stored response was found.");
        }
        // any other exception: disposing the uncommitted transaction rolls the reservation and any
        // partial business writes back together.
    }
}
