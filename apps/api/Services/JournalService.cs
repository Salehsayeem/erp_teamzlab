using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Manual journals (task-5): draft → approval posts. Approval rechecks EVERYTHING at commit time
// (002 AC1): maker-checker (013), period lock (005, R9e), posting eligibility, balance after tax
// control lines (009), and computes tax from the revision effective at the accounting date (032/035).
public sealed class JournalService(
    IJournalRepository journals, IPurchaseOrderRepository orders, IInventoryRepository inventory,
    INumberSeries numbers, IAuditRepository audit, IAccountRepository accounts, ITaxCodeRepository taxCodes,
    ErpDbContext db, EntityContext ec) : IJournalService
{
    // control accounts mandated by the brief for VAT posting (rates live in tax_codes data, R9c)
    private const string OutputVatAccount = "2200";
    private const string InputVatAccount = "1450";

    public object List() => journals.All();

    public (Journal? journal, string? code, string? message) Create(CurrentUser user, Dtos.CreateJournalRequest request)
    {
        if (!user.Has("journals:write")) return (null, "FORBIDDEN", "journals:write permission required.");
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Pending,
            request.Memo,
            request.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Debit, l.Credit, l.TaxCodeId)).ToList(),
            DateTimeOffset.UtcNow, user.Id, reversalOfId: null, accountingDate: request.Date);
        journals.Add(journal);
        audit.Log(user, "create", "journal", journal.Id,
            $"Manual journal drafted: {journal.Lines.Count} line(s), accounting date {journal.EffectiveDate:yyyy-MM-dd}");
        journals.Save();
        return (journal, null, null);
    }

    public (Journal? journal, string? code, string? message) Approve(CurrentUser approver, string journalId, string? reason)
    {
        var j = journals.Find(journalId);
        if (j is null) return (null, "JOURNAL_NOT_FOUND", "Journal not found.");
        if (j.Status != JournalStatus.Pending) return (null, "JOURNAL_APPROVAL_ERROR", $"Only pending journals can be approved (current: {j.Status}).");
        if (j.CreatedBy == approver.Id) return (null, "JOURNAL_APPROVAL_ERROR", "Maker-checker: the preparer cannot approve their own journal.");
        if (!approver.Has("journals:approve")) return (null, "FORBIDDEN", "journals:approve permission required.");

        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, j.EffectiveDate);
        if (guardCode is not null) return (null, guardCode, guardMsg);

        var accountError = ResolveAccounts(j);
        if (accountError is not null) return (null, accountError.Value.code, accountError.Value.message);

        var (control, taxError) = TaxControl(j, j.EffectiveDate);
        if (taxError is not null) return (null, taxError.Value.code, taxError.Value.message);

        var unbalanced = BalanceMessage(j, control);
        if (unbalanced is not null) return (null, "JOURNAL_UNBALANCED", unbalanced);

        journals.MarkPosted(j, control);
        audit.Log(approver, "approve", "journal", j.Id,
            $"Journal {j.Id} approved and posted by {approver.Email}: {j.Lines.Count} lines, {j.Lines.Sum(l => l.Debit):0.00} Dr" +
            (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}"));
        audit.Emit(approver, "finance.journal.approved", new { journalId = j.Id, approver = approver.Id, reason });
        journals.Save();
        return (j, null, null);
    }

    public (Journal? reversal, string? code, string? message) Reverse(CurrentUser user, string journalId)
    {
        var j = journals.Find(journalId);
        if (j is null) return (null, "JOURNAL_NOT_FOUND", "Journal not found.");
        if (j.Status != JournalStatus.Posted) return (null, "JOURNAL_REVERSAL_ERROR", "Only posted journals can be reversed.");
        if (j.ReversalOfId is not null) return (null, "JOURNAL_REVERSAL_ERROR", "A reversal journal cannot be reversed.");
        if (journals.HasReversal(journalId))
            return (null, "JOURNAL_ALREADY_REVERSED", $"Journal {journalId} has already been reversed; a retry must not reverse it twice.");
        if (!user.Has("journals:approve")) return (null, "FORBIDDEN", "journals:approve permission required.");

        // a reversal posts at today — the same lock applies (R9d: no posting into any closed month)
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, guardCode, guardMsg);

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

    // 002 AC1: every submitted line's account must exist and allow direct posting — checked at
    // commit, not only at draft creation
    private (string code, string message)? ResolveAccounts(Journal j)
    {
        foreach (var line in j.Lines)
        {
            var account = accounts.FindByCode(line.AccountCode);
            if (account is null) return ("ACCOUNT_NOT_FOUND", $"Account {line.AccountCode} is not in the chart of accounts.");
            if (!account.DirectPostingAllowed)
                return ("ACCOUNT_NOT_POSTABLE", $"Account {account.Code} {account.Name} does not allow direct posting.");
        }
        return null;
    }

    // 032/035: per-line tax from the revision effective at the accounting date; deterministic
    // decimal rounding (AwayFromZero, scale 2). TaxAmount stores the SIGNED value
    // Round((Debit−Credit) × RatePct/100, 2); control lines carry |tax| on the matching side.
    private (List<JournalLine> control, (string code, string message)? error) TaxControl(Journal j, DateOnly date)
    {
        var control = new List<JournalLine>();
        for (var i = 0; i < j.Lines.Count; i++) // record props are init-only — replace by index
        {
            var line = j.Lines[i];
            if (string.IsNullOrWhiteSpace(line.TaxCodeId)) continue;
            // citation may be a revision id or the bare code ("VAT-STD-BD") — either way the
            // EFFECTIVE revision at the accounting date is what gets stored (032 provenance)
            var cited = taxCodes.Find(line.TaxCodeId)
                ?? taxCodes.ByCode(line.TaxCodeId).FirstOrDefault();
            if (cited is null) return (control, ("TAX_CODE_NOT_FOUND", $"Tax code {line.TaxCodeId} does not exist."));
            var revision = taxCodes.EffectiveFor(cited.Code, date);
            if (revision is null)
                return (control, ("TAX_CODE_NOT_EFFECTIVE", $"No revision of tax code {cited.Code} is effective on {date:yyyy-MM-dd}."));
            var account = accounts.FindByCode(line.AccountCode)!;
            if (account.Type is not (AccountType.Revenue or AccountType.Expense or AccountType.Asset))
                return (control, ("VALIDATION_ERROR", $"Tax code {revision.Code} is not allowed on account {account.Code} ({account.Type})."));

            var tax = decimal.Round((line.Debit - line.Credit) * revision.RatePct / 100m, 2, MidpointRounding.AwayFromZero);
            // 032 provenance: the resolved revision row pins rate + version
            j.Lines[i] = new JournalLine(line.AccountCode, line.AccountName, line.Debit, line.Credit, revision.Id, tax);
            if (tax == 0m) continue; // zero/exempt basis (or 0%) — nothing to post
            if (account.Type == AccountType.Revenue)
                control.Add(ControlLine(OutputVatAccount, "VAT Payable", tax, revision.Id));
            else if (revision.Recoverable)
                control.Add(ControlLine(InputVatAccount, "Input VAT Recoverable", tax, revision.Id));
            // non-recoverable input tax (039 trim) stays in the line's cost — no control line
        }
        return (control, null);
    }

    private static JournalLine ControlLine(string accountCode, string accountName, decimal tax, string revisionId) =>
        tax > 0
            ? new JournalLine(accountCode, accountName, tax, 0, revisionId, tax)
            : new JournalLine(accountCode, accountName, 0, -tax, revisionId, tax);

    // 009 AC1: balance is enforced AFTER tax control lines — the difference is shown in the message
    private static string? BalanceMessage(Journal j, List<JournalLine> control)
    {
        var debits = j.Lines.Sum(l => l.Debit) + control.Sum(l => l.Debit);
        var credits = j.Lines.Sum(l => l.Credit) + control.Sum(l => l.Credit);
        return debits == credits
            ? null
            : $"Journal does not balance: debits {debits:0.00}, credits {credits:0.00}, difference {Math.Abs(debits - credits):0.00}.";
    }
}
