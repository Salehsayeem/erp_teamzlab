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
    (EarningDeductionCode? code, string? errorCode, string? message) UpdatePayCode(CurrentUser user, string codeCode, Dtos.UpdatePayCodeRequest request);
    (EarningDeductionCode? code, string? errorCode, string? message) DeactivatePayCode(CurrentUser user, string codeCode);
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

// ---- deferred screens (PM1–PM10). Same tri-result convention: (data, errorCode, message) —
// null code means success. Permissions per brief §3: R-HR makes, R-FIN approves/releases/closes,
// exceptions and releases ride payroll:approve / payroll:release (never weakened).
public interface IPeopleOrgService
{
    object ListOrgUnits(CurrentUser user, DateOnly? asOf);
    (OrgUnit? unit, string? code, string? message) CreateOrgUnit(CurrentUser user, Dtos.CreateOrgUnitRequest request);
    (OrgUnit? unit, string? code, string? message) CreateOrgUnitVersion(CurrentUser user, string unitId, Dtos.CreateOrgUnitVersionRequest request);
    object ListWorkLocations(CurrentUser user);
    (WorkLocation? location, string? code, string? message) CreateWorkLocation(CurrentUser user, Dtos.CreateWorkLocationRequest request);
    object ListPositions(CurrentUser user);
    (Position? position, string? code, string? message) CreatePosition(CurrentUser user, Dtos.CreatePositionRequest request);
    (PositionAssignment? assignment, string? code, string? message) CreateAssignment(CurrentUser user, string positionId, Dtos.CreatePositionAssignmentRequest request);
    (PositionAssignment? assignment, string? code, string? message) DecideAssignmentException(CurrentUser approver, string assignmentId, Dtos.DecideAssignmentExceptionRequest request);
    object ListAssignments(CurrentUser user, string? employmentId);
}

public interface IOnboardingService
{
    object ListTemplates(CurrentUser user);
    (OnboardingTemplate? template, string? code, string? message) CreateTemplate(CurrentUser user, Dtos.CreateOnboardingTemplateRequest request);
    (object? data, string? code, string? message) GenerateForWorker(CurrentUser user, string workerId);
    object TasksFor(CurrentUser user, string workerId);
    (OnboardingTask? task, string? code, string? message) Complete(CurrentUser user, string taskId);
    (OnboardingTask? task, string? code, string? message) Waive(CurrentUser approver, string taskId, string reason);
}

public interface IJobCatalogService
{
    object ListProfiles(CurrentUser user);
    (JobProfile? profile, string? code, string? message) CreateProfile(CurrentUser user, Dtos.CreateJobProfileRequest request);
    (JobProfileVersion? version, string? code, string? message) CreateProfileVersion(CurrentUser user, string profileId, Dtos.CreateJobProfileVersionRequest request);
    object ListGradeRanges(CurrentUser user);
    (GradeRange? range, string? code, string? message) CreateGradeRange(CurrentUser user, Dtos.CreateGradeRangeRequest request);
    (object? data, string? code, string? message) CompPolicyCheck(CurrentUser user, string workerId, Dtos.CompPolicyCheckRequest request);
}

public interface IWorkScheduleService
{
    object ListSchedules(CurrentUser user);
    (WorkSchedule? schedule, string? code, string? message) CreateSchedule(CurrentUser user, Dtos.CreateWorkScheduleRequest request);
    (ScheduleVersion? version, string? code, string? message) CreateScheduleVersion(CurrentUser user, string scheduleId, Dtos.CreateScheduleVersionRequest request);
    (object? data, string? code, string? message) Expand(CurrentUser user, string scheduleId, DateOnly from, DateOnly to);
    object ListHolidayCalendars(CurrentUser user);
    (HolidayCalendar? calendar, string? code, string? message) CreateHolidayCalendar(CurrentUser user, Dtos.CreateHolidayCalendarRequest request);
}

public interface ILeaveService
{
    object ListLeaveTypes(CurrentUser user);
    (LeaveType? type, string? code, string? message) CreateLeaveType(CurrentUser user, Dtos.CreateLeaveTypeRequest request);
    (object? data, string? code, string? message) RunAccrual(CurrentUser user, string leaveTypeId, Dtos.RunAccrualRequest request);
    (object? data, string? code, string? message) Balance(CurrentUser user, string workerId, string? leaveTypeId, DateOnly? asOf);
    (LeaveRequest? request, string? code, string? message) CreateLeaveRequest(CurrentUser user, Dtos.CreateLeaveRequestRequest request);
    object ListLeaveRequests(CurrentUser user, string? employmentId, LeaveRequestStatus? status);
    (LeaveRequest? request, string? code, string? message) DecideLeaveRequest(CurrentUser approver, string requestId, Dtos.DecideLeaveRequestRequest request);
    (LeaveRequest? request, string? code, string? message) Withdraw(CurrentUser user, string requestId, string reason);
    (LeaveAdjustment? adjustment, string? code, string? message) CreateLeaveAdjustment(CurrentUser user, Dtos.CreateLeaveAdjustmentRequest request);
    object ListLeaveAdjustments(CurrentUser user, string? employmentId);
    (LeaveAdjustment? adjustment, string? code, string? message) DecideLeaveAdjustment(CurrentUser approver, string adjustmentId, Dtos.DecideLeaveAdjustmentRequest request);
    object ListAttendanceExceptions(CurrentUser user, string? employmentId);
    (object? data, string? code, string? message) GenerateAttendanceExceptions(CurrentUser user, Dtos.GenerateAttendanceExceptionsRequest request);
    (AttendanceException? exception, string? code, string? message) DisposeAttendanceException(CurrentUser approver, string exceptionId, Dtos.DisposeAttendanceExceptionRequest request);
}

