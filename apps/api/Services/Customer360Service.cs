using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Customer 360 completion (ERP-SALES-012/013/014): composite detail with hierarchy rollups,
// onboarding controls, and authorized party merge with redirects. Merge authority is
// customers:merge (R-ADMIN, Q-S3); posted balances block a merge (014 edge).
public sealed class Customer360Service(
    ICustomerRepository customers, ICustomer360Repository c360, ISalesOrderRepository orders,
    IInvoiceRepository invoices, ICollectionService collections, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec) : ICustomer360Service
{
    public (object? data, string? code, string? message) Detail(CurrentUser u, string customerId)
    {
        var customer = customers.Find(customerId);
        if (customer is null) return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        // 014: a merged identifier resolves to the survivor — the read shows both facts
        var survivor = customer.MergedIntoId is { } sid ? customers.Find(sid) : null;
        var subject = survivor ?? customer;

        var parentId = subject.ParentCustomerId;
        var childrenIds = c360.ChildrenIds(subject.Id);
        // 012 AC2: the rollup names the entities it includes (self + direct children)
        var included = childrenIds.Append(subject.Id).OrderBy(x => x).ToList();
        var openOrders = orders.OpenForCustomer(subject.Id);
        var rollupTotal = childrenIds.Append(subject.Id).Sum(id =>
            orders.OpenForCustomer(id).Sum(o => CreditMath.OrderTotal(o)));

        // exposure respects viewer authorization (credit:read) — omitted otherwise (012 AC2)
        object? exposure = null;
        if (u.Has("credit:read"))
        {
            var (data, _, _) = collections.CreditExposure(subject.Id);
            exposure = data;
        }
        var posted = invoices.AllInvoices(InvoiceStatus.Posted, subject.Id);

        return (new
        {
            subject.Id, subject.Name, subject.TaxId, subject.Email, subject.CreditLimit,
            subject.PaymentTermNetDays, subject.RemindersSuppressed,
            mergedIntoId = customer.MergedIntoId,
            hierarchy = new { parentId, childrenIds, includedEntityIds = included, rollupOpenOrderTotal = decimal.Round(rollupTotal, 2, MidpointRounding.AwayFromZero) },
            openOrders = openOrders.Select(o => new { o.Id, o.Status, total = CreditMath.OrderTotal(o) }),
            openInvoiceCount = posted.Count,
            openInvoiceTotal = decimal.Round(posted.Sum(i => CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id))), 2, MidpointRounding.AwayFromZero),
            exposure,
            onboardingChecks = c360.ChecksFor(subject.Id).Select(ch => new
            {
                ch.CheckCode, ch.Mandatory, ch.Status, ch.MetAt, ch.MetBy,
            }),
        }, null, null);
    }

    public (object? data, string? code, string? message) ChecksList(string customerId)
    {
        if (!customers.Exists(customerId)) return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        return (new { checks = c360.ChecksFor(customerId).Select(CheckView) }, null, null);
    }

    // 012 AC1: self-parent and any indirect cycle are rejected — walk the proposed parent chain
    public (object? data, string? code, string? message) SetHierarchy(CurrentUser u, string customerId, string? parentId)
    {
        var customer = customers.Find(customerId);
        if (customer is null) return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        if (parentId is null)
        {
            c360.SetParent(customer, null);
            audit.Log(u, "hierarchy_clear", "customer", customerId, "Parent link cleared");
            c360.Save();
            return (new { customerId, parentId = (string?)null }, null, null);
        }
        if (parentId == customerId)
            return (null, "CUSTOMER_HIERARCHY_CYCLE", "A customer cannot be its own parent.");
        var parent = customers.Find(parentId);
        if (parent is null) return (null, "CUSTOMER_NOT_FOUND", "Parent customer does not exist.");
        var walker = parent;
        var hops = 0;
        while (walker is not null && hops++ < 100)
        {
            if (walker.Id == customerId)
                return (null, "CUSTOMER_HIERARCHY_CYCLE", $"Setting this parent would close a cycle through {walker.Id}.");
            walker = walker.ParentCustomerId is { } next ? customers.Find(next) : null;
        }
        c360.SetParent(customer, parentId);
        audit.Log(u, "hierarchy_set", "customer", customerId, $"Parent set to {parentId}");
        c360.Save();
        return (new { customerId, parentId }, null, null);
    }

    public (object? data, string? code, string? message) AddOnboardingCheck(CurrentUser u, string customerId, string checkCode, bool mandatory)
    {
        if (!customers.Exists(customerId)) return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        var existing = c360.FindCheck(customerId, checkCode);
        if (existing is not null)
            return (CheckView(existing), null, null); // idempotent re-add
        var check = new CustomerOnboardingCheck($"{customerId}:{checkCode}", ec.TenantId, ec.EntityId,
            customerId, checkCode, mandatory, OnboardingCheckStatus.Pending, null, null, u.Id, DateTimeOffset.UtcNow);
        c360.AddCheck(check);
        audit.Log(u, "add_onboarding_check", "customer", customerId, $"{checkCode} (mandatory: {mandatory})");
        c360.Save();
        return (CheckView(check), null, null);
    }

    public (object? data, string? code, string? message) MarkCheckMet(CurrentUser u, string customerId, string checkCode)
    {
        var check = c360.FindCheck(customerId, checkCode);
        if (check is null) return (null, "ONBOARDING_CHECK_NOT_FOUND", "Onboarding check not found for this customer.");
        c360.SetCheckMet(check, u.Id);
        audit.Log(u, "onboarding_check_met", "customer", customerId, $"{checkCode} marked met by {u.Email}");
        c360.Save();
        return (CheckView(check), null, null);
    }

    // 014 AC1: preview lists every reassignment before anything moves; no writes to masters
    public (object? data, string? code, string? message) MergePreview(CurrentUser u, string survivorId, string mergedId, string reason)
    {
        if (survivorId == mergedId)
            return (null, "VALIDATION_ERROR", "Survivor and merged customer must differ.");
        if (!customers.Exists(survivorId) || !customers.Exists(mergedId))
            return (null, "CUSTOMER_NOT_FOUND", "Both customers must exist.");
        var blocked = MergeBlockReason(mergedId);
        if (blocked is not null) return (null, blocked.Value.Code, blocked.Value.Message);
        var merge = new PartyMerge($"PMG-{Guid.NewGuid():N}"[..12], ec.TenantId, ec.EntityId,
            survivorId, mergedId, reason, PartyMergeStatus.Previewed, null, u.Id, DateTimeOffset.UtcNow);
        c360.AddMerge(merge);
        audit.Log(u, "merge_preview", "customer", mergedId, $"Merge preview into {survivorId}: {reason}");
        c360.Save();
        return (MergeView(merge), null, null);
    }

    // 014 AC2: execution re-checks posted balances, moves open aggregates, redirects the id
    public (object? data, string? code, string? message) MergeExecute(CurrentUser u, string previewId)
    {
        if (!u.Has("customers:merge")) return (null, "FORBIDDEN", "customers:merge permission required.");
        var merge = c360.FindMerge(previewId);
        if (merge is null) return (null, "MERGE_NOT_FOUND", "Merge preview not found.");
        if (merge.Status != PartyMergeStatus.Previewed)
            return (null, "MERGE_STATE_ERROR", $"Only previewed merges can execute (current: {merge.Status}).");
        var blocked = MergeBlockReason(merge.MergedId);
        if (blocked is not null) return (null, blocked.Value.Code, blocked.Value.Message);

        var reassignments = ReassignmentCounts(merge.MergedId);
        c360.ReassignToSurvivor(merge.MergedId, merge.SurvivorId);
        var merged = customers.Find(merge.MergedId);
        if (merged is null) return (null, "CUSTOMER_NOT_FOUND", "Merged customer disappeared mid-merge.");
        c360.SetMergedInto(merged, merge.SurvivorId);
        c360.SetMergeExecuted(merge, u.Id);
        audit.Log(u, "merge_execute", "customer", merge.MergedId,
            $"Merged into {merge.SurvivorId} ({string.Join(", ", reassignments.Select(r => $"{r.Key}={r.Value}"))})");
        audit.Emit(u, "sales.customer.merged", new { survivorId = merge.SurvivorId, mergedId = merge.MergedId, executedBy = u.Id });
        c360.Save();
        return (MergeView(merge), null, null);
    }

    private (string Code, string Message)? MergeBlockReason(string mergedId)
    {
        if (c360.HasPostedInvoices(mergedId))
            return ("MERGE_BLOCKED", "The merged account carries posted invoices — posted balances block a merge (014 edge); credit the documents first.");
        return null;
    }

    private Dictionary<string, int> ReassignmentCounts(string mergedId) => new()
    {
        ["openOrders"] = c360.OpenOrderCount(mergedId),
        ["openOpportunities"] = c360.OpenOpportunityCount(mergedId),
        ["openCases"] = c360.OpenCaseCount(mergedId),
        ["onboardingChecks"] = c360.ChecksFor(mergedId).Count,
    };

    private static object CheckView(CustomerOnboardingCheck check) => new
    {
        check.CustomerId, check.CheckCode, check.Mandatory, check.Status, check.MetAt, check.MetBy,
    };

    private object MergeView(PartyMerge merge) => new
    {
        merge.Id, merge.SurvivorId, merge.MergedId, merge.Reason, merge.Status,
        reassignments = ReassignmentCounts(merge.MergedId),
        requestedBy = merge.RequestedBy, requestedAt = merge.RequestedAt,
        executedBy = merge.ExecutedBy, executedAt = merge.ExecutedAt,
    };
}
