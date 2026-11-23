using System.Text.Json.Serialization;
using Erp.Api;
using Erp.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(Db.I);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<AuthMiddleware>();

var api = app.MapGroup("/api/v1");
CurrentUser User(HttpContext ctx) => (CurrentUser)ctx.Items["user"]!;

// auth (stub — D-13 will replace with IdP)
api.MapPost("/auth/token-exchange", (TokenExchangeReq req) =>
{
    var e = Guards.CheckEmail(req.Email);
    if (e is not null) return Envelope.Fail("VALIDATION_ERROR", e);
    var user = Db.I.WithLock(() => Db.I.Users.FirstOrDefault(u => u.Email == req.Email && u.Active));
    return user is null
        ? Envelope.Fail("USER_NOT_FOUND", "No active user with that email.", 404)
        : Envelope.Ok<object>(new { token = user.Id, user.Id, user.Email, user.DisplayName });
});

api.MapGet("/me", (HttpContext ctx) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (role, entity) = db.WithLock(() => (db.Roles.First(r => r.Id == u.RoleId), db.Entity));
    return Envelope.Ok<object>(new
    {
        user = new { u.Id, u.Email, u.Name },
        role = role.Name,
        permissions = u.Permissions,
        tenant = db.Tenant,
        legalEntity = entity,
    });
});

api.MapGet("/entities", (HttpContext ctx) => Envelope.Ok<object>(new[] { Db.I.Entity }));

api.MapGet("/suppliers", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Suppliers.Select(s => new { s.Id, s.Name, s.TaxId, s.Email, s.CreatedAt, s.CreatedBy }).ToList())));

api.MapPost("/suppliers", (HttpContext ctx, CreateSupplierReq req) =>
{
    var db = Db.I;
    var err = Guards.Check(req.Name, "Name") ?? Guards.Check(req.TaxId, "TaxId") ?? Guards.CheckEmail(req.Email);
    if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err);
    var u = User(ctx);
    if (!u.Has("suppliers:write")) return Envelope.Fail("FORBIDDEN", "suppliers:write permission required.", 403);
    var s = db.WithLock(() =>
    {
        var supplier = new Supplier(db.NextDoc("SUP"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        db.Suppliers.Add(supplier);
        Audit.Log(db, u, "create", "supplier", supplier.Id, $"Supplier '{req.Name}' created");
        return supplier;
    });
    return Envelope.Ok<object>(s, 201);
});

api.MapGet("/purchase-orders", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.PurchaseOrders.Select(p => new
    {
        p.Id, p.SupplierId, p.Status, p.Lines, p.CreatedBy, p.CreatedAt, p.ApprovedBy, p.ApprovedAt, p.RejectionReason,
        total = p.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2)),
    }).ToList())));

api.MapPost("/purchase-orders", (HttpContext ctx, CreatePoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("po:write")) return Envelope.Fail("FORBIDDEN", "po:write permission required.", 403);
    var lineErr = Guards.CheckLines(req.Lines);
    if (lineErr is not null) return Envelope.Fail("VALIDATION_ERROR", lineErr);
    if (!db.WithLock(() => db.Suppliers.Any(s => s.Id == req.SupplierId)))
        return Envelope.Fail("SUPPLIER_NOT_FOUND", "Supplier does not exist.", 404);
    var po = db.WithLock(() => { var p = PoService.Create(db, u, req.SupplierId, req.Lines); return p; });
    return Envelope.Ok<object>(po, 201);
});

api.MapPost("/purchase-orders/{id}/submit", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    var updated = db.WithLock(() =>
    {
        var po = db.PurchaseOrders.FirstOrDefault(p => p.Id == id);
        if (po is null || po.Status != PoStatus.Draft) return null;
        db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = po with { Status = PoStatus.PendingApproval };
        Audit.Log(db, u, "submit", "purchase_order", id, "PO submitted for approval");
        return db.PurchaseOrders.First(p => p.Id == id);
    });
    return updated is null ? Envelope.Fail("PO_NOT_SUBMITTABLE", "Only draft POs can be submitted.", 409) : Envelope.Ok<object>(updated);
});

api.MapPost("/purchase-orders/{id}/approvals", (HttpContext ctx, string id, ApprovePoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (po, error) = db.WithLock(() => PoService.Approve(db, u, id, req.Reason));
    return error is not null ? Envelope.Fail("PO_APPROVAL_ERROR", error, 409) : Envelope.Ok<object>(po!);
});

api.MapPost("/receipts", (HttpContext ctx, CreateReceiptReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("receipts:write")) return Envelope.Fail("FORBIDDEN", "receipts:write permission required.", 403);
    var (receipt, journal, error) = db.WithLock(() => ReceiptService.Receive(db, u, req.PurchaseOrderId, req.ItemSku, req.Qty));
    return error is not null ? Envelope.Fail("RECEIPT_ERROR", error, 409) : Envelope.Ok<object>(new { receipt, journal }, 201);
});

