namespace Erp.Api;

// Domain models — mock-mode in-memory store (04_Data_Model seed lists, trimmed to pilot slice).
// shortcut: no persistence, swaps to EF Core + PostgreSQL when DB is reachable (D-10)

public record Tenant(string Id, string Name);
public record LegalEntity(string Id, string TenantId, string Name, string Jurisdiction, string Currency, string DefaultLocale);

public record Role(string Id, string Name, string[] Permissions);
public record Membership(string UserId, string TenantId, string LegalEntityId, string RoleId);
public record User(string Id, string Email, string DisplayName, bool Active);
