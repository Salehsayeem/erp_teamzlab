using Erp.Api.Interfaces;
using Erp.Api.Data;

namespace Erp.Api.Services;

public sealed class JournalService(Db db) : IJournalService
{
    public object List() => db.WithLock(() => db.Journals);

    public (Journal? reversal, string? code, string? message) Reverse(CurrentUser user, string journalId)
    {
        var j = db.Journals.FirstOrDefault(x => x.TenantId == db.Tenant.Id && x.LegalEntityId == db.Entity.Id && x.Id == journalId);
        if (j is null) return (null, "JOURNAL_NOT_FOUND", "Journal not found.");
        if (j.Status != JournalStatus.Posted) return (null, "JOURNAL_REVERSAL_ERROR", "Only posted journals can be reversed.");
        if (j.ReversalOfId is not null) return (null, "JOURNAL_REVERSAL_ERROR", "A reversal journal cannot be reversed.");
        if (!user.Has("journals:approve")) return (null, "FORBIDDEN", "journals:approve permission required.");

        var lines = j.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Credit, l.Debit)).ToList();
        var rev = new Journal(db.NextDoc("J"), j.TenantId, j.LegalEntityId, JournalStatus.Posted,
            $"Reversal of {j.Id}: {j.Memo}", lines, DateTimeOffset.UtcNow, user.Id, j.Id);
        db.Journals.Add(rev);

        // finding 4: receipt-derived journals must unwind their stock increment (floor 0) + emit reversal event
        var receipt = db.Receipts.FirstOrDefault(r => r.TenantId == j.TenantId && r.LegalEntityId == j.LegalEntityId
            && j.Memo.Contains(r.Id, StringComparison.Ordinal));
        var stockNote = "";
        if (receipt is not null)
        {
            var idx = db.Stock.FindIndex(s => s.TenantId == receipt.TenantId && s.LegalEntityId == receipt.LegalEntityId && s.ItemSku == receipt.ItemSku);
            if (idx >= 0)
            {
                var remaining = Math.Max(0m, db.Stock[idx].QtyOnHand - receipt.Qty);
                db.Stock[idx] = db.Stock[idx] with { QtyOnHand = remaining };
                stockNote = $"; stock {receipt.ItemSku} adjusted to {remaining}";
            }
        }

        Audit.Emit(db, user, "finance.journal.reversed", new { journalId = j.Id, reversalId = rev.Id, receiptId = receipt?.Id });
        Audit.Log(db, user, "reverse", "journal", rev.Id, $"Journal {j.Id} reversed by {user.Email} (linked provenance){stockNote}");
        return (rev, null, null);
    }
}