api.MapGet("/customers", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Customers.Select(c => new { c.Id, c.Name, c.TaxId, c.Email, c.CreatedAt, c.CreatedBy }).ToList())));

api.MapPost("/customers", (HttpContext ctx, CreateCustomerReq req) =>
{
    var db = Db.I;
    var err = Guards.Check(req.Name, "Name") ?? Guards.Check(req.TaxId, "TaxId") ?? Guards.CheckEmail(req.Email);
    if (err is not null) return Envelope.Fail("VALIDATION_ERROR", err);
    var u = User(ctx);
    if (!u.Has("customers:write")) return Envelope.Fail("FORBIDDEN", "customers:write permission required.", 403);
    var c = db.WithLock(() =>
    {
        var customer = new Customer(db.NextDoc("CUS"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        db.Customers.Add(customer);
        Audit.Log(db, u, "create", "customer", customer.Id, $"Customer '{req.Name}' created");
        return customer;
    });
    return Envelope.Ok<object>(c, 201);
});

api.MapGet("/sales-orders", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.SalesOrders.Select(s => new
    {
        s.Id, s.CustomerId, s.Status, s.Lines, s.CreatedBy, s.CreatedAt, s.ApprovedBy, s.ApprovedAt, s.RejectionReason,
        total = s.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2)),
    }).ToList())));

api.MapPost("/sales-orders", (HttpContext ctx, CreateSoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("so:write")) return Envelope.Fail("FORBIDDEN", "so:write permission required.", 403);
    var lineErr = Guards.CheckLines(req.Lines);
    if (lineErr is not null) return Envelope.Fail("VALIDATION_ERROR", lineErr);
    if (!db.WithLock(() => db.Customers.Any(c => c.Id == req.CustomerId)))
        return Envelope.Fail("CUSTOMER_NOT_FOUND", "Customer does not exist.", 404);
    var so = db.WithLock(() => SalesService.Create(db, u, req.CustomerId, req.Lines));
    return Envelope.Ok<object>(so, 201);
});

api.MapPost("/sales-orders/{id}/submit", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    var updated = db.WithLock(() =>
    {
        var so = db.SalesOrders.FirstOrDefault(s => s.Id == id);
        if (so is null || so.Status != SoStatus.Draft) return null;
        db.SalesOrders[db.SalesOrders.IndexOf(so)] = so with { Status = SoStatus.PendingApproval };
        Audit.Log(db, u, "submit", "sales_order", id, "SO submitted for approval");
        return db.SalesOrders.First(s => s.Id == id);
    });
    return updated is null ? Envelope.Fail("SO_NOT_SUBMITTABLE", "Only draft sales orders can be submitted.", 409) : Envelope.Ok<object>(updated);
});

api.MapPost("/sales-orders/{id}/approvals", (HttpContext ctx, string id, ApproveSoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (so, error) = db.WithLock(() => SalesService.Approve(db, u, id, req.Reason));
    return error is not null ? Envelope.Fail("SO_APPROVAL_ERROR", error, 409) : Envelope.Ok<object>(so!);
});

api.MapPost("/sales-orders/{id}/shipments", (HttpContext ctx, string id, ShipReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("shipments:write")) return Envelope.Fail("FORBIDDEN", "shipments:write permission required.", 403);
    var (shipment, journal, code, error) = db.WithLock(() => SalesService.Ship(db, u, id, req.ItemSku, req.Qty));
    return error is not null ? Envelope.Fail(code!, error, 409) : Envelope.Ok<object>(new { shipment, journal }, 201);
});

api.MapPost("/sales-orders/{id}/invoices", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("so:write")) return Envelope.Fail("FORBIDDEN", "so:write permission required.", 403);
    var (journal, error) = db.WithLock(() => SalesService.Invoice(db, u, id));
    return error is not null ? Envelope.Fail("INVOICE_ERROR", error, 409) : Envelope.Ok<object>(journal!, 201);
});

api.MapGet("/journals", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Journals)));

api.MapPost("/journals/{id}/reversals", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var (rev, error) = db.WithLock(() => JournalService.Reverse(db, User(ctx), id));
    return error is not null ? Envelope.Fail("JOURNAL_REVERSAL_ERROR", error, 409) : Envelope.Ok<object>(rev!, 201);
});

api.MapGet("/stock", (HttpContext ctx) => Envelope.Ok<object>(Db.I.Stock));
api.MapGet("/audit-events", (HttpContext ctx) => Envelope.Ok<object>(Db.I.Audit));
api.MapGet("/outbox", (HttpContext ctx) => Envelope.Ok<object>(Db.I.Outbox)); // dev visibility only

app.Run();

public partial class Program;
