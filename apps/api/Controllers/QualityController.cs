using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// MF8/MF9 — quality plans, inspections, holds, NCR, CAPA, dashboards (ERP-MFG-055..063).
// quality:plan authors plans; quality:inspect inspects; quality:dispose disposes (SoD triple,
// 055) — R-QC is consumed here, seeded by the PROCUREMENT slice.
[ApiController]
[Route("api/v1/quality-plans")]
public sealed class QualityPlansController(IQualityService quality) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? itemSku, [FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        QualityPlanStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<QualityPlanStatus>(status, ignoreCase: true, out var parsed))
                return Envelope.Fail("VALIDATION_ERROR", $"status '{status}' is not a valid quality plan status.", 400);
            filter = parsed;
        }
        return Envelope.Ok<object>(quality.ListPlans(itemSku, filter));
    }

    [HttpPost]
    public IResult Create(CreateQualityPlanRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:plan")) return (Envelope.FailBody("FORBIDDEN", "quality:plan permission required."), 403);
        var (data, code, message) = quality.CreatePlan(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/approvals")]
    public IResult Approve(string id, [FromBody] RoutingDecisionRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:plan")) return (Envelope.FailBody("FORBIDDEN", "quality:plan permission required."), 403);
        var (data, code, message) = quality.ApprovePlan(u, id, request?.Reason);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });

    [HttpPost("{id}/characteristics")]
    public IResult AddCharacteristic(string id, AddCharacteristicRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Code, "Code") ?? Guards.Check(request.Name, "Name");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:plan")) return (Envelope.FailBody("FORBIDDEN", "quality:plan permission required."), 403);
        var (data, code, message) = quality.AddCharacteristic(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/sampling-plans")]
    public IResult AddSampling(string id, CreateSamplingPlanRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:plan")) return (Envelope.FailBody("FORBIDDEN", "quality:plan permission required."), 403);
        var (data, code, message) = quality.AddSamplingPlan(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });
}

[ApiController]
[Route("api/v1/inspections")]
public sealed class InspectionsController(IQualityService quality) : ControllerBase
{
    [HttpPost]
    public IResult Create(CreateInspectionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.QualityPlanId, "QualityPlanId") ?? Guards.Check(request.ItemSku, "ItemSku");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.CreateInspection(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpGet("{id}/results")]
    public IResult Results(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(quality.Results(id));
    }

    [HttpPost("{id}/results")]
    public IResult AddResult(string id, AddInspectionResultRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CharacteristicCode, "CharacteristicCode") ?? Guards.Check(request.RawValue, "RawValue");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.AddResult(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/results/{resultId}/amendments")]
    public IResult Amend(string id, string resultId, AmendResultRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.AmendResult(u, id, resultId, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/finalizations")]
    public IResult Finalize(string id, [FromBody] FinalizeInspectionRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.Finalize(u, id, request ?? new FinalizeInspectionRequest(null));
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/quality-holds")]
public sealed class QualityHoldsController(IQualityService quality) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(quality.Holds());
    }

    [HttpPost]
    public IResult Hold(CreateQualityHoldRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect") && !u.Has("quality:dispose"))
            return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.Hold(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/dispositions")]
    public IResult Dispose(string id, HoldDispositionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:dispose")) return (Envelope.FailBody("FORBIDDEN", "quality:dispose permission required."), 403);
        var (data, code, message) = quality.DisposeHold(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/nonconformances")]
public sealed class NonconformancesController(IQualityService quality) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(quality.ListNcs());
    }

    [HttpPost]
    public IResult Raise(CreateNcRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.RaiseNc(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/containments")]
    public IResult Contain(string id, CreateContainmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.AddContainment(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/closures")]
    public IResult Close(string id) => Idem.Wrap(HttpContext, (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:dispose")) return (Envelope.FailBody("FORBIDDEN", "quality:dispose permission required."), 403);
        var (data, code, message) = quality.CloseNc(u, id);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/containments/{id}/releases")]
public sealed class ContainmentReleasesController(IQualityService quality) : ControllerBase
{
    [HttpPost]
    public IResult Release(string id, ReleaseContainmentRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:dispose")) return (Envelope.FailBody("FORBIDDEN", "quality:dispose permission required."), 403);
        var (data, code, message) = quality.ReleaseContainment(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/capa-cases")]
public sealed class CapaCasesController(IQualityService quality) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(quality.ListCapas());
    }

    [HttpPost]
    public IResult Create(CreateCapaRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.CreateCapa(u, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/actions")]
    public IResult AddAction(string id, AddCapaActionRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.AddCapaAction(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/verifications")]
    public IResult Verify(string id, CapaVerificationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.VerifyCapa(u, id, request);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 201);
    });

    [HttpPost("{id}/closures")]
    public IResult Close(string id) => Idem.Wrap(HttpContext, (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:dispose")) return (Envelope.FailBody("FORBIDDEN", "quality:dispose permission required."), 403);
        var (data, code, message) = quality.CloseCapa(u, id);
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/capa-actions/{id}/completions")]
public sealed class CapaActionsController(IQualityService quality) : ControllerBase
{
    [HttpPost]
    public IResult Complete(string id, [FromBody] CompleteCapaActionRequest? request) => Idem.Wrap(HttpContext, request ?? (object)new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("quality:inspect")) return (Envelope.FailBody("FORBIDDEN", "quality:inspect permission required."), 403);
        var (data, code, message) = quality.CompleteCapaAction(u, id, request ?? new CompleteCapaActionRequest(null));
        return code is not null
            ? (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code))
            : (Envelope.OkBody(data!), 200);
    });
}

[ApiController]
[Route("api/v1/quality-dashboards")]
public sealed class QualityDashboardsController(IQualityService quality) : ControllerBase
{
    [HttpGet]
    public IResult Dashboard()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("mfg:read")) return Envelope.Fail("FORBIDDEN", "mfg:read permission required.", 403);
        return Envelope.Ok<object>(quality.Dashboard());
    }
}
