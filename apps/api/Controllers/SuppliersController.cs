using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(ISupplierService suppliers, ISupplierQualificationService qualification) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(suppliers.List());

    // deferred PROCUREMENT wave (041): expiry-gated evidence intake; the gate reads via
    // GET {id}/qualification — expired evidence fails eligibility absent policy grace
    [HttpGet("{id}/evidences")]
    public IResult Evidences(string id) => Envelope.Ok<object>(qualification.Evidences(id));

    [HttpPost("{id}/evidences")]
    public IResult AddEvidence(string id, AddEvidenceRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (evidence, code, error) = qualification.CreateEvidence(u, id, request.EvidenceType, request.MetadataJson, request.ExpiresOn, request.SupersedesId);
        return code is null ? (Envelope.OkBody(evidence!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("{id}/gates")]
    public IResult Gates(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:read")) return Envelope.Fail("FORBIDDEN", "suppliers:read permission required.", 403);
        var (data, code, message) = suppliers.Gates(id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost]
    public IResult Create(CreateSupplierRequest request) => Idem.Wrap(HttpContext, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.TaxId, "TaxId") ?? Guards.CheckEmail(request.Email);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (data, code, message) = suppliers.Create(u, request);
        return code is null ? (Envelope.OkBody(data), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // task-6 (ERP-PROCUREMENT-047): lifecycle transition with a mandatory reason
    [HttpPost("{id}/status")]
    public IResult SetStatus(string id, SetSupplierStatusRequest request)
    {
        var err = Guards.Check(request.Reason, "Reason");
        if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err, 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return Envelope.Fail("FORBIDDEN", "suppliers:write permission required.", 403);
        var (data, code, message) = suppliers.SetStatus(u, id, request);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    // task-6 (ERP-PROCUREMENT-023/044): effective-dated eligibility gate; a re-POST of the same
    // gate with exceptionUntil records the time-bounded exception (R-T6e: suppliers:write)
    [HttpPost("{id}/gates")]
    public IResult AddGate(string id, AddSupplierGateRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.GateCode, "GateCode")
            ?? (request.EffectiveTo is { } to && to < request.EffectiveFrom
                ? "EffectiveTo must be on or after EffectiveFrom." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (data, code, message) = suppliers.AddGate(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
