namespace Erp.Api;

public sealed class CurrentUser(string id, string email, string name, string roleId, string[] permissions)
{
    public string Id { get; } = id;
    public string Email { get; } = email;
    public string Name { get; } = name;
    public string RoleId { get; } = roleId;
    public string[] Permissions { get; } = permissions;
    public bool Has(string perm) => Permissions.Contains("*") || Permissions.Contains(perm);

    // AuthMiddleware stores the verified principal in ctx.Items["user"] — controllers read it back
    public static CurrentUser From(HttpContext ctx) => (CurrentUser)ctx.Items["user"]!;
}
