using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

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
        // P5 (002/003/006): the brief's enforcement point is CREATE — accounts, currency scale
        // and required dimensions are validated per line at draft time (approval rechecks).
        var lineError = ValidateLines(request.Lines, request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow));
        if (lineError is not null) return (null, lineError.Value.code, lineError.Value.message);
        var journal = new Journal(numbers.Next("J"), ec.TenantId, ec.EntityId, JournalStatus.Pending,
            request.Memo,
            request.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Debit, l.Credit, l.TaxCodeId, 0,
                l.Currency?.Trim().ToUpperInvariant(), l.AmountInCurrency, l.Dimensions)).ToList(),
            DateTimeOffset.UtcNow, user.Id, reversalOfId: null, accountingDate: request.Date);
        journals.Add(journal);
        audit.Log(user, "create", "journal", journal.Id,
            $"Manual journal drafted: {journal.Lines.Count} line(s), accounting date {journal.EffectiveDate:yyyy-MM-dd}");
        journals.Save();
        return (journal, null, null);
    }

    // 002 AC1 posting eligibility + 003 required dimensions + 006 currency scale, line-level:
    // every error names the line index (and the offending dimension/currency) so the entry form
    // can point at the row. Approval rechecks account eligibility at commit (ResolveAccounts).
    private (string code, string message)? ValidateLines(IReadOnlyList<Dtos.JournalLineInput> lines, DateOnly date)
    {
        var required = db.DimensionSets
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Required && s.Active)
            .OrderBy(s => s.Code).ToList();
        var allSets = db.DimensionSets
            .Where(s => s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId)
            .ToDictionary(s => s.Code);
        var values = db.DimensionValues
            .Where(v => v.TenantId == ec.TenantId && v.LegalEntityId == ec.EntityId).ToList();
        // 007 (ratified ruling): the functional currency is a single-row upsert — a pending
        // change overwrites the row, so a draft dated BEFORE its EffectiveFrom has no previous
        // value to resolve against and is rejected; drafts on/after the date use the new value.
        var setting = db.FinanceSettings.FirstOrDefault(s =>
                s.TenantId == ec.TenantId && s.LegalEntityId == ec.EntityId && s.Key == CurrencyService.FunctionalCurrencyKey);
        var functional = setting?.Value ?? EntityContext.SeedEntity.Currency;
        if (setting?.EffectiveFrom is { } effectiveFrom && date < effectiveFrom)
            return ("VALIDATION_ERROR",
                $"Rejected: functional currency change to {functional} effective {effectiveFrom:yyyy-MM-dd}; draft dated {date:yyyy-MM-dd}.");
        var functionalCurrency = db.Currencies.FirstOrDefault(c =>
            c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId && c.Code == functional);
        // F3 ruling (owner, 2026-11-27): fail CLOSED — if the functional currency has no row in
        // the currency master, the Debit/Credit settlement-scale check cannot run, and silently
        // skipping it is exactly the hole the check exists to close.
        if (functionalCurrency is null)
            return ("VALIDATION_ERROR",
                $"Functional currency {functional} has no row in the currency master; the settlement-scale check cannot run — configure it before posting.");
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var account = accounts.FindByCode(l.AccountCode);
            if (account is null)
                return ("ACCOUNT_NOT_FOUND", $"Line {i + 1}: account {l.AccountCode} is not in the chart of accounts.");
            if (!account.DirectPostingAllowed)
                return ("ACCOUNT_NOT_POSTABLE", $"Line {i + 1}: account {account.Code} {account.Name} does not allow direct posting.");

            var currencyCode = l.Currency?.Trim().ToUpperInvariant() ?? functional;
            var currency = db.Currencies.FirstOrDefault(c =>
                c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId && c.Code == currencyCode);
            if (currency is null)
                return ("CURRENCY_NOT_FOUND", $"Line {i + 1}: currency {currencyCode} is not in the currency master.");
            if (!currency.Active)
                return ("VALIDATION_ERROR", $"Line {i + 1}: currency {currency.Code} is inactive; new transactions may not cite it.");
            // 006 fix round: Debit/Credit are FUNCTIONAL-currency amounts — the transaction value
            // is stored independently in AmountInCurrency, so the LINE currency's settlement scale
            // governs only AmountInCurrency, while Debit/Credit answer to the functional scale.
            if (l.AmountInCurrency is { } txn && ScaleOf(txn) > currency.DecimalScale)
                return ("VALIDATION_ERROR",
                    $"Line {i + 1}: amount {txn} exceeds the {currency.DecimalScale}-decimal settlement scale of {currency.Code}.");
            if (functionalCurrency is not null) // always non-null here — fail-closed guard above
                foreach (var amount in new[] { l.Debit, l.Credit })
                    if (amount is { } v && ScaleOf(v) > functionalCurrency.DecimalScale)
                        return ("VALIDATION_ERROR",
                            $"Line {i + 1}: amount {v} exceeds the {functionalCurrency.DecimalScale}-decimal functional scale of {functionalCurrency.Code}.");

            foreach (var set in required)
                if (l.Dimensions is null || !l.Dimensions.TryGetValue(set.Code, out _))
                    return ("VALIDATION_ERROR",
                        $"Line {i + 1}: required dimension '{set.Code}' ({set.Name}) is missing on {date:yyyy-MM-dd}.");
            foreach (var (setCode, dimValue) in l.Dimensions ?? [])
            {
                if (!allSets.ContainsKey(setCode))
                    return ("VALIDATION_ERROR", $"Line {i + 1}: dimension '{setCode}' is not defined for this legal entity.");
                // 003 fix round: new postings (any date) must cite an ACTIVE value in interval —
                // posted lines are never re-validated, so history keeps inactive-but-valid references.
                if (!values.Any(v => v.SetCode == setCode && v.Value == dimValue && v.Active
                        && v.EffectiveFrom <= date && (v.EffectiveTo is null || v.EffectiveTo >= date)))
                    return ("VALIDATION_ERROR",
                        $"Line {i + 1}: dimension {setCode} value '{dimValue}' is not permitted on {date:yyyy-MM-dd}.");
            }
        }
        return null;
    }

    private static int ScaleOf(decimal amount)
    {
        var scale = 0;
        while (amount != decimal.Truncate(amount) && scale < 28) { amount *= 10; scale++; }
        return scale;
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
        try
        {
            journals.Save();
        }
        catch (DbUpdateConcurrencyException)
        {
            // money-hardening: the Pending→Posted UPDATE is status-guarded and matched ZERO rows —
            // another officer approved this journal while our snapshot was stale. Nothing above
            // commits: no duplicated control lines, no second approval audit/outbox trail.
            return (null, "JOURNAL_ALREADY_POSTED",
                $"Journal {journalId} was already posted; the retry carried a stale pending snapshot and wrote nothing.");
        }
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

        // money-hardening: the existence check above is read-then-write — two concurrent reversals
        // could both pass it before either commits, then BOTH post a reversal journal and unwind
        // the receipt stock twice. Reverse is multi-write, so check+commit are serialized per
        // SOURCE row (the InvoiceService exposure path): SELECT … FOR UPDATE inside the request
        // transaction (joined, never nested, when Idem.Wrap opened one), then the AUTHORITATIVE
        // re-check under the lock before anything is written.
        var ownsTx = db.Database.CurrentTransaction is null;
        using var tx = ownsTx ? db.Database.BeginTransaction() : null;
        db.Journals
            .FromSql($"SELECT * FROM journals WHERE id = {journalId} AND tenant_id = {ec.TenantId} AND legal_entity_id = {ec.EntityId} FOR UPDATE")
            .ToList();
        if (journals.HasReversal(journalId))
            return (null, "JOURNAL_ALREADY_REVERSED", $"Journal {journalId} has already been reversed; a retry must not reverse it twice.");

        // a reversal posts at today — the same lock applies (R9d: no posting into any closed month)
        var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, DateOnly.FromDateTime(DateTime.UtcNow));
        if (guardCode is not null) return (null, guardCode, guardMsg);

        var lines = j.Lines.Select(l => new JournalLine(l.AccountCode, l.AccountName, l.Credit, l.Debit, l.TaxCodeId, l.TaxAmount, l.Currency, l.AmountInCurrency, l.Dimensions)).ToList();
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
        if (ownsTx) tx!.Commit();
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
