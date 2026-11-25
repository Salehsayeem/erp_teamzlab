namespace Erp.Api;

public static class ErrorStatus
{
    // finding 5: status derives from the error code (400 body/qty validation, 404 missing record, 403 permission, 409 state conflict)
    public static int StatusFor(string code) => code switch
    {
        "VALIDATION_ERROR" => 400,
        "FORBIDDEN" => 403,
        // brief-pinned 400s: price-book interval overlap (024 AC2), customer hierarchy
        // cycle/self-parent (012 AC1) — request-shaped problems, not state conflicts
        "PRICE_OVERLAP" => 400,
        "CUSTOMER_HIERARCHY_CYCLE" => 400,
        _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => 404,
        _ => 409,
    };
}
