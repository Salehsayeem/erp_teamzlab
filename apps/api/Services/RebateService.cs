using System.Text.Json;
using Erp.Api.Data;
using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Services;

// Rebates (ERP-SALES-063): versioned agreements, accrual over posted invoices of the period,
// maker-checker settlement (rebates:settle on R-FIN), claims capped at the APPROVED accrual.
// Commission (064) rides the outbox from the invoice posting/reversal paths — no compensation
// engine here (boundary per brief S9).
public sealed class RebateService(
    ICustomerRepository customers, IInvoiceRepository invoices, ISalesDeferredRepository deferred,
    INumberSeries numbers, IAuditRepository audit, EntityContext ec, ErpDbContext db) : IRebateService
{
    public (object? data, string? code, string? message) CreateAgreement(CurrentUser u, string customerId, string basis, decimal ratePct, string accrualPeriod, DateOnly validFrom)
    {
        if (!customers.Exists(customerId)) return (null, "CUSTOMER_NOT_FOUND", "Customer does not exist.");
        if (!u.Has("rebates:write")) return (null, "FORBIDDEN", "rebates:write permission required.");
        if (ratePct is <= 0 or > 100) return (null, "VALIDATION_ERROR", "RatePct must be between 0 and 100.");
        var agreement = new RebateAgreement(numbers.Next("RBT"), ec.TenantId, ec.EntityId, customerId,
            basis, ratePct, accrualPeriod, RebateAgreementStatus.Active, 1, validFrom, null, u.Id, DateTimeOffset.UtcNow);
        deferred.AddAgreement(agreement);
        audit.Log(u, "create", "rebate_agreement", agreement.Id, $"{basis} @ {ratePct:0.##}% for {customerId}");
        deferred.Save();
        return (AgreementView(agreement), null, null);
    }

    public (object? data, string? code, string? message) ListAgreements() =>
        (deferred.Agreements().Select(AgreementView).ToList(), null, null);

    // 063 AC1: the calculation names its eligible transactions and the agreement version
    public (object? data, string? code, string? message) CalculateAccrual(CurrentUser u, string agreementId, string period)
    {
        var agreement = deferred.FindAgreement(agreementId);
        if (agreement is null) return (null, "REBATE_NOT_FOUND", "Rebate agreement not found.");
        if (!u.Has("rebates:write")) return (null, "FORBIDDEN", "rebates:write permission required.");
        var eligible = invoices.AllInvoices(InvoiceStatus.Posted, agreement.CustomerId)
            .Where(i => $"{i.InvoiceDate:yyyy-MM}" == period).ToList();
        var accrued = decimal.Round(
            eligible.Sum(i => CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id))) * agreement.RatePct / 100,
            2, MidpointRounding.AwayFromZero);
        var existing = deferred.FindAccrual(agreementId, period);
        if (existing is not null)
        {
            deferred.SetAccrual(existing, accrued, JsonSerializer.Serialize(eligible.Select(i => i.Id).ToList(), Idem.JsonOptions));
            deferred.Save();
            return (AccrualView(deferred.FindAccrual(agreementId, period)!), null, null);
        }
        var accrual = new RebateAccrual($"{agreementId}:{period}", ec.TenantId, ec.EntityId, agreementId, period,
            accrued, null, JsonSerializer.Serialize(eligible.Select(i => i.Id).ToList(), Idem.JsonOptions),
            agreement.Version, RebateAccrualStatus.Calculated, u.Id, DateTimeOffset.UtcNow);
        deferred.AddAccrual(accrual);
        audit.Log(u, "calculate", "rebate_accrual", accrual.Id, $"{eligible.Count} txn(s), {accrued:0.00} accrued");
        deferred.Save();
        return (AccrualView(accrual), null, null);
    }

    // settlement cap = the APPROVED accrual — approval is the rebates:settle act (R-FIN)
    public (object? data, string? code, string? message) ApproveAccrual(CurrentUser u, string agreementId, string period)
    {
        var accrual = deferred.FindAccrual(agreementId, period);
        if (accrual is null) return (null, "REBATE_NOT_FOUND", "Rebate accrual not found — calculate it first.");
        if (!u.Has("rebates:settle")) return (null, "FORBIDDEN", "rebates:settle permission required (R-FIN).");
        if (accrual.Status != RebateAccrualStatus.Calculated)
            return (null, "REBATE_STATE_ERROR", $"Only calculated accruals can be approved (current: {accrual.Status}).");
        if (accrual.CalculatedBy == u.Id)
            return (null, "REBATE_STATE_ERROR", "Maker-checker: the calculator cannot approve their own accrual.");
        deferred.SetAccrualApproved(accrual, u.Id);
        audit.Log(u, "approve", "rebate_accrual", accrual.Id, $"Accrual {accrual.AccruedAmount:0.00} approved");
        deferred.Save();
        return (AccrualView(deferred.FindAccrual(agreementId, period)!), null, null);
    }

    // 063 AC2: settlement above the approved accrued amount → 409 absent a documented adjustment
    public (object? data, string? code, string? message) CreateClaim(CurrentUser u, string agreementId, string period, decimal amount, IReadOnlyList<string> eligibleTxnIds)
    {
        var agreement = deferred.FindAgreement(agreementId);
        if (agreement is null) return (null, "REBATE_NOT_FOUND", "Rebate agreement not found.");
        if (!u.Has("rebates:write")) return (null, "FORBIDDEN", "rebates:write permission required.");
        var accrual = deferred.FindAccrual(agreementId, period);
        var approvedCap = accrual is { Status: RebateAccrualStatus.Approved } a ? a.ApprovedAmount ?? a.AccruedAmount : 0;
        // 063 AC2: the cap is cumulative — pending + settled claims reserve against one accrual
        var claims = deferred.ClaimsFor(agreementId, period);
        var committed = claims.Where(c => c.Status is RebateClaimStatus.Submitted or RebateClaimStatus.Approved)
            .Sum(c => c.Amount);
        if (amount + committed > approvedCap)
            return (null, "REBATE_EXCEEDS_ACCRUAL",
                $"Claim {amount:0.00} would put claims at {committed + amount:0.00} against the approved accrued amount {approvedCap:0.00} — settle less or attach a documented adjustment.");
        var claim = new RebateClaim($"{agreementId}:{period}:C{claims.Count + 1}", ec.TenantId, ec.EntityId,
            agreementId, period, amount, agreement.Version,
            JsonSerializer.Serialize(eligibleTxnIds, Idem.JsonOptions), RebateClaimStatus.Submitted,
            u.Id, DateTimeOffset.UtcNow);
        deferred.AddClaim(claim);
        audit.Log(u, "claim", "rebate_claim", claim.Id, $"Claim {amount:0.00} of approved {approvedCap:0.00}");
        deferred.Save();
        return (ClaimView(claim), null, null);
    }

    // read-back with agreement/accrual context; a claim sitting over a cap that shrank after
    // its approval surfaces as overCap — existing claims are never rewritten (money path)
    public (object? data, string? code, string? message) ListClaims()
    {
        var committed = new Dictionary<string, decimal>();
        var rows = deferred.Claims().Select(c =>
        {
            var agreement = deferred.FindAgreement(c.AgreementId);
            var accrual = deferred.FindAccrual(c.AgreementId, c.Period);
            var cap = accrual is { Status: RebateAccrualStatus.Approved } a ? a.ApprovedAmount ?? a.AccruedAmount : 0;
            var key = $"{c.AgreementId}|{c.Period}";
            var over = false;
            if (c.Status is RebateClaimStatus.Submitted or RebateClaimStatus.Approved)
            {
                committed[key] = committed.GetValueOrDefault(key) + c.Amount;
                over = committed[key] > cap; // cumulative, same guard as CreateClaim
            }
            return ClaimRow(c, agreement, cap, over);
        }).ToList();
        return (rows, null, null);
    }

    // rejection is its own decision (PO pattern): reason required, maker-checked, submitted-only
    public (object? data, string? code, string? message) RejectClaim(CurrentUser u, string claimId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "DECISION_REASON_REQUIRED", "A rejection reason is required.");
        var claim = deferred.FindClaim(claimId);
        if (claim is null) return (null, "REBATE_NOT_FOUND", "Rebate claim not found.");
        if (!u.Has("rebates:settle")) return (null, "FORBIDDEN", "rebates:settle permission required (R-FIN).");
        if (claim.Status != RebateClaimStatus.Submitted)
            return (null, "REBATE_STATE_ERROR", $"Only submitted claims can be rejected (current: {claim.Status}).");
        if (claim.RequestedBy == u.Id)
            return (null, "REBATE_STATE_ERROR", "Maker-checker: the claimant cannot reject their own claim.");
        deferred.SetClaimStatus(claim, RebateClaimStatus.Rejected, u.Id, reason);
        audit.Log(u, "reject", "rebate_claim", claimId, $"Claim {claim.Amount:0.00} rejected: {reason}");
        audit.Emit(u, "sales.rebate.claim.rejected", new { claimId, amount = claim.Amount, rejectedBy = u.Id, reason });
        deferred.Save();
        return (ClaimView(deferred.FindClaim(claimId)!), null, null);
    }

    public (object? data, string? code, string? message) ApproveClaim(CurrentUser u, string claimId, string? reason)
    {
        var claim = deferred.FindClaim(claimId);
        if (claim is null) return (null, "REBATE_NOT_FOUND", "Rebate claim not found.");
        if (!u.Has("rebates:settle")) return (null, "FORBIDDEN", "rebates:settle permission required (R-FIN).");
        if (claim.RequestedBy == u.Id)
            return (null, "REBATE_STATE_ERROR", "Maker-checker: the claimant cannot settle their own claim.");
        deferred.SetClaimStatus(claim, RebateClaimStatus.Approved, u.Id);
        audit.Log(u, "settle", "rebate_claim", claimId, $"Claim {claim.Amount:0.00} settled: {reason ?? "no reason given"}");
        audit.Emit(u, "sales.rebate.settled", new { claimId, amount = claim.Amount, settledBy = u.Id });
        deferred.Save();
        return (ClaimView(deferred.FindClaim(claimId)!), null, null);
    }

    // retro change edge: a pure read — preview the delta a new rate would produce before any
    // approved adjustment posts
    public (object? data, string? code, string? message) RecalculationPreview(string agreementId, string period, decimal newRatePct)
    {
        var agreement = deferred.FindAgreement(agreementId);
        if (agreement is null) return (null, "REBATE_NOT_FOUND", "Rebate agreement not found.");
        var eligible = invoices.AllInvoices(InvoiceStatus.Posted, agreement.CustomerId)
            .Where(i => $"{i.InvoiceDate:yyyy-MM}" == period).ToList();
        var openBase = eligible.Sum(i => CreditMath.InvoiceOpen(i, invoices.AdjustmentsForInvoice(i.Id)));
        return (new
        {
            agreementId, period, agreementVersion = agreement.Version, currentRatePct = agreement.RatePct,
            newRatePct, openBase = decimal.Round(openBase, 2, MidpointRounding.AwayFromZero),
            currentAccrual = decimal.Round(openBase * agreement.RatePct / 100, 2, MidpointRounding.AwayFromZero),
            projectedAccrual = decimal.Round(openBase * newRatePct / 100, 2, MidpointRounding.AwayFromZero),
            note = "Preview only — a retroactive change requires an approved adjustment before any posting.",
        }, null, null);
    }

    private static object AgreementView(RebateAgreement a) => new
    {
        a.Id, a.CustomerId, a.Basis, a.RatePct, a.AccrualPeriod, a.Status, a.Version, a.ValidFrom, a.ValidTo,
        a.CreatedBy, a.CreatedAt,
    };

    private static object AccrualView(RebateAccrual a) => new
    {
        a.Id, a.AgreementId, a.Period, a.AccruedAmount, a.ApprovedAmount,
        eligibleTxnIds = JsonDocument.Parse(a.EligibleTxnJson).RootElement.Clone(),
        a.AgreementVersion, a.Status, a.CalculatedBy, a.CalculatedAt, a.ApprovedBy, a.ApprovedAt,
    };

    private static object ClaimView(RebateClaim c) => new
    {
        c.Id, c.AgreementId, c.Period, c.Amount, c.AgreementVersion,
        eligibleTxnIds = JsonDocument.Parse(c.EligibleTxnJson).RootElement.Clone(),
        c.Status, requestedBy = c.RequestedBy, c.RequestedAt, c.ApprovedBy, c.ApprovedAt, c.RejectionReason,
    };

    private static object ClaimRow(RebateClaim c, RebateAgreement? agreement, decimal cap, bool overCap) => new
    {
        c.Id, c.AgreementId, c.Period, c.Amount, c.AgreementVersion,
        eligibleTxnIds = JsonDocument.Parse(c.EligibleTxnJson).RootElement.Clone(),
        c.Status, requestedBy = c.RequestedBy, c.RequestedAt, c.ApprovedBy, c.ApprovedAt, c.RejectionReason,
        customerId = agreement?.CustomerId, basis = agreement?.Basis, ratePct = agreement?.RatePct,
        approvedCap = cap, overCap,
    };
}
