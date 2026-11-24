using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// BOM/recipe revisions (ERP-MFG-014/015). Approved revisions are immutable — a change is a NEW
// revision; effective intervals of LIVE (Draft/Approved) revisions of one output sku must never
// overlap, so exactly one revision is selectable per sku on a date (AC2). Retiring an approved
// revision is the sanctioned way to open room for its successor: retired rows leave the live set,
// released orders keep their frozen revision id (015 edge).
public sealed class BomService(
    IManufacturingRepository boms, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IBomService
{
    public object List(string? outputSku, BomStatus? status)
    {
        var revisions = boms.AllBoms(outputSku, status);
        var lines = boms.LinesForRevisions(revisions.Select(r => r.Id).ToList())
            .GroupBy(l => l.BomRevisionId).ToDictionary(g => g.Key, g => g.ToList());
        return revisions.Select(r => ProjBom(r, lines.GetValueOrDefault(r.Id, []))).ToList();
    }

    public (object? data, string? code, string? message) Create(CurrentUser user, Dtos.CreateBomRequest request)
    {
        // DTO validation: OutputSku required, Revision >= 1, OutputQtyPer > 0 (015 AC2 basis),
        // interval sane
        if (Guards.Check(request.OutputSku, "OutputSku") is { } skuError) return (null, "VALIDATION_ERROR", skuError);
        if (request.Revision < 1) return (null, "VALIDATION_ERROR", "Revision must be at least 1.");
        if (Guards.CheckPositive(request.OutputQtyPer, "OutputQtyPer") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        if (request.EffectiveTo is { } to && to < request.EffectiveFrom)
            return (null, "VALIDATION_ERROR", "EffectiveTo must not be before EffectiveFrom.");

        // 014 edge: the effective interval must not overlap a LIVE revision of the same output sku
        if (Overlapping(request.OutputSku, request.EffectiveFrom, request.EffectiveTo, excluding: null) is { } clash)
            return (null, "BOM_EFFECTIVE_OVERLAP",
                $"Revision {clash.Revision} of {request.OutputSku} ({clash.Status}) already covers " +
                $"{clash.EffectiveFrom:yyyy-MM-dd}..{clash.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}; " +
                $"intervals of live revisions must not overlap.");

        var bom = new BomRevision(numbers.Next("BOM"), ec.TenantId, ec.EntityId, request.OutputSku,
            request.Revision, BomStatus.Draft, request.OutputQtyPer, request.EffectiveFrom, request.EffectiveTo,
            null, null, user.Id, DateTimeOffset.UtcNow);
        boms.AddBom(bom);
        audit.Log(user, "create", "bom_revision", bom.Id,
            $"BOM revision {request.Revision} drafted for {request.OutputSku}: batch basis {request.OutputQtyPer}, " +
            $"effective {request.EffectiveFrom:yyyy-MM-dd}..{request.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}");
        boms.Save();
        return (ProjBom(bom, []), null, null);
    }

    public (object? data, string? code, string? message) AddLine(CurrentUser user, string bomId, Dtos.AddBomLineRequest request)
    {
        var bom = boms.FindBom(bomId);
        if (bom is null) return (null, "BOM_NOT_FOUND", "BOM revision not found.");
        if (bom.Status != BomStatus.Draft)
            return (null, "BOM_STATE_ERROR",
                $"Only draft revisions accept component lines (current: {bom.Status}); an approved revision is " +
                $"immutable (014 AC1) — a change creates a successor revision.");

        if (Guards.Check(request.ComponentSku, "ComponentSku") is { } skuError) return (null, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.QtyPer, "QtyPer") is { } qtyError) return (null, "VALIDATION_ERROR", qtyError);
        if (Guards.Check(request.Unit, "Unit") is { } unitError) return (null, "VALIDATION_ERROR", unitError);
        if (request.UnitCost < 0) return (null, "VALIDATION_ERROR", "UnitCost must be >= 0.");

        // natural key {revision}:{component} — a duplicate line is a 409, not a silent overwrite
        if (boms.FindLine(bomId, request.ComponentSku) is not null)
            return (null, "BOM_LINE_DUPLICATE",
                $"Component {request.ComponentSku} already has a line on revision {bom.Revision} of {bom.OutputSku}.");

        var line = new BomLine($"{bomId}:{request.ComponentSku}", ec.TenantId, ec.EntityId, bomId,
            request.ComponentSku, request.QtyPer, request.Unit, request.IssuePolicy, request.UnitCost);
        boms.AddLine(line);
        audit.Log(user, "add", "bom_line", line.Id,
            $"Line {request.ComponentSku} x{request.QtyPer} {request.Unit} @ {request.UnitCost:0.####} " +
                $"({request.IssuePolicy}) added to {bomId}");
        boms.Save();
        return (ProjLine(line), null, null);
    }

    // 014 separable roles: the author never approves their own revision (approver ≠ author)
    public (object? data, string? code, string? message) Approve(CurrentUser approver, string bomId, string? reason)
    {
        var bom = boms.FindBom(bomId);
        if (bom is null) return (null, "BOM_NOT_FOUND", "BOM revision not found.");
        if (bom.Status != BomStatus.Draft)
            return (null, "BOM_STATE_ERROR", $"Only draft revisions can be approved (current: {bom.Status}).");
        if (bom.CreatedBy == approver.Id)
            return (null, "BOM_APPROVAL_ERROR", "Maker-checker: the author cannot approve their own BOM revision.");

        // belt-and-braces: approval must not create an overlap against another live APPROVED revision
        if (Overlapping(bom.OutputSku, bom.EffectiveFrom, bom.EffectiveTo, excluding: bom.Id) is { } clash
            && clash.Status == BomStatus.Approved)
            return (null, "BOM_EFFECTIVE_OVERLAP",
                $"Approving would overlap approved revision {clash.Revision} of {bom.OutputSku} " +
                $"({clash.EffectiveFrom:yyyy-MM-dd}..{clash.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"}).");

        boms.SetBomApproved(bom, approver.Id, DateTimeOffset.UtcNow);
        audit.Log(approver, "approve", "bom_revision", bom.Id,
            $"BOM revision {bom.Revision} of {bom.OutputSku} approved"
            + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}"));
        audit.Emit(approver, "mfg.bom.approved", new
        {
            bomId = bom.Id, outputSku = bom.OutputSku, revision = bom.Revision,
            outputQtyPer = bom.OutputQtyPer, effectiveFrom = bom.EffectiveFrom, effectiveTo = bom.EffectiveTo,
            approvedBy = approver.Id,
        });
        boms.Save();
        return (ProjBom(bom, boms.LinesForRevision(bom.Id)), null, null);
    }

    // retirement blocks NEW order planning; already-released orders keep their frozen revision
    public (object? data, string? code, string? message) Retire(CurrentUser user, string bomId, string? reason)
    {
        var bom = boms.FindBom(bomId);
        if (bom is null) return (null, "BOM_NOT_FOUND", "BOM revision not found.");
        if (bom.Status != BomStatus.Approved)
            return (null, "BOM_STATE_ERROR",
                $"Only approved revisions are retired (current: {bom.Status}); a draft is simply abandoned.");

        boms.SetBomRetired(bom);
        audit.Log(user, "retire", "bom_revision", bom.Id,
            $"BOM revision {bom.Revision} of {bom.OutputSku} retired — new order planning will no longer resolve it"
            + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}"));
        boms.Save();
        return (ProjBom(bom, boms.LinesForRevision(bom.Id)), null, null);
    }

    // ---- helpers ----

    private BomRevision? Overlapping(string outputSku, DateOnly from, DateOnly? to, string? excluding)
    {
        DateOnly end = to ?? DateOnly.MaxValue;
        return boms.LiveBomsFor(outputSku).FirstOrDefault(b =>
            b.Id != excluding && b.EffectiveFrom <= end && (b.EffectiveTo ?? DateOnly.MaxValue) >= from);
    }

    private static object ProjBom(BomRevision r, IReadOnlyList<BomLine> lines) => new
    {
        r.Id, r.OutputSku, r.Revision, r.Status, r.OutputQtyPer, r.EffectiveFrom, r.EffectiveTo,
        r.ApprovedBy, r.ApprovedAt, r.CreatedBy, r.CreatedAt,
        lines = lines.Select(ProjLine).ToList(),
    };

    private static object ProjLine(BomLine l) => new
    {
        l.Id, l.ComponentSku, l.QtyPer, l.Unit, l.IssuePolicy, l.UnitCost,
    };
}
