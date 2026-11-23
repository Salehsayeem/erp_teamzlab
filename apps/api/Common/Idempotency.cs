using Erp.Api.Interfaces;

namespace Erp.Api;

// finding 3: POST create endpoints honor Idempotency-Key — stored response is replayed without
// re-running the handler. Task-2 §7: the store is the idempotency table (IIdempotencyStore over
// ErpDbContext); semantics byte-identical to the Task-1 in-memory map — stored (status, body)
// keyed (tenant, entity, key) and replayed verbatim.
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
        var stored = store.TryGet(scope.TenantId, scope.EntityId, key);
        if (stored is not null) return Results.Content(stored.Value.Body, "application/json", statusCode: stored.Value.Status);
        var (body, status) = run();
        var json = System.Text.Json.JsonSerializer.Serialize(body, Opts);
        store.Set(scope.TenantId, scope.EntityId, key, status, json);
        return Results.Content(json, "application/json", statusCode: status);
    }
}
