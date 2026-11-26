namespace Erp.Api.Dtos;

// PEOPLE deep request DTOs (validated Guards-style in the controllers BEFORE the service runs).
// R-PEO3: one-step register — CreateWorkerRequest carries the person identity fields; there is
// no personId. R-PEO5: pay runs carry the FiscalPeriod id + the PayDate the period guard (R-PEO11)
// and run anchor both evaluate. ReversePayRunRequest.FundsReleased is nullable so a missing
// boolean is distinguishable from an explicit false (055 AC2 requires it to be present).
public sealed record CreateWorkerRequest(
    string FullName, string? PreferredName, string? NationalId, string? Email, string? Phone,
    string CategoryCode, DateOnly HireDate, decimal WeeklyPlannedHours,
    string? LocationId, string? ManagerUserId);

public sealed record TransitionEmploymentRequest(EmploymentStatus Status, DateOnly EffectiveDate, string? Reason);

// ExceptionApprovedBy (023 AC2): set only when a grade-policy exception was approved by an
// R-FIN officer (payroll:approve holder) — the service records it on the event's reason line.
public sealed record AddCompensationRequest(decimal Amount, string Currency, string PayBasis, DateOnly EffectiveFrom, string Reason, string? ExceptionApprovedBy = null);

public sealed record CreateTimeEntryRequest(string EmploymentId, DateOnly WorkDate, decimal Hours, decimal OvertimeHours, string? Note);

public sealed record DecideTimeEntryRequest(bool Approve, string? DecisionReason);

public sealed record CreateTimeCorrectionRequest(string TimeEntryId, decimal ProposedHours, decimal ProposedOvertimeHours, string Reason);

public sealed record DecideTimeCorrectionRequest(bool Approve, string? DecisionReason);

public sealed record CreatePayCodeRequest(string Code, PayCodeKind Kind, string Name, string CalcBasis,
    decimal? RatePct, string DebitAccount, string CreditAccount);

// fix R4-6: partial edit - null leaves the field unchanged; mapping/calc changes bump the version
public sealed record UpdatePayCodeRequest(string? Name, decimal? RatePct, string? CalcBasis);

