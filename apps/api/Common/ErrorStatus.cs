namespace Erp.Api;

public static class ErrorStatus
{
    // finding 5: status derives from the error code (400 body/qty validation, 404 missing record, 403 permission, 409 state conflict)
    public static int StatusFor(string code) => code switch
    {
        "VALIDATION_ERROR" => 400,
        "FORBIDDEN" => 403,
        _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => 404,
        _ => 409,
    };
}
