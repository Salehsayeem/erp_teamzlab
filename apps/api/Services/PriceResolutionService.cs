using Erp.Api.Data;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// 024/026: effective-dated price resolution with explicit rule/precedence evidence. Order:
// customer contract prices (highest minQty, then latest effectiveFrom) → book entries
// (lowest book Precedence, then highest entry minQty). No match → controlled PRICE_NOT_FOUND
// (024 edge: never a silent zero).
public sealed class PriceResolutionService(IPriceBookRepository priceBooks) : IPriceResolutionService
{
    public (PriceResolution? resolution, string? code, string? message) Resolve(
        string? customerId, string itemSku, decimal qty, DateOnly onDate)
    {
        if (customerId is not null)
        {
            var contract = priceBooks.ContractPricesFor(customerId, itemSku).FirstOrDefault(p =>
                p.MinQty <= qty && p.EffectiveFrom <= onDate && (p.EffectiveTo is null || p.EffectiveTo >= onDate));
            if (contract is not null)
                return (new PriceResolution(contract.UnitPrice, null, "contract",
                    contract.EffectiveFrom, contract.EffectiveTo, contract.UnitPrice, contract.Id), null, null);
        }

        var bookHit = priceBooks.Books()
            .SelectMany(b => b.Entries.Select(e => (Book: b, Entry: e)))
            .Where(x => x.Entry.ItemSku == itemSku && x.Entry.MinQty <= qty
                && x.Entry.EffectiveFrom <= onDate && (x.Entry.EffectiveTo is null || x.Entry.EffectiveTo >= onDate))
            .OrderBy(x => x.Book.Precedence).ThenByDescending(x => x.Entry.MinQty)
            .ThenBy(x => x.Book.Id)
            .Select(x => new { x.Book, x.Entry })
            .FirstOrDefault();
        if (bookHit is not null)
            return (new PriceResolution(bookHit.Entry.UnitPrice, bookHit.Book.Id,
                    bookHit.Entry.Rule ?? "book", bookHit.Entry.EffectiveFrom, bookHit.Entry.EffectiveTo,
                    bookHit.Entry.UnitPrice, $"{bookHit.Book.Id}:{bookHit.Entry.ItemSku}:{bookHit.Entry.MinQty}"),
                null, null);

        return (null, "PRICE_NOT_FOUND",
            $"No active price found for {itemSku} (qty {qty}) — configure a price book entry or contract price; prices never default to zero.");
    }
}
