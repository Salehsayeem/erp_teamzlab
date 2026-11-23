using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class JournalService(
    IJournalRepository journals, IPurchaseOrderRepository orders, IInventoryRepository inventory,
    INumberSeries numbers, IAuditRepository audit) : IJournalService
{
    public object List() => journals.All();

    public (Journal? reversal, string? code, string? message) Reverse(CurrentUser user, string journalId)
    {
        var j = journals.Find(journalId);
        if (j is null) return (null, "JOURNAL_NOT_FOUND", "Journal not found.");
        if (j.Status != JournalStatus.Posted) return (null, "JOURNAL_REVERSAL_ERROR", "Only posted journals can be reversed.");
        if (j.ReversalOfId is not null) return (null, "JOURNAL_REVERSAL_ERROR", "A reversal journal cannot be reversed.");
        if (!user.Has("journals:approve")) return (null, "FORBIDDEN", "journals:approve permission required.");

        var lines = j.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Credit, l.Debit)).ToList();
        var rev = new Journal(numbers.Next("J"), j.TenantId, j.LegalEntityId, JournalStatus.Posted,
            $"Reversal of {j.Id}: {j.Memo}", lines, DateTimeOffset.UtcNow, user.Id, j.Id);
        journals.Add(rev);

        // finding 4: receipt-derived journals must unwind their stock increment (floor 0) + emit reversal event
        var receipt = orders.FindReceiptReferencedBy(j.TenantId, j.LegalEntityId, j.Memo);
        var stockNote = "";
        if (receipt is not null)
        {
            var row = inventory.FindStockRow(receipt.TenantId, receipt.LegalEntityId, receipt.ItemSku);
            if (row is not null)
            {
                var remaining = Math.Max(0m, row.QtyOnHand - receipt.Qty);
                inventory.SetStockQty(row, remaining);
                stockNote = $"; stock {receipt.ItemSku} adjusted to {remaining}";
            }
        }

        audit.Emit(user, "finance.journal.reversed", new { journalId = j.Id, reversalId = rev.Id, receiptId = receipt?.Id });
        audit.Log(user, "reverse", "journal", rev.Id, $"Journal {j.Id} reversed by {user.Email} (linked provenance){stockNote}");
        journals.Save();
        return (rev, null, null);
    }
}