public interface IPayrollInputsService
{
    object ListTaxElections(CurrentUser user, string? employmentId);
    (TaxElection? election, string? code, string? message) CreateTaxElection(CurrentUser user, Dtos.CreateTaxElectionRequest request);
    (TaxElection? election, string? code, string? message) VerifyElection(CurrentUser user, string electionId);
    (object? data, string? code, string? message) UpdatePayrollConfig(CurrentUser user, Dtos.UpdatePayrollConfigRequest request);
    object ReadConfig();
    object ListContributionRules(CurrentUser user);
    (ContributionRule? rule, string? code, string? message) CreateContributionRule(CurrentUser user, Dtos.CreateContributionRuleRequest request);
    (object? data, string? code, string? message) ReconcileContribution(CurrentUser user, string ruleId, string runId);
    object ListGarnishments(CurrentUser user, string? employmentId);
    (GarnishmentOrder? order, string? code, string? message) CreateGarnishment(CurrentUser user, Dtos.CreateGarnishmentRequest request);
    (GarnishmentOrder? order, string? code, string? message) ReleaseGarnishment(CurrentUser releaser, string orderId, Dtos.ReleaseGarnishmentRequest request);
    object ListBenefitPlans(CurrentUser user);
    (BenefitPlan? plan, string? code, string? message) CreateBenefitPlan(CurrentUser user, Dtos.CreateBenefitPlanRequest request);
    (BenefitEnrollment? enrollment, string? code, string? message) CreateEnrollment(CurrentUser user, Dtos.CreateEnrollmentRequest request);
    (object? data, string? code, string? message) EnrollmentDetail(CurrentUser user, string enrollmentId);
    (object? data, string? code, string? message) PostProviderFeed(CurrentUser user, string enrollmentId, Dtos.PostProviderFeedRequest request);
    (object? data, string? code, string? message) ReconcileEnrollment(CurrentUser user, string enrollmentId, string period);
}

public interface IPaymentFileService
{
    object ListFiles(CurrentUser user, PaymentFileStatus? status);
    (object? data, string? code, string? message) FileDetail(CurrentUser user, string fileId);
    (object? data, string? code, string? message, bool replayed) CreateForRun(CurrentUser user, string runId, Dtos.CreatePaymentFileRequest request);
    (object? data, string? code, string? message) Release(CurrentUser releaser, string fileId);
    (object? data, string? code, string? message) RejectItems(CurrentUser releaser, string fileId, Dtos.RejectPaymentFileItemsRequest request);
}

public interface IPayrollCloseService
{
    object ListMappings(CurrentUser user);
    (PayrollAccountMapping? mapping, string? code, string? message) CreateMapping(CurrentUser user, Dtos.CreateAccountMappingRequest request);
    (object? data, string? code, string? message) CloseChecklist(CurrentUser user, string periodId);
    (object? data, string? code, string? message, object[]? items) Close(CurrentUser closer, string periodId, Dtos.ClosePayPeriodRequest request);
    (object? data, string? code, string? message) Reopen(CurrentUser closer, string periodId, Dtos.ReopenPayPeriodRequest request);
}

public interface ICredentialService
{
    object List(CurrentUser user, string? employmentId);
    (Credential? credential, string? code, string? message) Create(CurrentUser user, Dtos.CreateCredentialRequest request);
    (Credential? credential, string? code, string? message) Waive(CurrentUser approver, string credentialId, Dtos.WaiveCredentialRequest request);
}

public interface IWorkforceService
{
    object ListPlans(CurrentUser user);
    (object? data, string? code, string? message) GetPlan(CurrentUser user, string planId);
    (WorkforcePlan? plan, string? code, string? message) CreatePlan(CurrentUser user, Dtos.CreateWorkforcePlanRequest request);
    (object? data, string? code, string? message) Promote(CurrentUser user, string planId, Dtos.PromotePlanRequest request);
    object ListAllocationSets(CurrentUser user);
    (object? data, string? code, string? message) GetAllocationSet(CurrentUser user, string setId);
    (AllocationSet? set, string? code, string? message) CreateAllocationSet(CurrentUser user, Dtos.CreateAllocationSetRequest request);
    (object? data, string? code, string? message) ApproveAllocationSet(CurrentUser approver, string setId);
    (object? data, string? code, string? message) ReconcileAllocation(CurrentUser user, string setId, string runId);
    (object? data, string? code, string? message) Analytics(CurrentUser user, string metric, bool csv);
}
