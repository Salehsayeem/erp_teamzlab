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

public sealed record AddCompensationRequest(decimal Amount, string Currency, string PayBasis, DateOnly EffectiveFrom, string Reason);

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
