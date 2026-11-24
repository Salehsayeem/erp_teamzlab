using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// T6 contracts (ERP-PROCUREMENT-036/037/038/039, rulings R-T6f..h): commitment-only — nothing is
// ever posted to the ledger here; money moves at goods receipt (ReceiptService, unchanged).
// Amendments and price lines are insert-only (executed versions immutable, 037 AC1/AC2); overdue
// obligations are computed on read, never stored (038 AC2); renewal alerting is the
// expiringWithinDays flag plus manual exercise (R-T6g — no scheduler).
public sealed class ContractsService(
    IContractsRepository contracts, ISourcingRepository sourcing, ISupplierRepository suppliers,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec) : IContractsService
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

    public object List(ContractStatus? status, int? expiringWithinDays)
    {
        var rows = contracts.AllContracts(status);
        if (expiringWithinDays is { } days) rows = rows.Where(ExpiringWithin(days)).ToList();
        return rows.Select(c => ProjContract(c,
            contracts.AmendmentsForContract(c.Id).Count,
            contracts.ObligationsForContract(c.Id).Count(o => Overdue(o)))).ToList();
    }

    // 036 AC1: the award reference must resolve to an APPROVED award; supplier/currency/validity
    // carry forward from the recommendation flow; one contract per award
    public (object? data, string? code, string? message) Create(CurrentUser user, CreateContractRequest request)
    {
        if (suppliers.Find(request.SupplierId) is null)
            return (null, "SUPPLIER_NOT_FOUND", $"Supplier {request.SupplierId} not found.");
        var award = default(Award);
        if (request.AwardId is { } awardId)
        {
            award = sourcing.FindAward(awardId);
            if (award is null) return (null, "AWARD_NOT_FOUND", "Award not found.");
            if (award.Status != AwardStatus.Approved)
                return (null, "AWARD_NOT_APPROVED", $"Award {awardId} is {award.Status}; contracts need an approved award (036 AC1).");
            if (award.SupplierId != request.SupplierId)
                return (null, "AWARD_SUPPLIER_MISMATCH", $"Award {awardId} was made to {award.SupplierId}, not {request.SupplierId}.");
            if (contracts.AllContracts(null).Any(c => c.AwardId == awardId))
                return (null, "AWARD_ALREADY_CONTRACTED", $"Award {awardId} already has a contract.");
        }

        var contract = new Contract(numbers.Next("CTR"), ec.TenantId, ec.EntityId, request.SupplierId,
            request.AwardId, ContractStatus.Draft, request.Currency, request.EffectiveFrom, request.EffectiveTo,
            request.RenewalNoticeDays, request.OptionPeriodsTotal, 0, user.Id, Version: 1, null, null,
            user.Id, DateTimeOffset.UtcNow);
        contracts.AddContract(contract);
        audit.Log(user, "create", "contract", contract.Id,
            $"Contract {contract.Id} drafted for {request.SupplierId} ({request.Currency}, {request.EffectiveFrom:yyyy-MM-dd}..{request.EffectiveTo:yyyy-MM-dd})"
            + (award is null ? "" : $" from award {award.Id}"));
        contracts.Save();
        return (ProjContract(contract, 0, 0), null, null);
    }

    // 036 AC3: legal/business review is maker-checked and needs at least one price line
    public (object? data, string? code, string? message) Activate(CurrentUser user, string contractId)
    {
        var contract = contracts.FindContract(contractId);
        if (contract is null) return (null, "CONTRACT_NOT_FOUND", "Contract not found.");
        if (contract.Status != ContractStatus.Draft)
            return (null, "CONTRACT_STATE_ERROR", $"Only draft contracts can be activated (current: {contract.Status}).");
        if (contract.CreatedBy == user.Id)
            return (null, "CONTRACT_APPROVAL_ERROR", "Maker-checker: the contract creator cannot run the activation review.");
        if (!user.Has("contracts:approve"))
            return (null, "FORBIDDEN", "contracts:approve permission required.");
        if (contracts.PriceLinesForContract(contractId).Count == 0)
            return (null, "CONTRACT_NO_PRICE_LINES", "Activation needs at least one contract price line.");

        contracts.SetContractStatus(contract, ContractStatus.Active, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "activate", "contract", contractId, $"Contract {contractId} activated by {user.Email}");
        audit.Emit(user, "procurement.contract.activated", new { contractId, activatedBy = user.Id });
        contracts.Save();
        return (ProjContract(contract, contracts.AmendmentsForContract(contractId).Count, 0), null, null);
    }

    // 037: versions are strictly server-assigned and append-only; new price lines may not overlap
    // any existing range for the same SKU (AC3)
    public (object? data, string? code, string? message) AddAmendment(CurrentUser user, string contractId, AddAmendmentRequest request)
    {
        var contract = contracts.FindContract(contractId);
        if (contract is null) return (null, "CONTRACT_NOT_FOUND", "Contract not found.");
        if (contract.Status is not (ContractStatus.Draft or ContractStatus.Active))
            return (null, "CONTRACT_STATE_ERROR", $"A {contract.Status} contract takes no amendments.");

        var existing = contracts.PriceLinesForContract(contractId).ToList();
        foreach (var line in request.PriceLines)
            if (existing.Any(e => e.ItemSku == line.ItemSku && Overlaps(e, line.EffectiveFrom, line.EffectiveTo))
                || request.PriceLines.Any(o => o != line && o.ItemSku == line.ItemSku && LineOverlaps(o, line.EffectiveFrom, line.EffectiveTo)))
                return (null, "CONTRACT_PRICE_OVERLAP",
                    $"Price line {line.ItemSku} {line.EffectiveFrom:yyyy-MM-dd}..{line.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open"} overlaps an active range (037 AC3).");

        var version = contract.Version + 1;
        var amendment = new ContractAmendment(numbers.Next("AMD"), ec.TenantId, ec.EntityId, contractId,
            version, request.EffectiveDate, request.Summary, user.Id, DateTimeOffset.UtcNow);
        contracts.AddAmendment(amendment);
        foreach (var line in request.PriceLines)
            contracts.AddPriceLine(new ContractPriceLine($"{contractId}:{line.ItemSku}:{line.EffectiveFrom:yyyyMMdd}",
                ec.TenantId, ec.EntityId, contractId, line.ItemSku, line.UnitPrice, line.EffectiveFrom, line.EffectiveTo));
        contracts.SetContractVersion(contract, version);
        audit.Log(user, "amend", "contract", contractId,
            $"Amendment v{version} recorded: {request.Summary} ({request.PriceLines.Count} price line(s))");
        contracts.Save();
        return (new { amendment = ProjAmendment(amendment), contract = ProjContract(contract, version - 1, 0) }, null, null);
    }

    public (object? data, string? code, string? message) AddObligation(CurrentUser user, string contractId, AddObligationRequest request)
    {
        var contract = contracts.FindContract(contractId);
        if (contract is null) return (null, "CONTRACT_NOT_FOUND", "Contract not found.");
        var obligation = new ContractObligation(numbers.Next("OBL"), ec.TenantId, ec.EntityId, contractId,
            request.ObligationType, request.OwnerUserId, request.DueDate, request.SourceClause,
            ObligationStatus.Open, null, null, null, null, user.Id);
        contracts.AddObligation(obligation);
        audit.Log(user, "add", "contract_obligation", obligation.Id,
            $"Obligation '{request.ObligationType}' on {contractId}: owner {request.OwnerUserId}, due {request.DueDate:yyyy-MM-dd}");
        contracts.Save();
        return (ProjObligation(obligation), null, null);
    }

    // 038 AC3: evidence only marks EvidenceSubmitted — Verified is reachable ONLY via the verify step
    public (object? data, string? code, string? message) SubmitEvidence(CurrentUser user, string contractId, string obligationId, SubmitEvidenceRequest request)
    {
        var (obligation, code, message) = LoadObligation(contractId, obligationId);
        if (code is not null) return (null, code, message);
        if (obligation!.Status is not (ObligationStatus.Open or ObligationStatus.EvidenceSubmitted))
            return (null, "OBLIGATION_STATE_ERROR", $"A {obligation.Status} obligation takes no evidence.");
        contracts.SetObligationEvidence(obligation, request.EvidenceRef);
        audit.Log(user, "submit", "contract_obligation", obligationId, $"Evidence {request.EvidenceRef} submitted; awaiting verification");
        contracts.Save();
        return (ProjObligation(obligation), null, null);
    }

    public (object? data, string? code, string? message) VerifyObligation(CurrentUser user, string contractId, string obligationId)
    {
        var (obligation, code, message) = LoadObligation(contractId, obligationId);
        if (code is not null) return (null, code, message);
        if (obligation!.Status != ObligationStatus.EvidenceSubmitted)
            return (null, "OBLIGATION_STATE_ERROR", $"Verification needs submitted evidence (current: {obligation.Status}).");
        if (obligation.CreatedBy == user.Id)
            return (null, "OBLIGATION_APPROVAL_ERROR", "Maker-checker: the obligation creator cannot verify it.");
        if (!user.Has("contracts:approve"))
            return (null, "FORBIDDEN", "contracts:approve permission required.");

        contracts.SetObligationVerified(obligation, user.Id, DateTimeOffset.UtcNow);
        audit.Log(user, "verify", "contract_obligation", obligationId, $"Obligation '{obligation.ObligationType}' verified by {user.Email}");
        audit.Emit(user, "procurement.obligation.verified", new { contractId, obligationId, verifiedBy = user.Id });
        contracts.Save();
        return (ProjObligation(obligation), null, null);
    }

    // 038 edge: waived, not deleted
    public (object? data, string? code, string? message) WaiveObligation(CurrentUser user, string contractId, string obligationId, WaiveObligationRequest request)
    {
        var (obligation, code, message) = LoadObligation(contractId, obligationId);
        if (code is not null) return (null, code, message);
        if (obligation!.Status is not (ObligationStatus.Open or ObligationStatus.EvidenceSubmitted))
            return (null, "OBLIGATION_STATE_ERROR", $"A {obligation.Status} obligation cannot be waived.");
        if (obligation.CreatedBy == user.Id)
            return (null, "OBLIGATION_APPROVAL_ERROR", "Maker-checker: the obligation creator cannot waive it.");
        if (!user.Has("contracts:approve"))
            return (null, "FORBIDDEN", "contracts:approve permission required.");

        contracts.SetObligationWaived(obligation, request.Reason);
        audit.Log(user, "waive", "contract_obligation", obligationId, $"Obligation '{obligation.ObligationType}' waived by {user.Email}: {request.Reason}");
        contracts.Save();
        return (ProjObligation(obligation), null, null);
    }

    // 039 AC3 (R-T6g): manual exercise, sequential, capped by OptionPeriodsTotal; maker-checked
    // (approver ≠ contract creator); extends EffectiveTo by the current term length and records
    // the decision in the audit log
    public (object? data, string? code, string? message) ExerciseRenewal(CurrentUser user, string contractId, ExerciseRenewalRequest request)
    {
        var contract = contracts.FindContract(contractId);
        if (contract is null) return (null, "CONTRACT_NOT_FOUND", "Contract not found.");
        if (contract.Status != ContractStatus.Active)
            return (null, "CONTRACT_STATE_ERROR", $"Only active contracts exercise renewal options (current: {contract.Status}).");
        if (contract.CreatedBy == user.Id)
            return (null, "CONTRACT_APPROVAL_ERROR", "Maker-checker: the contract creator cannot exercise its renewal.");
        if (!user.Has("contracts:approve"))
            return (null, "FORBIDDEN", "contracts:approve permission required.");
        if (request.OptionNumber != contract.OptionsExercised + 1 || request.OptionNumber > (contract.OptionPeriodsTotal ?? 0))
            return (null, "CONTRACT_NO_OPTION",
                $"Option {request.OptionNumber} is not available (exercised {contract.OptionsExercised} of {contract.OptionPeriodsTotal ?? 0}).");

        var termDays = contract.EffectiveTo.DayNumber - contract.EffectiveFrom.DayNumber;
        var extended = contract.EffectiveTo.AddDays(termDays);
        contracts.SetContractRenewed(contract, contract.OptionsExercised + 1, extended);
        audit.Log(user, "renew", "contract", contractId,
            $"Renewal option {request.OptionNumber} exercised by {user.Email}; EffectiveTo extended to {extended:yyyy-MM-dd}");
        contracts.Save();
        return (ProjContract(contract, contracts.AmendmentsForContract(contractId).Count,
            contracts.ObligationsForContract(contractId).Count(o => Overdue(o))), null, null);
    }

    // ==================== helpers ====================

    private (ContractObligation? obligation, string? code, string? message) LoadObligation(string contractId, string obligationId)
    {
        var obligation = contracts.FindObligation(contractId, obligationId);
        return obligation is null
            ? ((ContractObligation?)null, "OBLIGATION_NOT_FOUND", "Contract obligation not found.")
            : (obligation, null, null);
    }

    private static bool Overdue(ContractObligation o) =>
        o.Status is ObligationStatus.Open or ObligationStatus.EvidenceSubmitted && o.DueDate < Today;

    private static Func<Contract, bool> ExpiringWithin(int days) => c => c.EffectiveTo <= Today.AddDays(days);

    private static bool Overlaps(ContractPriceLine e, DateOnly from, DateOnly? to) =>
        (e.EffectiveTo is null || from <= e.EffectiveTo) && (to is null || e.EffectiveFrom <= to);

    private static bool LineOverlaps(ContractPriceLineInput o, DateOnly from, DateOnly? to) =>
        (o.EffectiveTo is null || from <= o.EffectiveTo) && (to is null || o.EffectiveFrom <= to);

    private static object ProjContract(Contract c, int amendmentCount, int overdueObligations) => new
    {
        c.Id, c.SupplierId, c.AwardId, c.Status, c.Currency, c.EffectiveFrom, c.EffectiveTo,
        c.RenewalNoticeDays, c.OptionPeriodsTotal, c.OptionsExercised, c.OwnerUserId, c.Version,
        c.ActivatedBy, c.ActivatedAt, c.CreatedBy, c.CreatedAt, amendmentCount, overdueObligations,
        noticeDue = c.RenewalNoticeDays is { } notice && c.EffectiveTo.AddDays(-notice) <= Today,
    };

    private static object ProjAmendment(ContractAmendment a) => new
    {
        a.Id, a.ContractId, a.Version, a.EffectiveDate, a.Summary, a.ApprovedBy, a.ApprovedAt,
    };

    private static object ProjObligation(ContractObligation o) => new
    {
        o.Id, o.ContractId, o.ObligationType, o.OwnerUserId, o.DueDate, o.SourceClause, o.Status,
        o.EvidenceRef, o.VerifiedBy, o.VerifiedAt, o.WaiverReason, o.CreatedBy,
        overdue = Overdue(o),
    };
}
