using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// P1 requisitions (011–019): tolerant drafts, validated submission with budget gate + route
// resolution, fixed maker-checker decisions with versioned tokens (Q-P2/018), time/scope-bounded
// delegations (015), urgent/sole-source exception flags (019).
public sealed class RequisitionService(
    IRequisitionRepository requisitions, IApprovalRouteService routes, IBudgetChecker budget,
    IProcurementCatalogService catalog, INumberSeries numbers, IAuditRepository audit,
    IIdentityRepository identities, EntityContext ec) : IRequisitionService
{
    public object List(RequisitionStatus? status) =>
        requisitions.All(status).Select(ProjRequisition).ToList();

    public (Requisition? requisition, string? code, string? message) Create(
        CurrentUser u, string? purpose, string? currency, IReadOnlyList<RequisitionLineInput>? lines)
    {
        // drafts save incomplete (011 AC1) — no line validation here; submission is the gate
        var requisition = new Requisition(numbers.Next("REQ"), ec.TenantId, ec.EntityId, purpose ?? "",
            RequisitionStatus.Draft, 1, u.Id, DateTimeOffset.UtcNow, Currency: string.IsNullOrWhiteSpace(currency) ? "BDT" : currency);
        foreach (var l in lines ?? [])
            requisition.Lines.Add(new RequisitionLine(numbers.Next("RQL"), l.CategoryId ?? "", l.Description ?? "",
                l.Qty, l.UnitPrice, l.Unit ?? "", l.NeedBy ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), l.Qty, l.Unit ?? ""));
        requisitions.Add(requisition);
        audit.Log(u, "create", "requisition", requisition.Id, $"Requisition draft created ({requisition.Lines.Count} line(s))");
        requisitions.Save();
        return (requisition, null, null);
    }

    public object? Detail(string id)
    {
        var requisition = requisitions.Find(id);
        if (requisition is null) return null;
        var step = requisitions.FindStep(id);
        var reservation = requisitions.FindReservation(id);
        return new
        {
            requisition = ProjRequisition(requisition),
            lines = requisition.Lines.Select(l => new
            {
                l.Id, l.CategoryId, l.Description, l.Qty, l.UnitPrice, l.Unit, l.NeedBy, l.BaseQty, l.BaseUnit,
            }).ToList(),
            step = step is null ? null : new
            {
                step.Id, step.StepNo, step.ApproverRole, step.Decision, step.Version,
                step.DecidedBy, step.OnBehalfOfDelegatorId, step.DecidedAt, step.Reason,
                ruleVersion = requisition.RuleVersion,
            },
            decisions = requisitions.DecisionsFor(id).Select(d => new
            {
                d.Decision, d.Reason, d.ActorId, d.OnBehalfOfDelegatorId, d.StepVersion, d.At,
            }).ToList(),
            budget = reservation is null ? null : new
            {
                reservation.TxnId, reservation.Amount, reservation.Currency, reservation.Status,
            },
        };
    }

    public (Requisition? requisition, string? code, string? message) Submit(CurrentUser u, string id, string? overrideReason)
    {
        var requisition = requisitions.Find(id);
        if (requisition is null) return (null, "REQUISITION_NOT_FOUND", "Requisition not found.");
        if (requisition.Status is not RequisitionStatus.Draft)
            return (null, "REQUISITION_STATE_ERROR", $"Only draft requisitions can be submitted (current: {requisition.Status}).");

        // 012 first (cheapest, most specific), then 003 taxonomy, then 004 unit normalization
        if (ValidateLines(requisition) is { } vErr) return (null, vErr.Code, vErr.Message);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var (l, i) in requisition.Lines.Select((l, i) => (l, i + 1)))
        {
            var category = catalog.FindCategory(l.CategoryId);
            if (category is null)
                return (null, "VALIDATION_ERROR", $"line {i}: category '{l.CategoryId}' does not exist.");
            if (category.Status is CategoryStatus.Deprecated)
                return (null, "VALIDATION_ERROR", $"line {i}: category '{l.CategoryId}' is deprecated and cannot be selected on a new requisition.");
            var (baseQty, baseUnit, uCode, uMsg) = catalog.Resolve(l.Unit, l.Qty, today);
            if (uCode is not null) return (null, uCode, $"line {i}: {uMsg}");
            requisitions.SetField(l, nameof(RequisitionLine.BaseQty), baseQty);
            requisitions.SetField(l, nameof(RequisitionLine.BaseUnit), baseUnit);
        }

        var (rule, ruleCode, ruleMsg) = routes.ResolveActiveRule();
        if (rule is null) return (null, ruleCode, ruleMsg);

        var amount = requisition.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var outcome = budget.Evaluate(amount);
        if (outcome.BlockCode is { } block)
        {
            // 013 AC2: a hard stop yields only to requisitions:override + a recorded reason
            if (block == "BUDGET_HARD_STOP" && !string.IsNullOrWhiteSpace(overrideReason))
            {
                if (!u.Has("requisitions:override"))
                    return (null, "FORBIDDEN", "requisitions:override permission required to override a budget hard stop.");
                audit.Log(u, "override", "budget_check", requisition.Id, $"Budget hard stop overridden: {overrideReason}");
            }
            else return (null, block, outcome.Message!);
        }

        var at = DateTimeOffset.UtcNow;
        SetReq(requisition, nameof(Requisition.Status), RequisitionStatus.Submitted);
        SetReq(requisition, nameof(Requisition.Version), requisition.Version + 1);
        SetReq(requisition, nameof(Requisition.SubmittedBy), u.Id);
        SetReq(requisition, nameof(Requisition.SubmittedAt), at);
        SetReq(requisition, nameof(Requisition.RuleId), rule.Id);
        SetReq(requisition, nameof(Requisition.RuleVersion), rule.Version);
        SetReq(requisition, nameof(Requisition.ApproverRole), rule.ApproverRole);
        requisitions.AddStep(new ApprovalStep($"{requisition.Id}:s1", ec.TenantId, ec.EntityId, requisition.Id,
            1, rule.ApproverRole, null, 1));
        budget.Reserve(requisition.Id, amount, requisition.Currency ?? "BDT", u.Id);
        audit.Log(u, "submit", "requisition", requisition.Id,
            $"Requisition submitted ({amount:0.##} {requisition.Currency ?? "BDT"}, rule {rule.Id} v{rule.Version}, approver role {rule.ApproverRole})");
        requisitions.Save();
        return (requisition, null, null);
    }

    public (Requisition? requisition, string? code, string? message) Decide(
        CurrentUser actor, string id, string? decision, int version, string? reason)
    {
        var requisition = requisitions.Find(id);
        if (requisition is null) return (null, "REQUISITION_NOT_FOUND", "Requisition not found.");

        // authority (perm or delegation) before anything else — QA Gate 2: wrong role sees 403
        var amount = requisition.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        string? onBehalfOf = null;
        if (!actor.Has("requisitions:approve"))
        {
            var (delegation, dCode, dMsg) = routes.ResolveDelegation(actor, amount, RequisitionCategory(requisition));
            if (dCode is not null) return (null, dCode, dMsg!);
            if (delegation is null)
                return (null, "FORBIDDEN", "requisitions:approve permission (or an active delegation) is required.");
            onBehalfOf = delegation.DelegatorId;
        }
        // 016 AC1 — maker-checker: the creator can never decide their own requisition
        if (requisition.CreatedBy == actor.Id)
            return (null, "REQUISITION_APPROVAL_ERROR", "Maker-checker: the requester cannot decide their own requisition.");

        if (!Enum.TryParse<RequisitionDecisionKind>(decision, ignoreCase: false, out var kind) || decision is null)
            return (null, "VALIDATION_ERROR", "Decision must be one of Approve, Reject, Return, RequestInfo.");
        if (!Enum.IsDefined(kind)) return (null, "VALIDATION_ERROR", "Decision must be one of Approve, Reject, Return, RequestInfo.");

        var step = requisitions.FindStep(id);
        if (step is null || step.Decision is RequisitionDecisionKind.Approve or RequisitionDecisionKind.Reject or RequisitionDecisionKind.Return)
            return (null, "REQUISITION_STATE_ERROR", "This requisition has no pending approval step (one terminal decision per step, 018 AC3).");
        // 018 AC2 — the decision must carry the version token currently shown
        if (version != step.Version)
            return (null, "REQUISITION_VERSION_CONFLICT", $"Decision token stale: step is at version {step.Version}, request carried {version}.");

        if (kind is RequisitionDecisionKind.Reject or RequisitionDecisionKind.Return)
        {
            var minReason = reason?.Trim();
            if (minReason is null || minReason.Length < 10)
                return (null, "DECISION_REASON_REQUIRED", "Reject and Return decisions require a reason of at least 10 characters (017 AC2).");
        }

        var at = DateTimeOffset.UtcNow;
        SetStep(step, nameof(ApprovalStep.Decision), kind);
        SetStep(step, nameof(ApprovalStep.DecidedBy), actor.Id);
        SetStep(step, nameof(ApprovalStep.OnBehalfOfDelegatorId), onBehalfOf);
        SetStep(step, nameof(ApprovalStep.DecidedAt), at);
        SetStep(step, nameof(ApprovalStep.Reason), reason);
        SetStep(step, nameof(ApprovalStep.Version), step.Version + 1);
        requisitions.AddDecision(new RequisitionDecision($"{id}:d{step.Version}", ec.TenantId, ec.EntityId, id,
            step.Id, kind, reason, actor.Id, onBehalfOf, step.Version, at));

        if (kind is not RequisitionDecisionKind.RequestInfo) // info requests keep the step pending (017 AC1)
        {
            var status = kind switch
            {
                RequisitionDecisionKind.Approve => RequisitionStatus.Approved,
                RequisitionDecisionKind.Reject => RequisitionStatus.Rejected,
                _ => RequisitionStatus.Returned,
            };
            SetReq(requisition, nameof(Requisition.Status), status);
            SetReq(requisition, nameof(Requisition.Version), requisition.Version + 1);
        }
        audit.Log(actor, kind.ToString().ToLowerInvariant(), "requisition", id,
            $"Requisition {kind} by {actor.Email}" + (onBehalfOf is null ? "" : $" on behalf of delegator {onBehalfOf}"));
        if (kind is RequisitionDecisionKind.Approve)
            audit.Emit(actor, "procurement.requisition.approved", new { requisitionId = id, approver = actor.Id, onBehalfOf });
        requisitions.Save();
        return (requisition, null, null);
    }

    public (Requisition? requisition, string? code, string? message) FlagException(
        CurrentUser u, string id, string? reasonCode, string? narrative, string? evidenceRef)
    {
        var requisition = requisitions.Find(id);
        if (requisition is null) return (null, "REQUISITION_NOT_FOUND", "Requisition not found.");
        if (requisition.Status is not (RequisitionStatus.Draft or RequisitionStatus.Submitted))
            return (null, "REQUISITION_STATE_ERROR", $"Exceptions flag pre-decision requisitions (current: {requisition.Status}).");
        if (string.IsNullOrWhiteSpace(reasonCode) || string.IsNullOrWhiteSpace(narrative))
            return (null, "EXCEPTION_REASON_REQUIRED", "An exception needs both a reason code and a narrative (019 AC1).");
        var at = DateTimeOffset.UtcNow;
        SetReq(requisition, nameof(Requisition.ExceptionCode), reasonCode.Trim());
        SetReq(requisition, nameof(Requisition.ExceptionNarrative), narrative.Trim());
        SetReq(requisition, nameof(Requisition.ExceptionEvidenceRef), evidenceRef);
        SetReq(requisition, nameof(Requisition.ExceptionFlaggedAt), at);
        audit.Log(u, "flag-exception", "requisition", id,
            $"Exception {reasonCode} flagged: compensating approval path required (019 AC3)");
        requisitions.Save();
        return (requisition, null, null);
    }

    public (ApprovalDelegation? delegation, string? code, string? message) CreateDelegation(
        CurrentUser u, string? delegateId, DateTimeOffset from, DateTimeOffset to, decimal? maxAmount, string? categoryId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(delegateId) || string.IsNullOrWhiteSpace(reason))
            return (null, "VALIDATION_ERROR", "DelegateId and reason are required.");
        if (delegateId == u.Id)
            return (null, "DELEGATION_INVALID", "A delegation cannot target its own delegator (015 AC1).");
        if (to <= from)
            return (null, "DELEGATION_INVALID", "The delegation window must end after it starts (015 AC1).");
        if (identities.FindActiveUserById(delegateId) is null)
            return (null, "DELEGATION_INVALID", "The delegate must be an active user in this entity.");
        var delegation = new ApprovalDelegation($"DLG-{DateTimeOffset.UtcNow.Ticks}", ec.TenantId, ec.EntityId,
            u.Id, delegateId, from, to, maxAmount, categoryId, reason.Trim(), u.Id, DateTimeOffset.UtcNow);
        requisitions.AddDelegation(delegation);
        audit.Log(u, "create", "approval_delegation", delegation.Id,
            $"Approval authority delegated to {delegateId} from {from:u} to {to:u}" +
            (maxAmount is { } cap ? $" up to {cap:0.##}" : "") + (categoryId is { } c ? $" scoped to {c}" : ""));
        requisitions.Save();
        return (delegation, null, null);
    }

    // ==================== helpers ====================

    // 012 AC2: field-specific line errors keep the requisition in draft
    private static (string? Code, string? Message)? ValidateLines(Requisition requisition)
    {
        if (requisition.Lines.Count == 0)
            return ("VALIDATION_ERROR", "At least one line is required to submit (line 1: missing).");
        foreach (var (l, i) in requisition.Lines.Select((l, i) => (l, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(l.CategoryId)) return ("VALIDATION_ERROR", $"line {i}: categoryId is required.");
            if (string.IsNullOrWhiteSpace(l.Description)) return ("VALIDATION_ERROR", $"line {i}: description is required.");
            if (l.Qty <= 0) return ("VALIDATION_ERROR", $"line {i}: qty must be greater than 0.");
            if (l.UnitPrice < 0) return ("VALIDATION_ERROR", $"line {i}: unitPrice must be >= 0.");
            if (string.IsNullOrWhiteSpace(l.Unit)) return ("VALIDATION_ERROR", $"line {i}: unit is required.");
        }
        return null;
    }

    private static string? RequisitionCategory(Requisition requisition)
    {
        var categories = requisition.Lines.Select(l => l.CategoryId).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        return categories.Count == 1 ? categories[0] : null; // mixed-category reqs only match unscoped delegations
    }

    private static object ProjRequisition(Requisition r) => new
    {
        r.Id, r.Purpose, r.Status, r.Version, r.Currency, r.CreatedBy, r.CreatedAt,
        r.SubmittedBy, r.SubmittedAt, r.RuleId, r.RuleVersion, r.ApproverRole,
        r.ExceptionCode, r.ExceptionNarrative, r.ExceptionEvidenceRef, r.ExceptionFlaggedAt,
        total = r.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
    };

    private void SetReq<T>(Requisition r, string name, T value) => requisitions.SetField(r, name, value!);
    private void SetStep<T>(ApprovalStep s, string name, T value) => requisitions.SetField(s, name, value!);
}
