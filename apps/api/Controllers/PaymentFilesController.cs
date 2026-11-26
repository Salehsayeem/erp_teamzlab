using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM7 — payment file handoff (050, maker-checker). Creation rides the MAKER's payroll:calculate;
// release + rejections are the CHECKER's payroll:release acts — calculation approval ≠ funds
// release, and the maker can never release their own file.
[ApiController]
[Route("api/v1/pay-runs/{runId}/payment-files")]
public sealed class RunPaymentFilesController(IPaymentFileService files) : ControllerBase
{
    [HttpPost]
    public IResult Create(string runId, Dtos.CreatePaymentFileRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:calculate")) return (Envelope.FailBody("FORBIDDEN", "payroll:calculate permission required."), 403);
        var (data, code, message, replayed) = files.CreateForRun(u, runId, request);
        return code is null
            ? (Envelope.OkBody(data!), replayed ? 200 : 201) // replay of the run's existing file is a 200
            : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/payment-files")]
public sealed class PaymentFilesController(IPaymentFileService files) : ControllerBase
{
    [HttpGet]
    public IResult List([FromQuery] string? status)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (okStatus, statusValue) = Query.Enum<PaymentFileStatus>(status);
        return okStatus
            ? Envelope.Ok<object>(files.ListFiles(u, statusValue))
            : Envelope.Fail("VALIDATION_ERROR", $"Unknown payment file status '{status}'.", 400);
    }

    [HttpGet("{id}")]
    public IResult Detail(string id)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:read")) return Envelope.Fail("FORBIDDEN", "payroll:read permission required.", 403);
        var (data, code, message) = files.FileDetail(u, id);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/releases")]
    public IResult Release(string id, Dtos.ReleasePaymentFileRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:release")) return (Envelope.FailBody("FORBIDDEN", "payroll:release permission required."), 403);
        var (data, code, message) = files.Release(u, id);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}/rejections")]
    public IResult Reject(string id, Dtos.RejectPaymentFileItemsRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("payroll:release")) return (Envelope.FailBody("FORBIDDEN", "payroll:release permission required."), 403);
        var (data, code, message) = files.RejectItems(u, id, request);
        return code is null ? (Envelope.OkBody(data!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
