using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Forecast (ERP-SALES-018/020): category submissions, bucketed report, immutable snapshots.
[ApiController]
[Route("api/v1")]
public sealed class ForecastController(IForecastService forecast) : ControllerBase
{
    [HttpPost("forecast-submissions")]
    public IResult Submit(SubmitForecastRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Owner, "Owner") ?? Guards.Check(request.Period, "Period", 7)
            ?? (Enum.TryParse<ForecastCategory>(request.Category, true, out _)
                ? null : "Category must be Pipeline, BestCase or Commit.");
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, resubmitted, code, message) = forecast.Submit(u, request.Owner, request.Period,
            Enum.Parse<ForecastCategory>(request.Category, true), request.OverrideValue);
        // resubmission is an update of the (owner, period, category) row — 200, not 201
        return code is null ? (Envelope.OkBody(data!), resubmitted ? 200 : 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("forecast")]
    public IResult Report([FromQuery] string period)
    {
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = forecast.Report(u, period);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("forecast-snapshots")]
    public IResult Snapshot(CreateSnapshotRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Period, "Period", 7);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        var (data, code, message) = forecast.CreateSnapshot(u, request.Period);
        if (code == "FORECAST_SNAPSHOT_INCOMPLETE")
            return (Envelope.OkBody(data!), 200); // recorded as Incomplete, surfaced in the body
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    [HttpGet("forecast-snapshots")]
    public IResult Snapshots([FromQuery] string period)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("forecasts:read")) return Envelope.Fail("FORBIDDEN", "forecasts:read permission required.", 403);
        var (data, code, message) = forecast.Snapshots(period);
        return code is null ? Envelope.Ok<object>(data!) : Envelope.Fail(code, message!, ErrorStatus.StatusFor(code));
    }
}
