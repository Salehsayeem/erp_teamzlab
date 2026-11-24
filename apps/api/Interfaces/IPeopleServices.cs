namespace Erp.Api.Interfaces;

// PEOPLE deep services (worker register, time capture, payroll runs). Same tri-result convention
// as the other slices: (data, errorCode, message) — null code means success.
public interface IWorkerService
{
    object List(CurrentUser user, EmploymentStatus? status, string? search);
    (Employment? worker, string? code, string? message) Create(CurrentUser user, Dtos.CreateWorkerRequest request);
    (Employment? worker, string? code, string? message) Transition(CurrentUser user, string workerId, Dtos.TransitionEmploymentRequest request);
    (CompensationEvent? evt, string? code, string? message) AddCompensation(CurrentUser user, string workerId, Dtos.AddCompensationRequest request);
    object Compensations(CurrentUser user, string workerId, DateOnly? asOf);
    (WorkerPayGroup? membership, string? code, string? message) AddMembership(CurrentUser user, string workerId, Dtos.AddPayGroupMembershipRequest request);
    (PayGroup? group, string? code, string? message) CreatePayGroup(CurrentUser user, Dtos.CreatePayGroupRequest request);
    object ListPayGroups(CurrentUser user);
    (object? data, string? code, string? message) PayHistory(CurrentUser user, string workerId);
}

public interface ITimeService
{
    object List(CurrentUser user, string? employmentId, TimeEntryStatus? status);
    (TimeEntry? entry, string? code, string? message) Create(CurrentUser user, Dtos.CreateTimeEntryRequest request, string? idemKey);
    (TimeEntry? entry, string? code, string? message) Decide(CurrentUser approver, string entryId, Dtos.DecideTimeEntryRequest request);
    object ListCorrections(CurrentUser user, string? employmentId, TimeCorrectionStatus? status);
    (TimeCorrection? correction, string? code, string? message) CreateCorrection(CurrentUser user, Dtos.CreateTimeCorrectionRequest request);
    (TimeCorrection? correction, string? code, string? message) DecideCorrection(CurrentUser approver, string correctionId, Dtos.DecideTimeCorrectionRequest request);
}

public interface IPayrollService
{
    object ListPayCodes(CurrentUser user, PayCodeKind? kind);
    (EarningDeductionCode? code, string? errorCode, string? message) CreatePayCode(CurrentUser user, Dtos.CreatePayCodeRequest request);
    object ListRuns(CurrentUser user, PayRunStatus? status, string? periodId);
    (PayRun? run, string? code, string? message) CreateRun(CurrentUser user, Dtos.CreatePayRunRequest request);
    (PayRun? run, string? code, string? message) Calculate(CurrentUser user, string runId);
    (object? data, string? code, string? message) Validations(CurrentUser user, string runId);
    (object? data, string? code, string? message) Preview(CurrentUser user, string runId);
    (PayRun? run, string? code, string? message) Approve(CurrentUser approver, string runId, string? reason);
    (object? data, string? code, string? message) Post(CurrentUser approver, string runId);
    (object? data, string? code, string? message) Payslip(CurrentUser user, string runId, string workerId);
    (object? data, string? code, string? message) Reverse(CurrentUser approver, string runId, Dtos.ReversePayRunRequest request);
}
