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

    // gate rejections (010/013) carry the machine-readable failure list INSIDE the error object:
    // the release gate names its failed prerequisites, the close gate names each unreconciled item
    public static object FailBodyPrerequisites(string code, string message, object[] prerequisites) =>
        new { success = false, error = new { code, message, prerequisites } };

    public static object FailBodyItems(string code, string message, object[] items) =>
        new { success = false, error = new { code, message, items } };

    // failure that also pins a structured record (e.g. an e-invoice submission kept for
    // provenance while transmission is blocked) — the record rides inside the error object
    public static object FailBodyDetail(string code, string message, object detail) =>
        new { success = false, error = new { code, message, detail } };

    public static IResult From(object body, int status) => Results.Json(body, statusCode: status);
}
