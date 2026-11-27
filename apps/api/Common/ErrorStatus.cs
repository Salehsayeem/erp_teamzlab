namespace Erp.Api;

public static class ErrorStatus
{
    // finding 5: status derives from the error code (400 body/qty validation, 404 missing record, 403 permission, 409 state conflict)
    public static int StatusFor(string code) => code switch
    {
        "VALIDATION_ERROR" => 400,
        // deferred PROCUREMENT wave — brief-named 400s + validation-shaped codes
        "CATEGORY_CYCLE" or "CATEGORY_DEPRECATED" or "QTY_PRECISION" or "UOM_CONVERSION_INVALID"
            or "DECISION_REASON_REQUIRED" or "EXCEPTION_REASON_REQUIRED" or "DELEGATION_INVALID"
            or "CLAIM_OUTCOME_REQUIRED" or "REVIEW_OUTCOME_REQUIRED" => 400,
        // deferred SALES wave — brief-pinned 400s: price-book interval overlap (024 AC2),
        // customer hierarchy cycle/self-parent (012 AC1) — request-shaped, not state conflicts
        "PRICE_OVERLAP" or "CUSTOMER_HIERARCHY_CYCLE" => 400,
        // fix round: missing reference data is a 404, not a state conflict (FX_RATE_AMBIGUOUS stays 409)
        "FX_RATE_MISSING" => 404,
        // FINANCE-deep P1 — brief-pinned 400s: calendar coverage validation (004 AC2) and the
        // 062 evidence/N-A reason gates are request-shaped, not state conflicts
        "CALENDAR_OVERLAP" or "CALENDAR_GAP" or "CLOSE_TASK_EVIDENCE_REQUIRED" or "CLOSE_TASK_REASON_REQUIRED" => 400,
        "FORBIDDEN" or "DELEGATION_SCOPE_DENIED" or "DISPOSITION_SOD_ERROR" => 403,
        _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => 404,
        _ => 409,
    };
}
