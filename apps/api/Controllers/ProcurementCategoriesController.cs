using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// P2 taxonomy (002/003) — writes sit on catalog:write (Q-P5); list/detail are tenant-scoped reads.
[ApiController]
[Route("api/v1/procurement-categories")]
public sealed class ProcurementCategoriesController(IProcurementCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(catalog.Categories());

    [HttpPost]
    public IResult Create(CreateCategoryRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("catalog:write")) return (Envelope.FailBody("FORBIDDEN", "catalog:write permission required."), 403);
        var (category, code, error) = catalog.CreateCategory(u, request.Name, request.ParentId, request.ExternalCode, request.EffectiveFrom);
        return code is null ? (Envelope.OkBody(category!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{id}")]
    public IResult Update(string id, UpdateCategoryRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("catalog:write")) return Envelope.Fail("FORBIDDEN", "catalog:write permission required.", 403);
        var (category, code, error) = catalog.UpdateCategory(u, id, request.Name, request.ParentId, request.ExternalCode, request.EffectiveTo);
        return code is null ? Envelope.Ok<object>(category!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }

    [HttpPost("{id}/deprecations")]
    public IResult Deprecate(string id, DeprecateCategoryRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("catalog:write")) return Envelope.Fail("FORBIDDEN", "catalog:write permission required.", 403);
        var (category, code, error) = catalog.DeprecateCategory(u, id);
        return code is null ? Envelope.Ok<object>(category!) : Envelope.Fail(code, error!, ErrorStatus.StatusFor(code));
    }
}

// P2 purchasing units (004) — conversions must be dimension-compatible and interval-active.
[ApiController]
[Route("api/v1/unit-of-measures")]
public sealed class UnitOfMeasuresController(IProcurementCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public IResult List() => Envelope.Ok<object>(catalog.Units());

    [HttpPost]
    public IResult Create(CreateUomRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("catalog:write")) return (Envelope.FailBody("FORBIDDEN", "catalog:write permission required."), 403);
        var (uom, code, error) = catalog.CreateUom(u, request.Code, request.Name, request.Dimension, request.Precision, request.IsBase);
        return code is null ? (Envelope.OkBody(uom!), 201) : (Envelope.FailBody(code, error!), ErrorStatus.StatusFor(code));
    });

    [HttpPost("{code}/conversions")]
    public IResult AddConversion(string code, CreateConversionRequest request)
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("catalog:write")) return Envelope.Fail("FORBIDDEN", "catalog:write permission required.", 403);
        var (conversion, errCode, error) = catalog.CreateConversion(u, code, request.ToCode, request.Factor, request.EffectiveFrom, request.EffectiveTo);
        return errCode is null ? Envelope.Ok<object>(conversion!, 201) : Envelope.Fail(errCode, error!, ErrorStatus.StatusFor(errCode));
    }
}
