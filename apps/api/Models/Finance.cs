namespace Erp.Api;

public enum JournalStatus { Pending, Posted }

public record JournalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);

// Lines as a settable body property for EF (see PurchaseOrder) — same JSON shape, same call sites
public record Journal(string Id, string TenantId, string LegalEntityId, JournalStatus Status, string Memo, DateTimeOffset CreatedAt, string CreatedBy, string? ReversalOfId = null)
{
    public List<JournalLine> Lines { get; set; } = [];
    public Journal(string id, string tenantId, string legalEntityId, JournalStatus status, string memo,
        IReadOnlyList<JournalLine> lines, DateTimeOffset createdAt, string createdBy, string? reversalOfId = null)
        : this(id, tenantId, legalEntityId, status, memo, createdAt, createdBy, reversalOfId)
    { Lines = [.. lines]; }
}
