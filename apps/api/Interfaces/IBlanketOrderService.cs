using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P3 blanket orders (054): ceiling-debited releases under a FOR UPDATE row lock — concurrent
// releases serialize and the loser conflicts; expired parents block new releases.
public interface IBlanketOrderService
{
    (BlanketOrder? Order, string? Code, string? Message) Create(CurrentUser u, string? supplierId, string? currency, decimal ceilingAmount, DateOnly validFrom, DateOnly validTo);
    (BlanketRelease? Release, string? Code, string? Message) Release(CurrentUser u, string blanketOrderId, decimal amount, string? poId);
    object? Detail(string id);
}
