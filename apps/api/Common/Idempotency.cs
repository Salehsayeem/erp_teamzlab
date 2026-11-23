namespace Erp.Api;

// finding 3: POST create endpoints honor Idempotency-Key — stored response is replayed without re-running the handler
public static class Idem
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts =
        new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static IResult Wrap(HttpContext ctx, Data.Db db, Func<(object body, int status)> run)
    {
        var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            var fresh = run();
            return Envelope.From(fresh.body, fresh.status);
        }
        var mapKey = $"{db.Tenant.Id}:{db.Entity.Id}:{key}";
        var stored = db.WithLock(() => db.Idempotency.TryGetValue(mapKey, out var hit) ? hit : ((int Status, string Body)?)null);
        if (stored is not null) return Results.Content(stored.Value.Body, "application/json", statusCode: stored.Value.Status);
        var (body, status) = run();
        var json = System.Text.Json.JsonSerializer.Serialize(body, Opts);
        db.WithLock(() => db.Idempotency[mapKey] = (status, json));
        return Results.Content(json, "application/json", statusCode: status);
    }
}
