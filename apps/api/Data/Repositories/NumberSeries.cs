using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Task-2 §2: doc numbering over PG sequences — one doc_<prefix> sequence per prefix; nextval is
// atomic so concurrent requests never collide, and the SUP-0007 format stays stable.
public sealed class NumberSeries(ErpDbContext db) : INumberSeries
{
    public string Next(string prefix)
    {
        var sequence = $"doc_{prefix.ToLowerInvariant()}";
        var value = db.Database.SqlQuery<long>($"SELECT nextval({sequence}) AS \"Value\"").Single();
        return $"{prefix}-{value:D4}";
    }
}
