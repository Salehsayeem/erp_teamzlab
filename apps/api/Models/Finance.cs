namespace Erp.Api;

public enum JournalStatus { Pending, Posted }

public record JournalLine(string AccountCode, string AccountName, decimal Debit, decimal Credit);
public record Journal(string Id, string TenantId, string LegalEntityId, JournalStatus Status, string Memo, IReadOnlyList<JournalLine> Lines, DateTimeOffset CreatedAt, string CreatedBy, string? ReversalOfId = null);
