using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Task-2 §2: doc numbering over PG sequences — one doc_<prefix> sequence per prefix; nextval is
// atomic so concurrent requests never collide, and the SUP-0007 format stays stable.
// P5 (016): the admin view reads pg_sequences (no second prefix list to drift) and voids are
// recorded bookkeeping — the sequence itself never reissues a voided number.
public sealed class NumberSeries(ErpDbContext db, EntityContext? ec = null) : INumberSeries
{
    // direct constructions (tests) keep the seed scope; DI passes the scoped request context
    private readonly EntityContext _ec = ec ?? new EntityContext();

    public string Next(string prefix)
    {
        var sequence = $"doc_{prefix.ToLowerInvariant()}";
        var value = db.Database.SqlQuery<long>($"SELECT nextval({sequence}) AS \"Value\"").Single();
        return $"{prefix}-{value:D4}";
    }

    public List<NumberSeriesView> List()
    {
        var voidCounts = db.NumberSeriesVoids
            .Where(v => v.TenantId == _ec.TenantId && v.LegalEntityId == _ec.EntityId)
            .GroupBy(v => v.Prefix)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionary(x => x.Key, x => x.Count);
        var rows = db.Database.SqlQuery<SequenceRow>(
                $"SELECT sequencename AS \"Name\", last_value AS \"LastValue\" FROM pg_sequences WHERE schemaname = CURRENT_SCHEMA() AND sequencename LIKE 'doc\\_%' ORDER BY sequencename")
            .ToList();
        return rows.Select(r =>
        {
            var prefix = r.Name[4..].ToUpperInvariant();
            return new NumberSeriesView(prefix, r.LastValue, $"{prefix}-{(r.LastValue ?? 0):D4}", voidCounts.GetValueOrDefault(prefix));
        }).ToList();
    }

    public bool Exists(string prefix) => db.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_sequences WHERE schemaname = CURRENT_SCHEMA() AND sequencename = {"doc_" + prefix.ToLowerInvariant()}) AS \"Value\"")
        .Single();

    public bool AlreadyVoided(string prefix, string number) => db.NumberSeriesVoids.Any(v =>
        v.TenantId == _ec.TenantId && v.LegalEntityId == _ec.EntityId && v.Prefix == prefix && v.Number == number);

    public void AddVoid(NumberSeriesVoid v) => db.NumberSeriesVoids.Add(v);

    private sealed record SequenceRow(string Name, long? LastValue);
}
