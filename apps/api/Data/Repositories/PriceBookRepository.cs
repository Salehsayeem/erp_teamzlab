using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// Price books + customer contract prices (024/026) — entries are owned rows; contract prices
// carry natural keys so re-inserting the identical interval is rejected by the PK
public sealed class PriceBookRepository(ErpDbContext db, EntityContext ec) : IPriceBookRepository
{
    private IQueryable<PriceBook> Scoped() =>
        db.PriceBooks.Where(b => b.TenantId == ec.TenantId && b.LegalEntityId == ec.EntityId);

    public List<PriceBook> Books() => Scoped().OrderBy(b => b.Precedence).ThenBy(b => b.Id).ToList();

    public PriceBook? FindBook(string bookId) => Scoped().FirstOrDefault(b => b.Id == bookId);

    public void AddBook(PriceBook book)
    {
        db.PriceBooks.Add(book);
        StampEntries(book);
    }

    public List<CustomerContractPrice> ContractPricesFor(string customerId, string itemSku) =>
        db.CustomerContractPrices
            .Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId
                && p.CustomerId == customerId && p.ItemSku == itemSku)
            .OrderByDescending(p => p.MinQty).ThenByDescending(p => p.EffectiveFrom)
            .ToList();

    public void AddContractPrice(CustomerContractPrice price) => db.CustomerContractPrices.Add(price);

    public int Save() => db.SaveChanges();

    // owned rows need tenant/entity shadow stamps (same as so_lines)
    private void StampEntries(PriceBook book)
    {
        foreach (var entry in book.Entries)
        {
            db.Entry(entry).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(entry).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
    }
}
