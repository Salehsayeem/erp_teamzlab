using System.Text.Json;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// AR aging (FINANCE-031; R9a: no payables aging — no spec card mandates it). Open AR = debit lines
// on the AR control account across posted journals, less allocations (net credit journals) applied
// FIFO oldest-invoice-first. Bucket boundaries and credit terms come from the finance_settings row
// seeded per ruling R9b — never code constants. basis=due ages by due date (invoice + terms);
// basis=invoice ages by the invoice date itself.
public sealed class FinanceAgingService(
    IJournalRepository journals, ISalesOrderRepository orders, IFinanceSettingRepository settings) : IFinanceAgingService
{
    private const string ArAccount = "1200";
    private const string SettingsKey = "aging";
    private const string SoMemoMarker = "AR invoice for SO ";

    // JSON keys in the settings row: {"bucketDays":[30,60,90],"creditTermsDays":30} — record
    // parameter names must match them (System.Text.Json matches case-insensitively)
    private sealed record AgingConfig(int[] BucketDays, int CreditTermsDays);

    private sealed record AgingEntry(Journal Journal, decimal Amount, DateOnly InvoiceDate, DateOnly DueDate, int DaysPastDue, string? CustomerId);

    private sealed record BucketView(string Label, int FromDays, int? ToDays, int Count, decimal Amount);

    public (object? aging, string? code, string? message) Receivables(DateOnly asOf, string basis)
    {
        var row = settings.Get(SettingsKey);
        if (row is null)
            return (null, "FINANCE_SETTINGS_MISSING", $"Finance setting '{SettingsKey}' is not configured for this legal entity.");
        var cfg = JsonSerializer.Deserialize<AgingConfig>(row.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (cfg is null || cfg.BucketDays.Length == 0)
            return (null, "FINANCE_SETTINGS_MISSING", $"Finance setting '{SettingsKey}' has no bucket boundaries configured.");
        var dueBasis = !basis.Equals("invoice", StringComparison.OrdinalIgnoreCase);

        var entries = OpenItems()
            .Select(r => Entry(r.Journal, r.Remaining, asOf, cfg.CreditTermsDays, dueBasis))
            .OrderBy(e => e.DueDate)
            .ToList();

        var worklist = entries.Select(e => (object)new
        {
            journalId = e.Journal.Id,
            customerId = e.CustomerId,
            invoiceDate = e.InvoiceDate,
            dueDate = e.DueDate,
            daysPastDue = e.DaysPastDue,
            amount = e.Amount,
            dunningReady = e.DaysPastDue > cfg.BucketDays[0], // past the first boundary → collection contact
        }).ToList();

        var buckets = BuildBuckets(cfg.BucketDays, entries);
        var aging = new
        {
            asOf,
            basis = dueBasis ? "due" : "invoice",
            termsDays = cfg.CreditTermsDays,
            buckets,
            totalOpen = buckets.Sum(b => b.Amount),
            worklist,
        };
        return (aging, null, null);
    }

    // per posted journal: net AR balance (debits − credits on the AR account). Net-credit journals
    // are allocations — applied FIFO to the earliest invoices; survivors carry true open amounts.
    private List<(Journal Journal, decimal Remaining)> OpenItems()
    {
        var rows = new List<(Journal Journal, decimal Remaining)>();
        var allocations = new List<(DateOnly Date, decimal Amount)>();
        foreach (var j in journals.All().Where(j => j.Status == JournalStatus.Posted).OrderBy(j => j.EffectiveDate))
        {
            var net = j.Lines.Where(l => l.AccountCode == ArAccount).Sum(l => l.Debit)
                    - j.Lines.Where(l => l.AccountCode == ArAccount).Sum(l => l.Credit);
            if (net > 0) rows.Add((j, net));
            else if (net < 0) allocations.Add((j.EffectiveDate, -net));
        }

        foreach (var (_, amount) in allocations.OrderBy(a => a.Date))
        {
            var left = amount;
            for (var i = 0; i < rows.Count && left > 0; i++)
            {
                if (rows[i].Remaining <= 0) continue;
                var take = Math.Min(rows[i].Remaining, left);
                rows[i] = (rows[i].Journal, rows[i].Remaining - take);
                left -= take;
            }
        }
        return rows.Where(r => r.Remaining > 0).ToList();
    }

    private AgingEntry Entry(Journal j, decimal amount, DateOnly asOf, int termsDays, bool dueBasis)
    {
        var invoiceDate = j.EffectiveDate;
        var dueDate = invoiceDate.AddDays(termsDays);
        var anchor = dueBasis ? dueDate : invoiceDate;
        return new AgingEntry(j, decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            invoiceDate, dueDate, asOf.DayNumber - anchor.DayNumber, CustomerFor(j));
    }

    private static List<BucketView> BuildBuckets(int[] boundaries, List<AgingEntry> entries)
    {
        var buckets = new List<BucketView>();
        for (var i = 0; i <= boundaries.Length; i++)
        {
            var inBucket = entries.Where(e => IndexFor(boundaries, e.DaysPastDue) == i).ToList();
            buckets.Add(new BucketView(
                Label(boundaries, i),
                i == 0 ? 0 : boundaries[i - 1] + 1,
                i == boundaries.Length ? null : boundaries[i],
                inBucket.Count,
                decimal.Round(inBucket.Sum(e => e.Amount), 2, MidpointRounding.AwayFromZero)));
        }
        return buckets;
    }

    private static int IndexFor(int[] boundaries, int days)
    {
        for (var i = 0; i < boundaries.Length; i++)
            if (days <= boundaries[i]) return i; // not-yet-due (negative) lands in the current bucket
        return boundaries.Length;
    }

    private static string Label(int[] boundaries, int index) =>
        index == boundaries.Length
            ? $"{boundaries[^1]}+" // ruled convention (R9b): the open bucket is labeled "90+" (holds 91+ days)
            : $"{(index == 0 ? 0 : boundaries[index - 1] + 1)}-{boundaries[index]}";

    private string? CustomerFor(Journal j) =>
        j.Memo.StartsWith(SoMemoMarker, StringComparison.Ordinal)
        && orders.Find(j.Memo[SoMemoMarker.Length..].Trim()) is { } so ? so.CustomerId : null;
}
