using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// T6 contracts (ERP-PROCUREMENT-036..039): commitment-only — nothing here touches the ledger.
// Creates are Idem.Wrap'd; checker acts (activation, verification, waiver, renewal) do their
// maker-checker + permission checks in the service.
[ApiController]
[Route("api/v1/contracts")]
public sealed class ContractsController(IContractsService contracts) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status, [FromQuery] string? expiringWithinDays)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("contracts:read")) return Envelope.Fail("FORBIDDEN", "contracts:read permission required.", 403);
        var (ok, statusValue) = Query.Enum<ContractStatus>(status);
        if (!ok) return Envelope.Fail("VALIDATION_ERROR", $"Unknown contract status '{status}'.", 400);
        int? days = null;
        if (!string.IsNullOrWhiteSpace(expiringWithinDays))
        {
            if (!int.TryParse(expiringWithinDays, out var parsed) || parsed < 0)
                return Envelope.Fail("VALIDATION_ERROR", "expiringWithinDays must be a non-negative number of days.", 400);
            days = parsed;
        }
        return Envelope.Ok<object>(contracts.List(statusValue, days));
    }

    [HttpPost]
    public IResult Create(CreateContractRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.SupplierId, "SupplierId") ?? Guards.Check(request.Currency, "Currency")
            ?? (request.EffectiveFrom > request.EffectiveTo ? "EffectiveFrom must be on or before EffectiveTo." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("contracts:write")) return (Envelope.FailBody("FORBIDDEN", "contracts:write permission required."), 403);
        var (data, code, message) = contracts.Create(u, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/activations")]
    public IResult Activate(string id)
    {
        var (data, code, message) = contracts.Activate(CurrentUser.From(HttpContext), id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/amendments")]
    public IResult AddAmendment(string id, AddAmendmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Summary, "Summary");
        if (err is null && (request.PriceLines is null || request.PriceLines.Count == 0))
            err = "At least one price line is required.";
        if (err is null && request.PriceLines!.Any(l => string.IsNullOrWhiteSpace(l.ItemSku)))
            err = "Every price line needs an item SKU.";
        if (err is null && request.PriceLines!.GroupBy(l => new { l.ItemSku, l.EffectiveFrom, l.EffectiveTo }).Any(g => g.Count() > 1))
            err = "Identical duplicate price lines are not allowed in one amendment.";
        if (err is null && request.PriceLines!.Any(l => l.UnitPrice < 0))
            err = "Price line unit price must be 0 or greater.";
        if (err is null && request.PriceLines!.Any(l => l.EffectiveTo is { } to && to < l.EffectiveFrom))
            err = "Price line EffectiveTo must be on or after EffectiveFrom.";
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("contracts:write")) return (Envelope.FailBody("FORBIDDEN", "contracts:write permission required."), 403);
        var (data, code, message) = contracts.AddAmendment(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/obligations")]
    public IResult AddObligation(string id, AddObligationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ObligationType, "ObligationType") ?? Guards.Check(request.OwnerUserId, "OwnerUserId")
            ?? (request.DueDate == default ? "DueDate is required." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("contracts:write")) return (Envelope.FailBody("FORBIDDEN", "contracts:write permission required."), 403);
        var (data, code, message) = contracts.AddObligation(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/obligations/{obligationId}/evidence")]
    public IResult SubmitEvidence(string id, string obligationId, SubmitEvidenceRequest request)
    {
        var err = Guards.Check(request.EvidenceRef, "EvidenceRef");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("contracts:write")) return Envelope.Fail("FORBIDDEN", "contracts:write permission required.", 403);
        var (data, code, message) = contracts.SubmitEvidence(u, id, obligationId, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/obligations/{obligationId}/verifications")]
    public IResult Verify(string id, string obligationId)
    {
        var (data, code, message) = contracts.VerifyObligation(CurrentUser.From(HttpContext), id, obligationId);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/obligations/{obligationId}/waivers")]
    public IResult Waive(string id, string obligationId, WaiveObligationRequest request)
    {
        var err = Guards.Check(request.Reason, "Reason");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var (data, code, message) = contracts.WaiveObligation(CurrentUser.From(HttpContext), id, obligationId, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/renewals")]
    public IResult Renew(string id, ExerciseRenewalRequest request)
    {
        if (request.OptionNumber < 1) return Envelope.Fail("VALIDATION_ERROR", "OptionNumber must be 1 or greater.", 400);
        var (data, code, message) = contracts.ExerciseRenewal(CurrentUser.From(HttpContext), id, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
