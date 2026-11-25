using Erp.Api.Data;
using Erp.Api.Data.Repositories;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// MF8 — quality plans, sampling, inspections (ERP-MFG-055..058) and MF9 — holds, NCR, CAPA,
// dashboards (ERP-MFG-059..063). SoD triple per 055: R-MFG authors plans (quality:plan), the
// plan approver is not the author, and inspection/disposition live on R-QC (CONSUMED — the role
// is seeded by PROCUREMENT, never by this slice). Finalized inspections are immutable; a
// correction is a NEW linked amended result preserving both rows (058). Holds are the
// availability authority (059 — the flagged InventoryService edit); dispositions restore or
// retire the quantity per decision, scrap posts through the shared period guard. NC closure is
// blocked while containment is open and containment release needs a reviewer other than the
// opener (061). CAPA cannot close until every action is done AND effectiveness is verified
// satisfied (062).
public sealed class QualityService(
    IMfgDeferredRepository mfg, IInventoryRepository inventory, IJournalRepository journals,
    IPurchaseOrderRepository costing, INumberSeries numbers, IAuditRepository audit,
    EntityContext ec, ErpDbContext db) : IQualityService
{
    private const string ScrapAccount = "5200";
    private const string InventoryAccount = "1400";
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- MF8: plans / characteristics / sampling ----

    public object ListPlans(string? itemSku, QualityPlanStatus? status) =>
        mfg.AllQualityPlans(itemSku, status).Select(p => new
        {
            p.Id, p.ItemSku, p.SupplierId, p.ProcessStage, p.Revision, status = p.Status.ToString(),
            p.EffectiveFrom, p.EffectiveTo, p.ApprovedBy, p.ApprovedAt, p.CreatedBy, p.CreatedAt,
            characteristics = mfg.CharacteristicsFor(p.Id).Select(ProjCharacteristic).ToList(),
        }).ToList();

    public (QualityPlan plan, string? code, string? message) CreatePlan(CurrentUser user, Dtos.CreateQualityPlanRequest request)
    {
        if (Guards.Check(request.ItemSku, "ItemSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
        if (request.Revision < 1) return (null!, "VALIDATION_ERROR", "Revision must be 1 or greater.");
        if (request.EffectiveTo is { } t && t < request.EffectiveFrom)
            return (null!, "VALIDATION_ERROR", "Effective-to cannot be before effective-from.");

        var plan = new QualityPlan(numbers.Next("qpl"), ec.TenantId, ec.EntityId, request.ItemSku,
            request.SupplierId, request.ProcessStage, request.Revision, QualityPlanStatus.Draft,
            request.EffectiveFrom, request.EffectiveTo, null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddQualityPlan(plan);
        audit.Log(user, "create", "quality_plan", plan.Id,
            $"Quality plan r{request.Revision} drafted for {request.ItemSku}");
        mfg.Save();
        return (plan, null, null);
    }

    public (object data, string? code, string? message) ApprovePlan(CurrentUser approver, string planId, string? reason)
    {
        var plan = mfg.FindQualityPlan(planId);
        if (plan is null) return (null!, "QUALITY_PLAN_NOT_FOUND", "Quality plan not found.");
        if (plan.Status != QualityPlanStatus.Draft)
            return (null!, "QUALITY_PLAN_STATE_ERROR", $"Only draft plans can be approved (current: {plan.Status}).");
        if (plan.CreatedBy == approver.Id)
            return (null!, "QUALITY_PLAN_APPROVAL_ERROR", "Maker-checker: the plan author cannot approve their own revision (055).");
        if (mfg.CharacteristicsFor(planId).Count == 0)
            return (null!, "QUALITY_PLAN_EMPTY", "A plan without characteristics cannot be approved (055).");

        mfg.SetQualityPlanApproved(plan, approver.Id, DateTimeOffset.UtcNow);
        audit.Log(approver, "approve", "quality_plan", plan.Id,
            $"Quality plan r{plan.Revision} of {plan.ItemSku} approved{Append(reason)}");
        mfg.Save();
        return (new { plan.Id, plan.ItemSku, plan.Revision, status = plan.Status.ToString(), plan.ApprovedBy, plan.ApprovedAt }, null, null);
    }

    public (object data, string? code, string? message) AddCharacteristic(CurrentUser user, string planId, Dtos.AddCharacteristicRequest request)
    {
        var plan = mfg.FindQualityPlan(planId);
        if (plan is null) return (null!, "QUALITY_PLAN_NOT_FOUND", "Quality plan not found.");
        if (plan.Status != QualityPlanStatus.Draft)
            return (null!, "QUALITY_PLAN_STATE_ERROR", "Approved plans are immutable — draft a successor revision.");
        if (Guards.Check(request.Code, "Code") is { } codeError) return (null!, "VALIDATION_ERROR", codeError);
        if (Guards.Check(request.Name, "Name") is { } nameError) return (null!, "VALIDATION_ERROR", nameError);
        if (mfg.FindCharacteristic(planId, request.Code) is not null)
            return (null!, "QUALITY_CHARACTERISTIC_DUPLICATE", $"Characteristic {request.Code} already exists on the plan.");
        if (request.DataType == CharacteristicType.Numeric && request.MinValue is { } min && request.MaxValue is { } max && min > max)
            return (null!, "VALIDATION_ERROR", "MinValue cannot exceed MaxValue.");

        var characteristic = new QualityCharacteristic($"{planId}:{request.Code}", ec.TenantId, ec.EntityId,
            planId, request.Code, request.Name, request.DataType, request.MinValue, request.MaxValue,
            request.TargetValue, request.Unit, request.Mandatory, 1);
        mfg.AddCharacteristic(characteristic);
        audit.Log(user, "add", "quality_characteristic", characteristic.Id, $"Characteristic {request.Code} added to plan {planId}");
        mfg.Save();
        return (ProjCharacteristic(characteristic), null, null);
    }

    public (object data, string? code, string? message) AddSamplingPlan(CurrentUser user, string planId, Dtos.CreateSamplingPlanRequest request)
    {
        var plan = mfg.FindQualityPlan(planId);
        if (plan is null) return (null!, "QUALITY_PLAN_NOT_FOUND", "Quality plan not found.");
        if (request.Kind == SamplingKind.FixedSample && (request.SampleSize is not { } size || size < 1))
            return (null!, "VALIDATION_ERROR", "A fixed-sample plan needs SampleSize >= 1.");

        var sampling = new SamplingPlan(numbers.Next("qpl"), ec.TenantId, ec.EntityId, planId,
            request.Kind, request.SampleSize, user.Id, DateTimeOffset.UtcNow);
        mfg.AddSamplingPlan(sampling);
        audit.Log(user, "add", "sampling_plan", sampling.Id, $"Sampling plan {request.Kind} on plan {planId}");
        mfg.Save();
        return (new { sampling.Id, sampling.QualityPlanId, kind = sampling.Kind.ToString(), sampling.SampleSize }, null, null);
    }

    // ---- MF8: inspections ----

    public (object data, string? code, string? message) CreateInspection(CurrentUser inspector, Dtos.CreateInspectionRequest request)
    {
        var plan = mfg.FindQualityPlan(request.QualityPlanId);
        if (plan is null) return (null!, "QUALITY_PLAN_NOT_FOUND", "Quality plan not found.");
        if (plan.Status != QualityPlanStatus.Approved || plan.EffectiveFrom > Today || plan.EffectiveTo < Today)
            return (null!, "QUALITY_PLAN_NOT_EFFECTIVE", "Inspections require an approved plan effective today (055).");
        if (Guards.Check(request.ItemSku, "ItemSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.PopulationQty, "PopulationQty") is { } popError) return (null!, "VALIDATION_ERROR", popError);

        // sampling (057): the plan revision rides the inspection; full inspection takes the whole
        // population, fixed sample takes the configured size (Q-MF3)
        var sampling = mfg.SamplingFor(plan.Id);
        var sampleSize = sampling?.Kind switch
        {
            SamplingKind.FixedSample => sampling.SampleSize ?? 1,
            _ => (int)Math.Ceiling(request.PopulationQty),
        };

        var inspection = new Inspection(numbers.Next("qin"), ec.TenantId, ec.EntityId, plan.Id,
            plan.Revision, request.OrderId, request.ItemSku, request.LotSerial, sampleSize,
            InspectionStatus.InProgress, inspector.Id, null, null, inspector.Id, DateTimeOffset.UtcNow);
        mfg.AddInspection(inspection);
        audit.Log(inspector, "create", "inspection", inspection.Id,
            $"Inspection {inspection.Id} against plan r{plan.Revision} of {request.ItemSku} (sample {sampleSize})");
        mfg.Save();
        return (ProjInspection(inspection), null, null);
    }

    public (object data, string? code, string? message) AddResult(CurrentUser inspector, string inspectionId, Dtos.AddInspectionResultRequest request)
    {
        var inspection = mfg.FindInspection(inspectionId);
        if (inspection is null) return (null!, "INSPECTION_NOT_FOUND", "Inspection not found.");
        if (inspection.Status == InspectionStatus.Finalized)
            return (null!, "QUALITY_INSPECTION_STATE_ERROR", "The inspection is finalized — results are immutable; file an amendment (058).");
        var characteristic = mfg.FindCharacteristic(inspection.QualityPlanId, request.CharacteristicCode);
        if (characteristic is null)
            return (null!, "QUALITY_CHARACTERISTIC_NOT_FOUND", $"Characteristic {request.CharacteristicCode} is not on plan revision {inspection.PlanRevision}.");
        if (mfg.ResultsFor(inspectionId).Any(r => r.CharacteristicCode == request.CharacteristicCode))
            return (null!, "QUALITY_RESULT_DUPLICATE", "Use an amendment to correct an existing result (058).");

        var result = new InspectionResult($"{inspectionId}:{request.CharacteristicCode}:a1", ec.TenantId, ec.EntityId,
            inspectionId, request.CharacteristicCode, request.RawValue, Evaluate(characteristic, request.RawValue),
            characteristic.RuleVersion, 1, null, inspector.Id, DateTimeOffset.UtcNow);
        mfg.AddInspectionResult(result);
        audit.Log(inspector, "add", "inspection_result", result.Id,
            $"Result {request.CharacteristicCode} = {request.RawValue} (pass: {result.EvaluatedPass?.ToString() ?? "n/a"}, rule v{characteristic.RuleVersion})");
        mfg.Save();
        return (ProjResult(result), null, null);
    }

    // 058 AC2: finalized results are immutable — the correction is a NEW linked amended result
    public (object data, string? code, string? message) AmendResult(CurrentUser inspector, string inspectionId, string resultId, Dtos.AmendResultRequest request)
    {
        var inspection = mfg.FindInspection(inspectionId);
        if (inspection is null) return (null!, "INSPECTION_NOT_FOUND", "Inspection not found.");
        var original = mfg.FindInspectionResult(resultId);
        if (original is null || original.InspectionId != inspectionId)
            return (null!, "INSPECTION_RESULT_NOT_FOUND", "Inspection result not found.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (null!, "VALIDATION_ERROR", "An amendment reason is required — corrections never overwrite silently.");
        var characteristic = mfg.FindCharacteristic(inspection.QualityPlanId, original.CharacteristicCode);
        if (characteristic is null) return (null!, "QUALITY_CHARACTERISTIC_NOT_FOUND", "Characteristic not found.");

        var amendmentNo = mfg.ResultsFor(inspectionId).Count(r => r.CharacteristicCode == original.CharacteristicCode) + 1;
        var amended = new InspectionResult($"{inspectionId}:{original.CharacteristicCode}:a{amendmentNo}",
            ec.TenantId, ec.EntityId, inspectionId, original.CharacteristicCode, request.RawValue,
            Evaluate(characteristic, request.RawValue), characteristic.RuleVersion, amendmentNo,
            original.Id, inspector.Id, DateTimeOffset.UtcNow);
        mfg.AddInspectionResult(amended);
        audit.Log(inspector, "amend", "inspection_result", amended.Id,
            $"Result {original.CharacteristicCode} amended (a{amendmentNo}): {request.Reason}");
        mfg.Save();
        return (ProjResult(amended), null, null);
    }

    public (object data, string? code, string? message) Finalize(CurrentUser inspector, string inspectionId, Dtos.FinalizeInspectionRequest request)
    {
        var inspection = mfg.FindInspection(inspectionId);
        if (inspection is null) return (null!, "INSPECTION_NOT_FOUND", "Inspection not found.");
        if (inspection.Status == InspectionStatus.Finalized)
            return (null!, "QUALITY_INSPECTION_STATE_ERROR", "The inspection is already finalized (058).");

        // 058 AC1: incomplete context blocks finalization — every MANDATORY characteristic of
        // the exact plan revision must carry a result
        var characteristics = mfg.CharacteristicsFor(inspection.QualityPlanId);
        var entered = mfg.ResultsFor(inspectionId).Select(r => r.CharacteristicCode).ToHashSet();
        var missing = characteristics.Where(c => c.Mandatory && !entered.Contains(c.Code)).ToList();
        if (missing.Count > 0)
            return (null!, "QUALITY_INSPECTION_INCOMPLETE",
                "Mandatory characteristics without results: " + string.Join(", ", missing.Select(c => c.Code)));

        if (!mfg.SetInspectionFinalized(inspection, inspector.Id, DateTimeOffset.UtcNow))
            return (null!, "QUALITY_INSPECTION_STATE_ERROR", "Finalization raced another decision — reload.");
        audit.Log(inspector, "finalize", "inspection", inspection.Id,
            $"Inspection finalized{Append(request?.Note)} — results immutable from here (058)");
        mfg.Save();
        return (ProjInspection(inspection), null, null);
    }

    public object Results(string inspectionId) => mfg.ResultsFor(inspectionId).Select(ProjResult).ToList();

    private static bool? Evaluate(QualityCharacteristic characteristic, string rawValue) =>
        characteristic.DataType switch
        {
            CharacteristicType.Numeric when decimal.TryParse(rawValue, out var value) =>
                (characteristic.MinValue is null || value >= characteristic.MinValue)
                && (characteristic.MaxValue is null || value <= characteristic.MaxValue),
            CharacteristicType.Boolean => bool.TryParse(rawValue, out var b) ? b : null,
            _ => null, // free-text characteristics carry no automatic pass/fail
        };

    // ---- MF9: holds / dispositions ----

    public (object data, string? code, string? message) Hold(CurrentUser user, Dtos.CreateQualityHoldRequest request)
    {
        if (!user.Has("quality:inspect") && !user.Has("quality:dispose"))
            return (null!, "FORBIDDEN", "quality:inspect permission required.");
        if (Guards.Check(request.ItemSku, "ItemSku") is { } skuError) return (null!, "VALIDATION_ERROR", skuError);
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);

        var hold = new QualityHold(numbers.Next("qhd"), ec.TenantId, ec.EntityId, request.ItemSku,
            request.LotSerial, request.Qty, request.OrderId, request.InspectionId, request.NonconformanceId,
            QualityHoldStatus.Open, null, null, null, user.Id, DateTimeOffset.UtcNow, null, null);
        mfg.AddQualityHold(hold);
        audit.Log(user, "hold", "quality_hold", hold.Id,
            $"Hold placed on {request.Qty} x {request.ItemSku} — availability reduced immediately (059)");
        mfg.Save();
        return (ProjHold(hold), null, null);
    }

    // 059: only a permitted state allows movement — the disposition restores (Release/Rework/
    // Return) or retires (Scrap) the held quantity; scrap draws stock through the period guard
    public (object data, string? code, string? message) DisposeHold(CurrentUser disposer, string holdId, Dtos.HoldDispositionRequest request)
    {
        var hold = mfg.FindQualityHold(holdId);
        if (hold is null) return (null!, "QUALITY_HOLD_NOT_FOUND", "Quality hold not found.");
        if (!disposer.Has("quality:dispose"))
            return (null!, "FORBIDDEN", "quality:dispose permission required.");
        if (hold.HeldBy == disposer.Id)
            return (null!, "QUALITY_DISPOSITION_ERROR", "Maker-checker: the holder cannot dispose their own hold (059).");
        if (Guards.CheckPositive(request.Qty, "Qty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);
        if (request.Qty > hold.Qty)
            return (null!, "VALIDATION_ERROR", $"Disposition qty exceeds the held quantity ({hold.Qty}).");

        string? journalId = null;
        if (request.Disposition == HoldDisposition.Scrap)
        {
            // scrap disposal posts stock + a balanced journal under the ONE period guard
            var (guardCode, guardMsg) = FinanceGuards.EnsurePeriodOpen(db, ec, Today);
            if (guardCode is not null) return (null!, guardCode, guardMsg);
            var unitCost = costing.AverageUnitCost(hold.ItemSku); // the landed weighted-average basis
            var value = decimal.Round(Math.Abs(request.Qty * unitCost), 2, MidpointRounding.AwayFromZero);
            var lines = new JournalLine[]
            {
                new(ScrapAccount, "Production Scrap and Yield Loss", value, 0),
                new(InventoryAccount, "Inventory", 0, value),
            };
            var journal = new Journal(numbers.Next("j"), ec.TenantId, ec.EntityId, JournalStatus.Posted,
                $"Quality hold {hold.Id} scrap disposal ({request.Qty} x {hold.ItemSku})", lines, DateTimeOffset.UtcNow, disposer.Id);
            journals.Add(journal);
            inventory.DrawStock(hold.ItemSku, request.Qty);
            journalId = journal.Id;
            audit.Emit(disposer, "quality.scrap.posted", new { holdId, qty = request.Qty, itemSku = hold.ItemSku, journalId });
        }

        if (!mfg.SetHoldDispositioned(hold, request.Disposition, request.Qty, disposer.Id, DateTimeOffset.UtcNow, journalId))
            return (null!, "QUALITY_HOLD_STATE_ERROR", "The hold was already dispositioned.");
        audit.Log(disposer, "dispose", "quality_hold", hold.Id,
            $"Disposition {request.Disposition} on {request.Qty} x {hold.ItemSku}{Append(request.Reason)}");
        mfg.Save();
        return (ProjHold(hold), null, null);
    }

    public object Holds() => mfg.AllHolds().Select(ProjHold).ToList();

    // ---- MF9: nonconformances / containment ----

    public (object data, string? code, string? message) RaiseNc(CurrentUser user, Dtos.CreateNcRequest request)
    {
        if (Guards.Check(request.SourceEventId, "SourceEventId") is { } srcError) return (null!, "VALIDATION_ERROR", srcError);
        if (Guards.Check(request.Title, "Title") is { } titleError) return (null!, "VALIDATION_ERROR", titleError);
        if (Guards.CheckPositive(request.AffectedQty, "AffectedQty") is { } qtyError) return (null!, "VALIDATION_ERROR", qtyError);
        if (request.Objects.Count == 0)
            return (null!, "VALIDATION_ERROR", "An NC references at least one affected object (060 AC1).");

        var nc = new Nonconformance(numbers.Next("ncr"), ec.TenantId, ec.EntityId, request.SourceEventId,
            request.Title, request.ItemSku, request.OrderId, request.AffectedQty, NcStatus.Open,
            null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddNonconformance(nc);
        foreach (var obj in request.Objects)
            mfg.AddNcObject(new NcObject($"{nc.Id}:{obj.ObjectRef}", ec.TenantId, ec.EntityId, nc.Id,
                obj.ObjectType, obj.ObjectRef, obj.Qty));
        audit.Log(user, "raise", "nonconformance", nc.Id, $"NC raised: {request.Title} ({request.AffectedQty} affected)");
        mfg.Save();
        return (ProjNc(nc), null, null);
    }

    public (object data, string? code, string? message) AddContainment(CurrentUser user, string ncId, Dtos.CreateContainmentRequest request)
    {
        var nc = mfg.FindNonconformance(ncId);
        if (nc is null) return (null!, "NC_NOT_FOUND", "Nonconformance not found.");
        if (Guards.Check(request.Kind, "Kind") is { } kindError) return (null!, "VALIDATION_ERROR", kindError);
        var containment = new ContainmentAction(numbers.Next("ncr"), ec.TenantId, ec.EntityId, ncId,
            request.Kind, "Open", user.Id, null, null, null);
        mfg.AddContainment(containment);
        audit.Log(user, "contain", "nonconformance", ncId, $"Containment {request.Kind} opened");
        mfg.Save();
        return (ProjContainment(containment), null, null);
    }

    public (object data, string? code, string? message) ReleaseContainment(CurrentUser reviewer, string containmentId, Dtos.ReleaseContainmentRequest request)
    {
        var containment = mfg.FindContainment(containmentId);
        if (containment is null) return (null!, "CONTAINMENT_NOT_FOUND", "Containment action not found.");
        if (!reviewer.Has("quality:dispose"))
            return (null!, "FORBIDDEN", "quality:dispose permission required to release containment (061).");
        if (containment.OpenedBy == reviewer.Id)
            return (null!, "CONTAINMENT_RELEASE_ERROR", "Maker-checker: only an authorized reviewer other than the opener releases containment (061 AC2).");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (null!, "VALIDATION_ERROR", "A release reason is required.");
        if (!mfg.SetContainmentReleased(containment, reviewer.Id, DateTimeOffset.UtcNow))
            return (null!, "CONTAINMENT_STATE_ERROR", "The containment was already released.");
        audit.Log(reviewer, "release", "containment", containmentId, $"Containment released: {request.Reason}");
        mfg.Save();
        return (ProjContainment(containment), null, null);
    }

    public (object data, string? code, string? message) CloseNc(CurrentUser reviewer, string ncId)
    {
        var nc = mfg.FindNonconformance(ncId);
        if (nc is null) return (null!, "NC_NOT_FOUND", "Nonconformance not found.");
        if (!reviewer.Has("quality:dispose"))
            return (null!, "FORBIDDEN", "quality:dispose permission required.");
        // 061 AC2: closure is blocked while any mandatory containment is still open
        if (mfg.ContainmentsFor(ncId).Any(c => c.Status == "Open"))
            return (null!, "NC_CONTAINMENT_OPEN", "Containment is still open — release containment before closing the NC (061).");
        if (!mfg.SetNcClosed(nc, reviewer.Id, DateTimeOffset.UtcNow))
            return (null!, "NC_STATE_ERROR", "The NC is already closed.");
        audit.Log(reviewer, "close", "nonconformance", ncId, "NC closed");
        mfg.Save();
        return (ProjNc(nc), null, null);
    }

    public object ListNcs() => mfg.AllNonconformances().Select(n => ProjNc(n)).ToList();

    // ---- MF9: CAPA ----

    public (object data, string? code, string? message) CreateCapa(CurrentUser user, Dtos.CreateCapaRequest request)
    {
        if (Guards.Check(request.Title, "Title") is { } titleError) return (null!, "VALIDATION_ERROR", titleError);
        if (Guards.Check(request.EffectivenessCriteria, "EffectivenessCriteria") is { } critError) return (null!, "VALIDATION_ERROR", critError);
        var capa = new CapaCase(numbers.Next("cpa"), ec.TenantId, ec.EntityId, request.Title,
            request.NonconformanceId, request.EffectivenessCriteria, CapaStatus.Open,
            null, null, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCapaCase(capa);
        audit.Log(user, "create", "capa_case", capa.Id, $"CAPA case opened: {request.Title}");
        mfg.Save();
        return (ProjCapa(capa), null, null);
    }

    public (object data, string? code, string? message) AddCapaAction(CurrentUser user, string capaId, Dtos.AddCapaActionRequest request)
    {
        var capa = mfg.FindCapaCase(capaId);
        if (capa is null) return (null!, "CAPA_NOT_FOUND", "CAPA case not found.");
        if (Guards.Check(request.Description, "Description") is { } descError) return (null!, "VALIDATION_ERROR", descError);
        var action = new CapaAction($"{capaId}:a{mfg.CapaActionsFor(capaId).Count + 1}", ec.TenantId, ec.EntityId,
            capaId, request.Description, request.DueBy, false, null, null);
        mfg.AddCapaAction(action);
        audit.Log(user, "add", "capa_action", action.Id, $"CAPA action added: {request.Description}");
        mfg.Save();
        return (ProjCapaAction(action), null, null);
    }

    public (object data, string? code, string? message) CompleteCapaAction(CurrentUser user, string actionId, Dtos.CompleteCapaActionRequest request)
    {
        var action = mfg.FindCapaAction(actionId);
        if (action is null) return (null!, "CAPA_ACTION_NOT_FOUND", "CAPA action not found.");
        if (action.Done) return (null!, "CAPA_ACTION_STATE_ERROR", "The action is already complete.");
        mfg.SetCapaActionDone(action, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "complete", "capa_action", actionId, "CAPA action complete");
        mfg.Save();
        return (ProjCapaAction(action), null, null);
    }

    public (object data, string? code, string? message) VerifyCapa(CurrentUser user, string capaId, Dtos.CapaVerificationRequest request)
    {
        var capa = mfg.FindCapaCase(capaId);
        if (capa is null) return (null!, "CAPA_NOT_FOUND", "CAPA case not found.");
        if (Guards.Check(request.CriteriaResult, "CriteriaResult") is { } err) return (null!, "VALIDATION_ERROR", err);
        var verification = new CapaVerification(numbers.Next("cpa"), ec.TenantId, ec.EntityId, capaId,
            request.CriteriaResult, request.Satisfied, user.Id, DateTimeOffset.UtcNow);
        mfg.AddCapaVerification(verification);
        audit.Log(user, "verify", "capa_case", capaId, $"Effectiveness verified: {request.CriteriaResult} (satisfied: {request.Satisfied})");
        mfg.Save();
        return (new { verification.Id, verification.CapaCaseId, verification.CriteriaResult, verification.Satisfied, verification.VerifiedBy, verification.At }, null, null);
    }

    public (object data, string? code, string? message) CloseCapa(CurrentUser closer, string capaId)
    {
        var capa = mfg.FindCapaCase(capaId);
        if (capa is null) return (null!, "CAPA_NOT_FOUND", "CAPA case not found.");
        if (!closer.Has("quality:dispose"))
            return (null!, "FORBIDDEN", "quality:dispose permission required.");
        if (capa.CreatedBy == closer.Id)
            return (null!, "CAPA_CLOSE_ERROR", "Maker-checker: the case creator cannot close their own case.");
        // 062 AC1: CAPA cannot close until configured actions + effectiveness criteria are satisfied
        var openActions = mfg.CapaActionsFor(capaId).Where(a => !a.Done).ToList();
        if (openActions.Count > 0)
            return (null!, "CAPA_CLOSE_BLOCKED",
                "Open actions remain: " + string.Join(", ", openActions.Select(a => a.Id)));
        var verifications = mfg.CapaVerificationsFor(capaId);
        if (verifications.Count == 0 || !verifications.Any(v => v.Satisfied))
            return (null!, "CAPA_CLOSE_BLOCKED",
                "Effectiveness criteria are not satisfied — verification is required before closure (062).");
        if (!mfg.SetCapaClosed(capa, closer.Id, DateTimeOffset.UtcNow))
            return (null!, "CAPA_STATE_ERROR", "The case is already closed.");
        audit.Log(closer, "close", "capa_case", capaId, "CAPA case closed — actions done, effectiveness satisfied");
        mfg.Save();
        return (ProjCapa(capa), null, null);
    }

    public object ListCapas() => mfg.AllCapaCases().Select(c => ProjCapa(c)).ToList();

    // 063: read-only dashboard — declares as-of + source modules, tenant-scoped by construction
    public object Dashboard() => new
    {
        asOf = DateTimeOffset.UtcNow,
        sourceModules = new[] { "mfg.quality", "inventory" },
        openHolds = mfg.AllHolds().Count(h => h.Status == QualityHoldStatus.Open),
        heldQty = mfg.AllHolds().Where(h => h.Status == QualityHoldStatus.Open).Sum(h => h.Qty),
        nonconformancesOpen = mfg.AllNonconformances().Count(n => n.Status == NcStatus.Open),
        capaOpen = mfg.AllCapaCases().Count(c => c.Status == CapaStatus.Open),
        inspectionsInProgress = db.Inspections
            .Count(i => i.TenantId == ec.TenantId && i.LegalEntityId == ec.EntityId && i.Status == InspectionStatus.InProgress),
    };

    private static object ProjCharacteristic(QualityCharacteristic c) => new
    {
        c.Id, c.PlanId, c.Code, c.Name, dataType = c.DataType.ToString(), c.MinValue, c.MaxValue,
        c.TargetValue, c.Unit, c.Mandatory, c.RuleVersion,
    };

    private static object ProjInspection(Inspection i) => new
    {
        i.Id, i.QualityPlanId, i.PlanRevision, i.OrderId, i.ItemSku, i.LotSerial, i.SampleSize,
        status = i.Status.ToString(), i.InspectorId, i.FinalizedBy, i.FinalizedAt, i.CreatedAt,
    };

    private static object ProjResult(InspectionResult r) => new
    {
        r.Id, r.InspectionId, r.CharacteristicCode, r.RawValue, r.EvaluatedPass, r.RuleVersion,
        r.AmendmentNo, r.AmendsId, r.EnteredBy, r.EnteredAt,
    };

    private static object ProjHold(QualityHold h) => new
    {
        h.Id, h.ItemSku, h.LotSerial, h.Qty, h.OrderId, h.InspectionId, h.NonconformanceId,
        status = h.Status.ToString(), disposition = h.Disposition?.ToString(), h.DispositionedQty,
        h.JournalId, h.HeldBy, h.HeldAt, h.DispositionedBy, h.DispositionedAt,
    };

    private object ProjNc(Nonconformance n) => new
    {
        n.Id, n.SourceEventId, n.Title, n.ItemSku, n.OrderId, n.AffectedQty, status = n.Status.ToString(),
        n.ClosedBy, n.ClosedAt, n.RaisedBy, n.RaisedAt,
        objects = mfg.NcObjectsFor(n.Id).Select(o => new { o.ObjectType, o.ObjectRef, o.Qty }).ToList(),
        containments = mfg.ContainmentsFor(n.Id).Select(c => new { c.Id, c.Kind, status = c.Status.ToString(), c.OpenedBy, c.ReleasedBy }).ToList(),
    };

    private static object ProjContainment(ContainmentAction c) => new
    {
        c.Id, c.NonconformanceId, c.Kind, status = c.Status.ToString(), c.OpenedBy,
        c.ReleasedBy, c.ReleasedAt, c.ReleaseReason,
    };

    private static object ProjCapa(CapaCase c) => new
    {
        c.Id, c.Title, c.NonconformanceId, c.EffectivenessCriteria, status = c.Status.ToString(),
        c.ClosedBy, c.ClosedAt, c.CreatedBy, c.CreatedAt,
    };

    private static object ProjCapaAction(CapaAction a) => new
    {
        a.Id, a.CapaCaseId, a.Description, a.DueBy, a.Done, a.CompletedBy, a.CompletedAt,
    };

    private static string Append(string? reason) => string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}";
}
