using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Service cases: intake with channel idempotency + SLA due computation (023/026), forward
// lifecycle with reopen (024), breach flag derived on read (037). SLA is the R8d flat
// priority→hours mapping (plain clock); holiday calendars are deferred.
public sealed class ServiceCaseService(
    IAssetsRepository cases, IIdentityRepository identities, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec) : IServiceCaseService
{
    // ERP-ASSETS-024 AC1: forward flow plus reopen from Resolved; Closed is terminal
    private static readonly IReadOnlyDictionary<CaseStatus, CaseStatus[]> Legal = new Dictionary<CaseStatus, CaseStatus[]>
    {
        [CaseStatus.Open] = [CaseStatus.InProgress],
        [CaseStatus.InProgress] = [CaseStatus.Waiting, CaseStatus.Resolved],
        [CaseStatus.Waiting] = [CaseStatus.InProgress, CaseStatus.Resolved],
        [CaseStatus.Resolved] = [CaseStatus.Closed, CaseStatus.Open],
        [CaseStatus.Closed] = [],
    };

    public static TimeSpan SlaWindow(CasePriority priority) => priority switch
    {
        CasePriority.High => TimeSpan.FromHours(24),
        CasePriority.Medium => TimeSpan.FromHours(48),
        _ => TimeSpan.FromHours(72),
    };

    public object List()
    {
        var now = DateTimeOffset.UtcNow;
        return cases.AllCases().Select(c => Proj(c, BreachState(c, now))).ToList();
    }

    public (object? data, string? code, string? message) Create(CurrentUser user, CreateCaseRequest request)
    {
        // ERP-ASSETS-023 AC1: a valid requester/account link is required before save
        if (identities.FindActiveUserById(request.RequesterId) is null)
            return (null, "VALIDATION_ERROR", "Requester must be an active user.");

        var now = DateTimeOffset.UtcNow;
        var serviceCase = new ServiceCase(numbers.Next("CASE"), ec.TenantId, ec.EntityId, request.Title, request.Channel,
            request.RequesterId, request.Priority, CaseStatus.Open, AssignedTo: null,
            now + SlaWindow(request.Priority), ResolutionCode: null, BreachState: null, user.Id, now);
        cases.AddCase(serviceCase);
        audit.Log(user, "create", "service_case", serviceCase.Id,
            $"Case '{request.Title}' opened via {request.Channel} with {request.Priority} priority; due {serviceCase.DueAt:u}");
        cases.Save();
        return (Proj(serviceCase, null), null, null);
    }

    public (object? data, string? code, string? message) Assign(CurrentUser user, string caseId, string assigneeId)
    {
        var serviceCase = cases.FindCase(caseId);
        if (serviceCase is null) return (null, "CASE_NOT_FOUND", "Service case not found.");
        if (serviceCase.Status == CaseStatus.Closed)
            return (null, "CASE_STATE_ERROR", "Closed cases cannot be reassigned.");
        if (identities.FindActiveUserById(assigneeId) is null)
            return (null, "VALIDATION_ERROR", "Assignee must be an active user.");

        cases.SetCaseAssigned(serviceCase, assigneeId);
        audit.Log(user, "assign", "service_case", caseId, $"Case {caseId} assigned to {assigneeId} by {user.Email}");
        cases.Save();
        return (Proj(serviceCase, BreachState(serviceCase, DateTimeOffset.UtcNow)), null, null);
    }

    public (object? data, string? code, string? message) Transition(CurrentUser user, string caseId, CaseStatus to, string? reason, string? resolutionCode)
    {
        var serviceCase = cases.FindCase(caseId);
        if (serviceCase is null) return (null, "CASE_NOT_FOUND", "Service case not found.");
        var from = serviceCase.Status;
        if (!Legal[from].Contains(to))
            return (null, "CASE_TRANSITION_ERROR", $"Illegal transition {from} → {to}.");

        // reopen = Resolved → Open reactivates the SLA clock by policy (fresh due instant)
        var reopen = from == CaseStatus.Resolved && to == CaseStatus.Open;

        // a transition into Resolved is a resolution: stamp the code and emit the same outbox
        // event as /resolution (024 AC2 — the controller rejects a missing code before this)
        if (to == CaseStatus.Resolved)
        {
            cases.SetCaseStatus(serviceCase, to, resolutionCode, null);
            audit.Log(user, "resolve", "service_case", caseId,
                $"Case {caseId} resolved by {user.Email} with code {resolutionCode}");
            audit.Emit(user, "assets.case.resolved", new { caseId, resolutionCode, resolvedBy = user.Id });
        }
        else
        {
            cases.SetCaseStatus(serviceCase, to, reopen ? null : serviceCase.ResolutionCode,
                reopen ? DateTimeOffset.UtcNow + SlaWindow(serviceCase.Priority) : null);
            audit.Log(user, "transition", "service_case", caseId,
                $"Case {caseId}: {from} → {to} by {user.Email}{(reason is null ? "" : $" ({reason})")}");
        }
        cases.Save();
        return (Proj(serviceCase, BreachState(serviceCase, DateTimeOffset.UtcNow)), null, null);
    }

    public (object? data, string? code, string? message) Resolve(CurrentUser user, string caseId, string resolutionCode)
    {
        var serviceCase = cases.FindCase(caseId);
        if (serviceCase is null) return (null, "CASE_NOT_FOUND", "Service case not found.");
        var from = serviceCase.Status;
        if (!Legal[from].Contains(CaseStatus.Resolved))
            return (null, "CASE_TRANSITION_ERROR", $"Illegal transition {from} → Resolved.");

        cases.SetCaseStatus(serviceCase, CaseStatus.Resolved, resolutionCode, null);
        audit.Log(user, "resolve", "service_case", caseId,
            $"Case {caseId} resolved by {user.Email} with code {resolutionCode}");
        audit.Emit(user, "assets.case.resolved", new { caseId, resolutionCode, resolvedBy = user.Id });
        cases.Save();
        return (Proj(serviceCase, null), null, null);
    }

    // ERP-ASSETS-037: breach flag derived per read — DueAt passed while the case is still open
    private static string? BreachState(ServiceCase c, DateTimeOffset now) =>
        c.Status is not (CaseStatus.Resolved or CaseStatus.Closed) && c.DueAt < now ? "Breached" : null;

    private static object Proj(ServiceCase c, string? breachState) => new
    {
        c.Id, c.Title, c.Channel, c.RequesterId, c.Priority, c.Status, c.AssignedTo, c.DueAt,
        c.ResolutionCode, breachState, c.CreatedBy, c.CreatedAt,
    };
}
