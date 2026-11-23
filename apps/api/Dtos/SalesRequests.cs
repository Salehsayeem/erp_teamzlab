namespace Erp.Api.Dtos;

using Erp.Api;

public record CreateCustomerRequest(string Name, string TaxId, string Email);
public record CreateSoRequest(string CustomerId, IReadOnlyList<SoLine> Lines);
public record ApproveSoRequest(string? Reason);
public record ShipRequest(string ItemSku, decimal Qty);
