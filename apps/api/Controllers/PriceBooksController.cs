using Erp.Api.Data;
using Erp.Api.Dtos;
using Erp.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

// Price books + customer contract prices (ERP-SALES-024/025/026): effective-dated entries with
// explicit rule/precedence evidence. Resolution itself runs inside quote line creation
// (PriceResolutionService); this surface manages the master data.
[ApiController]
[Route("api/v1")]
public sealed class PriceBooksController(
    IPriceBookRepository priceBooks, INumberSeries numbers, IAuditRepository audit,
    EntityContext ec, ErpDbContext db) : ControllerBase
{
    [HttpGet("price-books")]
    public IResult List()
    {
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("pricebooks:write")) return Envelope.Fail("FORBIDDEN", "pricebooks:write permission required.", 403);
        return Envelope.Ok<object>(priceBooks.Books().Select(b => new
        {
            b.Id, b.Name, b.Currency, b.Precedence, b.CreatedBy, b.CreatedAt,
            entries = b.Entries.Select(e => new
            {
                e.ItemSku, e.MinQty, e.UnitPrice, e.EffectiveFrom, e.EffectiveTo, e.Rule,
            }),
        }));
    }

    [HttpPost("price-books")]
    public IResult CreateBook(CreatePriceBookRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.Name, "Name") ?? Guards.Check(request.Currency, "Currency", 3)
            ?? (request.Precedence < 1 ? "Precedence must be >= 1 (lower wins)." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("pricebooks:write")) return (Envelope.FailBody("FORBIDDEN", "pricebooks:write permission required."), 403);
        var book = new PriceBook(numbers.Next("PBL"), ec.TenantId, ec.EntityId, request.Name, request.Currency,
            request.Precedence, u.Id, DateTimeOffset.UtcNow);
        priceBooks.AddBook(book);
        audit.Log(u, "create", "price_book", book.Id, $"Price book '{request.Name}' (precedence {request.Precedence})");
        priceBooks.Save();
        return (Envelope.OkBody(book), 201);
    });

    [HttpPost("price-books/{id}/entries")]
    public IResult AddEntry(string id, CreateBookEntryRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.ItemSku, "ItemSku", 100) ?? Guards.CheckPositive(request.UnitPrice, "UnitPrice")
            ?? (request.MinQty < 0 ? "MinQty must be >= 0." : null)
            ?? (request.EffectiveTo is { } to && to < request.EffectiveFrom ? "EffectiveTo must not be before EffectiveFrom." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("pricebooks:write")) return (Envelope.FailBody("FORBIDDEN", "pricebooks:write permission required."), 403);
        var book = priceBooks.FindBook(id);
        if (book is null) return (Envelope.FailBody("PRICE_BOOK_NOT_FOUND", "Price book not found."), 404);

        // 024 AC2: overlapping active entries for the same (sku, minQty) tier are rejected —
        // cross-tier or cross-book overlaps resolve by precedence at read time instead
        var overlap = book.Entries.Any(e => e.ItemSku == request.ItemSku && e.MinQty == request.MinQty
            && request.EffectiveFrom <= (e.EffectiveTo ?? DateOnly.MaxValue)
            && (request.EffectiveTo ?? DateOnly.MaxValue) >= e.EffectiveFrom);
        if (overlap) return (Envelope.FailBody("PRICE_OVERLAP",
            $"An active entry for {request.ItemSku} (minQty {request.MinQty}) already covers this interval."), 400);

        book.Entries.Add(new PriceBookEntry(request.ItemSku, request.MinQty, request.UnitPrice,
            request.EffectiveFrom, request.EffectiveTo, request.Rule));
        foreach (var entry in book.Entries)
        {
            db.Entry(entry).Property("TenantId").CurrentValue = ec.TenantId;
            db.Entry(entry).Property("LegalEntityId").CurrentValue = ec.EntityId;
        }
        audit.Log(u, "add_entry", "price_book", book.Id,
            $"Entry {request.ItemSku} @ {request.UnitPrice:0.00} from {request.EffectiveFrom:yyyy-MM-dd}");
        priceBooks.Save();
        return (Envelope.OkBody(book), 201);
    });

    [HttpPost("customer-contract-prices")]
    public IResult AddContractPrice(CreateContractPriceRequest request) => Idem.Wrap(HttpContext, request, () =>
    {
        var err = Guards.Check(request.CustomerId, "CustomerId") ?? Guards.Check(request.ItemSku, "ItemSku", 100)
            ?? Guards.CheckPositive(request.UnitPrice, "UnitPrice")
            ?? (request.EffectiveTo is { } to && to < request.EffectiveFrom ? "EffectiveTo must not be before EffectiveFrom." : null);
        if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
        var u = CurrentUser.From(HttpContext);
        if (!u.Has("pricebooks:write")) return (Envelope.FailBody("FORBIDDEN", "pricebooks:write permission required."), 403);

        // 024 AC2 analog on the contract surface: same (customer, sku, minQty) interval overlap rejected
        var overlap = priceBooks.ContractPricesFor(request.CustomerId, request.ItemSku).Any(p => p.MinQty == request.MinQty
            && request.EffectiveFrom <= (p.EffectiveTo ?? DateOnly.MaxValue)
            && (request.EffectiveTo ?? DateOnly.MaxValue) >= p.EffectiveFrom);
        if (overlap) return (Envelope.FailBody("PRICE_OVERLAP",
            $"A contract price for {request.ItemSku} (minQty {request.MinQty}) already covers this interval."), 400);

        var price = new CustomerContractPrice(
            $"{request.CustomerId}:{request.ItemSku}:{request.EffectiveFrom:yyyyMMdd}:{request.MinQty:0.##}",
            ec.TenantId, ec.EntityId, request.CustomerId, request.ItemSku, request.MinQty, request.UnitPrice,
            request.EffectiveFrom, request.EffectiveTo, u.Id, DateTimeOffset.UtcNow);
        priceBooks.AddContractPrice(price);
        audit.Log(u, "create", "contract_price", price.Id, $"Contract price {request.ItemSku} @ {request.UnitPrice:0.00}");
        priceBooks.Save();
        return (Envelope.OkBody(price), 201);
    });
}
