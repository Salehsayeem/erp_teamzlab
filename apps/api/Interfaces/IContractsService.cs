using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// T6 contracts: from approved awards, checker-activated, versioned amendments with price-schedule
// overlap protection, obligations with evidence → verification/waiver, manual renewal exercise
public interface IContractsService
{
    object List(ContractStatus? status, int? expiringWithinDays);
    (object? data, string? code, string? message) Create(CurrentUser user, CreateContractRequest request);
    (object? data, string? code, string? message) Activate(CurrentUser user, string contractId);
    (object? data, string? code, string? message) AddAmendment(CurrentUser user, string contractId, AddAmendmentRequest request);
    (object? data, string? code, string? message) AddObligation(CurrentUser user, string contractId, AddObligationRequest request);
    (object? data, string? code, string? message) SubmitEvidence(CurrentUser user, string contractId, string obligationId, SubmitEvidenceRequest request);
    (object? data, string? code, string? message) VerifyObligation(CurrentUser user, string contractId, string obligationId);
    (object? data, string? code, string? message) WaiveObligation(CurrentUser user, string contractId, string obligationId, WaiveObligationRequest request);
    (object? data, string? code, string? message) ExerciseRenewal(CurrentUser user, string contractId, ExerciseRenewalRequest request);
}