public sealed record AddPayGroupMembershipRequest(string PayGroupId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record CreatePayGroupRequest(string Code, string Name, string Frequency);

public sealed record CreatePayRunRequest(string PayGroupId, string PeriodId, DateOnly PayDate);

public sealed record CalculatePayRunRequest();

public sealed record ApprovePayRunRequest(string? Reason);

public sealed record PostPayRunRequest();

public sealed record ReversePayRunRequest(string Reason, bool? FundsReleased);

// ---- deferred screens wave (PM1–PM10) ------------------------------------------------

// PM1
public sealed record CreateOrgUnitRequest(string Code, string Name, string? ParentOrgUnitId, string? LocationId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record CreateOrgUnitVersionRequest(string Name, string? ParentOrgUnitId, string? LocationId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record CreateWorkLocationRequest(string Code, string Name, string Timezone, string? Address);
public sealed record CreatePositionRequest(string Code, string Title, string? OrgUnitId, string? GradeCode,
    string? RequiredCredentialCode, decimal BudgetedFte, int BudgetedHeadcount);
public sealed record CreatePositionAssignmentRequest(string EmploymentId, decimal Fte, string? CostCenter,
    string? ManagerUserId, string? LocationId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ExceptionReason);
public sealed record DecideAssignmentExceptionRequest(bool Approve, string Reason);

// PM2
public sealed record CreateOnboardingTemplateRequest(string CategoryCode, string TaskKey, string Name,
    int DueOffsetDays, string OwnerRole, bool Required);
public sealed record GenerateOnboardingTasksRequest();
public sealed record DecideOnboardingTaskRequest(string Reason);

// PM3
public sealed record CreateJobProfileRequest(string Code, string Name, string? Department, string Title,
    DateOnly EffectiveFrom, IReadOnlyList<CompetencyItem>? Competencies);
public sealed record CreateJobProfileVersionRequest(string Title, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    IReadOnlyList<CompetencyItem>? Competencies);
public sealed record CompetencyItem(string Name, string Level, bool Required);
public sealed record CreateGradeRangeRequest(string GradeCode, string Currency, string Frequency,
    decimal MinAmount, decimal MidAmount, decimal MaxAmount, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record CompPolicyCheckRequest(decimal Amount, DateOnly EffectiveFrom);

// PM4
public sealed record CreateWorkScheduleRequest(string Code, string Name, string Timezone, string StartTimeLocal);
public sealed record CreateScheduleVersionRequest(decimal WeeklyPlannedHours, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? HolidayCalendarId);
public sealed record CreateHolidayCalendarRequest(string Code, string Name, IReadOnlyList<HolidayItem> Dates);
public sealed record HolidayItem(string Name, DateOnly Date);

// PM5
public sealed record CreateLeaveTypeRequest(string Code, string Name, string Units, string AccrualMethod,
    decimal? AccrualRatePerMonth, decimal? AnnualEntitlementDays, decimal? CarryoverLimitDays,
    bool RequiresApproval, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record RunAccrualRequest(string Period, DateOnly AsOf, string? EmploymentId);
public sealed record CreateLeaveRequestRequest(string EmploymentId, string LeaveTypeId, DateOnly FromDate,
    DateOnly ToDate, decimal Days, string Reason);
public sealed record DecideLeaveRequestRequest(bool Approve, string? DecisionReason);
public sealed record CreateLeaveAdjustmentRequest(string EmploymentId, string LeaveTypeId, decimal Qty, string Reason);
public sealed record DecideLeaveAdjustmentRequest(bool Approve, string? DecisionReason);
public sealed record GenerateAttendanceExceptionsRequest(DateOnly From, DateOnly To);
public sealed record DisposeAttendanceExceptionRequest(string Disposition, string Reason);

// PM6
public sealed record CreateTaxElectionRequest(string EmploymentId, string Kind, string? Value,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record VerifyElectionRequest();
public sealed record UpdatePayrollConfigRequest(bool? MandatoryElection, string? ElectionKind);
public sealed record CreateContributionRuleRequest(string Code, string Name, string Party, decimal RatePct,
    decimal? ThresholdAmount, decimal? CeilingAmount, string PayCodeId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record CreateGarnishmentRequest(string EmploymentId, string Kind, int Priority,
    decimal AmountPerPeriod, decimal? ProtectedMinimum, string CourtRef, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record ReleaseGarnishmentRequest(string Evidence);
public sealed record CreateBenefitPlanRequest(string Code, string Name, string? ProviderCode, decimal EmployeePct,
    string PayCodeId);
public sealed record CreateEnrollmentRequest(string EmploymentId, string PlanId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
public sealed record PostProviderFeedRequest(string Period, decimal Amount, string SourceEventId);

// PM7
public sealed record CreatePaymentFileRequest(string Provider);
public sealed record ReleasePaymentFileRequest();
public sealed record RejectPaymentFileItemsRequest(IReadOnlyList<PaymentItemReject> Items);
public sealed record PaymentItemReject(string EmploymentId, string Reason);

// PM8 — Active defaults to true; an explicitly INACTIVE mapping covering a pay date BLOCKS
// posting and names the element (053 AC2), so the flag must be settable at creation.
public sealed record CreateAccountMappingRequest(string Element, string AccountCode, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, bool? Active = null);
public sealed record ClosePayPeriodRequest(bool? AcknowledgeLater, IReadOnlyList<CloseWaiver>? Waivers);
public sealed record CloseWaiver(string Code, string Reason);
public sealed record ReopenPayPeriodRequest(string Reason);

// PM9
public sealed record CreateCredentialRequest(string EmploymentId, string Code, string Name, string? IssuedBy,
    DateOnly? IssuedOn, DateOnly? ExpiresOn, string? EvidenceRef);
public sealed record WaiveCredentialRequest(string Reason);

// PM10
public sealed record CreateWorkforcePlanRequest(string Code, string Name, DateOnly HorizonStart,
    DateOnly HorizonEnd, string? Assumptions, IReadOnlyList<PlanLineItem> Lines);
public sealed record PlanLineItem(string PositionCode, string Title, decimal Fte, string? OrgUnitId, string? GradeCode);
public sealed record PromotePlanRequest(string? PositionCode);
public sealed record CreateAllocationSetRequest(string Code, string Name, string Unit, IReadOnlyList<AllocationItem> Lines);
public sealed record AllocationItem(string CostCenter, decimal Pct);
public sealed record ApproveAllocationSetRequest();
