using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// CRM interaction timeline + cases (ERP-SALES-007/008). R-T7d: deliberately separate from the
// ASSETS ServiceCase entity — only CasePriority and the R8d flat SLA hours mapping are reused.
public sealed class CrmService(
    ICrmRepository crm, INumberSeries numbers,
    IAuditRepository audit, EntityContext ec) : ICrmService
{
    // 008: forward map; Resolved demands resolution text (checked by the controller before this)
    private static readonly IReadOnlyDictionary<CrmCaseStatus, CrmCaseStatus[]> Legal = new Dictionary<CrmCaseStatus, CrmCaseStatus[]>
    {
        [CrmCaseStatus.Open] = [CrmCaseStatus.InProgress],
        [CrmCaseStatus.InProgress] = [CrmCaseStatus.Resolved],
        [CrmCaseStatus.Resolved] = [CrmCaseStatus.Closed],
        [CrmCaseStatus.Closed] = [],
    };

    public static readonly IReadOnlySet<string> Channels = new HashSet<string>
    { "Call", "Meeting", "Email", "Note", "Task" };

    public object Interactions(InteractionPartyType? partyType, string? partyId) =>
        Filtered(partyType, partyId).Select(i => new
        {
            i.Id, i.PartyType, i.PartyId, i.Channel, i.Subject, i.Notes,
            i.OccurredAt, i.RecordedAt, i.CreatedBy, // both timestamps shown (007 AC1)
        }).ToList();

    private List<Interaction> Filtered(InteractionPartyType? partyType, string? partyId)
    {
        if (partyType is null || partyId is null) return crm.AllInteractions();
        return crm.InteractionsFor(partyType.Value, partyId);
    }

    public (Interaction? interaction, string? code, string? message) LogInteraction(CurrentUser user, Dtos.LogInteractionRequest request)
    {
        var interaction = new Interaction(numbers.Next("INT"), ec.TenantId, ec.EntityId,
            request.PartyType, request.PartyId, request.Channel, request.Subject, request.Notes,
            request.OccurredAt, DateTimeOffset.UtcNow, user.Id); // RecordedAt = server receipt
        crm.AddInteraction(interaction);
        audit.Log(user, "log", "interaction", interaction.Id,
            $"Interaction '{request.Subject}' via {request.Channel} at {request.OccurredAt:u}");
        crm.Save();
        return (interaction, null, null);
    }

    public object Cases(bool overdueOnly)
    {
        var now = DateTimeOffset.UtcNow;
        var cases = crm.AllCases();
        if (!overdueOnly) return cases.Select(c => Proj(c, now)).ToList();
        // 008 AC2: overdue = response target passed while the case still awaits its response
        return cases.Where(c => c.ResponseDueAt < now && c.Status is not (CrmCaseStatus.Resolved or CrmCaseStatus.Closed))
            .Select(c => Proj(c, now)).ToList();
    }

    public (CrmCase? crmCase, string? code, string? message) CreateCase(CurrentUser user, Dtos.CreateCrmCaseRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var crmCase = new CrmCase(numbers.Next("CC"), ec.TenantId, ec.EntityId, request.CustomerId,
            request.Category, request.Priority, CrmCaseStatus.Open, user.Id,
            now + ServiceCaseService.SlaWindow(request.Priority), null, request.SalesOrderId, user.Id, now);
        crm.AddCase(crmCase);
        audit.Log(user, "create", "crm_case", crmCase.Id,
            $"CRM case '{request.Category}' opened with {request.Priority} priority; response due {crmCase.ResponseDueAt:u}");
        crm.Save();
        return (crmCase, null, null);
    }

    public (CrmCase? crmCase, string? code, string? message) TransitionCase(CurrentUser user, string caseId, CrmCaseStatus to, string? resolution)
    {
        var crmCase = crm.FindCase(caseId);
        if (crmCase is null) return (null, "CRM_CASE_NOT_FOUND", "CRM case not found.");
        var from = crmCase.Status;
        if (!Legal[from].Contains(to))
            return (null, "CRM_CASE_TRANSITION_ERROR", $"Illegal transition {from} → {to}.");

        crm.SetCaseStatus(crmCase, to, to == CrmCaseStatus.Resolved ? resolution : crmCase.Resolution);
        audit.Log(user, "transition", "crm_case", caseId, $"CRM case {caseId}: {from} → {to}");
        crm.Save();
        return (crmCase, null, null);
    }

    private static object Proj(CrmCase c, DateTimeOffset now) => new
    {
        c.Id, c.CustomerId, c.Category, c.Priority, c.Status, c.OwnerId, c.ResponseDueAt,
        c.Resolution, c.SalesOrderId, c.CreatedBy, c.CreatedAt,
        overdue = c.Status is not (CrmCaseStatus.Resolved or CrmCaseStatus.Closed) && c.ResponseDueAt < now,
    };
}
