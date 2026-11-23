namespace Erp.Api;

// Response envelope (05_API_Spec §1–2, CLAUDE.md backend rules) — shapes are frozen by tests
public static class Envelope
{
    public static IResult Ok<T>(T data, int status = 200) =>
        Results.Json(new { success = true, data }, statusCode: status);

    public static IResult Fail(string code, string message, int status = 400) =>
        Results.Json(new { success = false, error = new { code, message } }, statusCode: status);

    // payload forms for Idem.Wrap (serialized once, stored, replayed verbatim)
    public static object OkBody<T>(T data) => new { success = true, data };
    public static object FailBody(string code, string message) => new { success = false, error = new { code, message } };
    public static IResult From(object body, int status) => Results.Json(body, statusCode: status);
}
