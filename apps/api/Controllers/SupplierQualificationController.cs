using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P5 supplier qualification (040–043, 061) — questionnaires, responses + reviews, screenings with
// the human-disposition queue, observations + disputes, risk profiles, qualification status.
// Evidence/observation intake sits on the suppliers routes (SuppliersController additive).
[ApiController]
[Route("api/v1")]
public sealed class SupplierQualificationController(ISupplierQualificationService qualification) : ControllerBase
{
    [HttpPost("supplier-questionnaires")]
    public IResult CreateQuestionnaire(CreateQuestionnaireRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (questionnaire, code, error) = qualification.CreateQuestionnaire(u, request.Name, request.Questions);
        return code is null ? (Envelope.OkBody(questionnaire!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("suppliers/{id}/questionnaire-responses")]
    public IResult CreateResponse(string id, CreateQuestionnaireResponseRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (response, code, error) = qualification.CreateResponse(u, id, request.QuestionnaireId, request.AnswersJson);
        return code is null ? (Envelope.OkBody(response!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("questionnaire-responses/{id}/reviews")]
    public IResult ReviewResponse(string id, ReviewQuestionnaireResponseRequest request)
    {
        var (response, code, error) = qualification.ReviewResponse(CurrentUser.From(HttpContext), id, request.ReviewDecision, request.FieldCommentsJson);
        return code is null ? Envelope.Ok<object>(response!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpGet("suppliers/{id}/qualification")]
    public IResult Qualification(string id) => Envelope.Ok<object>(qualification.Qualification(id));

    [HttpGet("suppliers/{id}/risk-profile")]
    public IResult RiskProfile(string id) => Envelope.Ok<object>(qualification.RiskProfile(id));

    [HttpGet("suppliers/{id}/screenings")]
    public IResult Screenings(string id) => Envelope.Ok<object>(qualification.ScreeningsFor(id));

    [HttpPost("suppliers/{id}/screenings")]
    public IResult CreateScreening(string id, AddScreeningRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (screening, code, error) = qualification.CreateScreening(u, id, request.Source, request.SourceRef);
        return code is null ? (Envelope.OkBody(screening!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("screenings/{id}/dispositions")]
    public IResult DisposeScreening(string id, DisposeScreeningRequest request)
    {
        var (screening, code, error) = qualification.DisposeScreening(CurrentUser.From(HttpContext), id, request.Disposition, request.Reason);
        return code is null ? Envelope.Ok<object>(screening!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("suppliers/{id}/observations")]
    public IResult CreateObservation(string id, AddObservationRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
        var (observation, code, error) = qualification.CreateObservation(u, id, request.Metric, request.Value, request.Period, request.Source);
        return code is null ? (Envelope.OkBody(observation!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("observations/{id}/disputes")]
    public IResult DisputeObservation(string id, DisputeObservationRequest request)
    {
        var (observation, code, error) = qualification.DisputeObservation(CurrentUser.From(HttpContext), id, request.Reason, request.AmendedValue);
        return code is null ? Envelope.Ok<object>(observation!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}
