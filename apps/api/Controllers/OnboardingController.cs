using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// PM2 — onboarding task orchestration (011). Waivers NEED a reason (400 without) and a named
// approver who is not the task's generator (maker-checker per brief §3).
[ApiController]
[Route("api/v1/onboarding-templates")]
public sealed class OnboardingTemplatesController(IOnboardingService onboarding) : ControllerBase
{
    [HttpGet]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(onboarding.ListTemplates(u));
    }

    [HttpPost]
    public IResult Create(Dtos.CreateOnboardingTemplateRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CategoryCode, "CategoryCode", 50) ?? Guards.Check(request.TaskKey, "TaskKey", 50)
            ?? Guards.Check(request.Name, "Name") ?? Guards.Check(request.OwnerRole, "OwnerRole", 50);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("onboarding:write")) return (Envelope.FailBody("FORBIDDEN", "onboarding:write permission required."), 403);
        var (template, code, message) = onboarding.CreateTemplate(u, request);
        return code is null ? (Envelope.OkBody(template!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/workers/{workerId}/onboarding-tasks")]
public sealed class WorkerOnboardingController(IOnboardingService onboarding) : ControllerBase
{
    [HttpGet]
    public IResult List(string workerId)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("workers:read")) return Envelope.Fail("FORBIDDEN", "workers:read permission required.", 403);
        return Envelope.Ok<object>(onboarding.TasksFor(u, workerId));
    }

    // task-set generation from category config — idempotent on replay
    [HttpPost("generations")]
    public IResult Generate(string workerId, Dtos.GenerateOnboardingTasksRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("onboarding:write")) return (Envelope.FailBody("FORBIDDEN", "onboarding:write permission required."), 403);
        var (data, code, message) = onboarding.GenerateForWorker(u, workerId);
        return code is null ? (Envelope.OkBody(data!), 201) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}

[ApiController]
[Route("api/v1/onboarding-tasks")]
public sealed class OnboardingTasksController(IOnboardingService onboarding) : ControllerBase
{
    [HttpPost("{id}/completions")]
    public IResult Complete(string id) => Idem.Wrap(HttpContext, new { }, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("onboarding:write")) return (Envelope.FailBody("FORBIDDEN", "onboarding:write permission required."), 403);
        var (task, code, message) = onboarding.Complete(u, id);
        return code is null ? (Envelope.OkBody(task!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });

    // every waiver names its approver (the caller) + reason (011 AC2)
    [HttpPost("{id}/waivers")]
    public IResult Waive(string id, Dtos.DecideOnboardingTaskRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (Envelope.FailBody("VALIDATION_ERROR", "A waiver requires a Reason (011 AC2)."), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("onboarding:write")) return (Envelope.FailBody("FORBIDDEN", "onboarding:write permission required."), 403);
        var (task, code, message) = onboarding.Waive(u, id, request.Reason);
        return code is null ? (Envelope.OkBody(task!), 200) : (Envelope.FailBody(code, message!), ErrorStatus.StatusFor(code));
    });
}
