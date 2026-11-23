using System.Text.Json.Serialization;
using Erp.Api;
using Erp.Api.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(Db.I);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<BindingEnvelopeMiddleware>();
app.UseMiddleware<AuthMiddleware>();

var api = app.MapGroup("/api/v1");
CurrentUser User(HttpContext ctx) => (CurrentUser)ctx.Items["user"]!;

// finding 5: status derives from the error code (400 body/qty validation, 404 missing record, 403 permission, 409 state conflict)
static int StatusFor(string code) => code switch
{
    "VALIDATION_ERROR" => 400,
    "FORBIDDEN" => 403,
    _ when code.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => 404,
    _ => 409,
};

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

api.MapPost("/suppliers", (HttpContext ctx, CreateSupplierReq req) => Idem.Wrap(ctx, Db.I, () =>
{
    var db = Db.I;
    var err = Guards.Check(req.Name, "Name") ?? Guards.Check(req.TaxId, "TaxId") ?? Guards.CheckEmail(req.Email);
    if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
    var u = User(ctx);
    if (!u.Has("suppliers:write")) return (Envelope.FailBody("FORBIDDEN", "suppliers:write permission required."), 403);
    var s = db.WithLock(() =>
    {
        var supplier = new Supplier(db.NextDoc("SUP"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        db.Suppliers.Add(supplier);
        Audit.Log(db, u, "create", "supplier", supplier.Id, $"Supplier '{req.Name}' created");
        return supplier;
    });
    return (Envelope.OkBody(s), 201);
}));

api.MapGet("/purchase-orders", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.PurchaseOrders.Select(p => new
    {
        p.Id, p.SupplierId, p.Status, p.Lines, p.CreatedBy, p.CreatedAt, p.ApprovedBy, p.ApprovedAt, p.RejectionReason,
        total = p.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
    }).ToList())));

api.MapPost("/purchase-orders", (HttpContext ctx, CreatePoReq req) => Idem.Wrap(ctx, Db.I, () =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("po:write")) return (Envelope.FailBody("FORBIDDEN", "po:write permission required."), 403);
    var lineErr = Guards.CheckLines(req.Lines);
    if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
    if (!db.WithLock(() => db.Suppliers.Any(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == req.SupplierId)))
        return (Envelope.FailBody("SUPPLIER_NOT_FOUND", "Supplier does not exist."), 404);
    var po = db.WithLock(() => PoService.Create(db, u, req.SupplierId, req.Lines));
    return (Envelope.OkBody(po), 201);
}));

api.MapPost("/purchase-orders/{id}/submit", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("po:write")) return Envelope.Fail("FORBIDDEN", "po:write permission required.", 403);
    var (updated, code) = db.WithLock(() =>
    {
        var po = db.PurchaseOrders.FirstOrDefault(p => p.TenantId == db.Tenant.Id && p.LegalEntityId == db.Entity.Id && p.Id == id);
        if (po is null) return ((PurchaseOrder?)null, (string?)"PO_NOT_FOUND");
        if (po.Status != PoStatus.Draft) return ((PurchaseOrder?)null, (string?)"PO_NOT_SUBMITTABLE");
        db.PurchaseOrders[db.PurchaseOrders.IndexOf(po)] = po with { Status = PoStatus.PendingApproval };
        Audit.Log(db, u, "submit", "purchase_order", id, "PO submitted for approval");
        return ((PurchaseOrder?)db.PurchaseOrders.First(p => p.Id == id), null);
    });
    return updated is null ? Envelope.Fail(code!, code == "PO_NOT_FOUND" ? "Purchase order not found." : "Only draft POs can be submitted.", StatusFor(code!)) : Envelope.Ok<object>(updated);
});

api.MapPost("/purchase-orders/{id}/approvals", (HttpContext ctx, string id, ApprovePoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (po, code, error) = db.WithLock(() => PoService.Approve(db, u, id, req.Reason));
    return error is not null ? Envelope.Fail(code!, error, StatusFor(code!)) : Envelope.Ok<object>(po!);
});

api.MapPost("/receipts", (HttpContext ctx, CreateReceiptReq req) => Idem.Wrap(ctx, Db.I, () =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("receipts:write")) return (Envelope.FailBody("FORBIDDEN", "receipts:write permission required."), 403);
    var (receipt, journal, code, error) = db.WithLock(() => ReceiptService.Receive(db, u, req.PurchaseOrderId, req.ItemSku, req.Qty));
    return error is not null
        ? (Envelope.FailBody(code!, error), StatusFor(code!))
        : (Envelope.OkBody(new { receipt, journal }), 201);
}));

api.MapGet("/customers", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Customers.Select(c => new { c.Id, c.Name, c.TaxId, c.Email, c.CreatedAt, c.CreatedBy }).ToList())));

api.MapPost("/customers", (HttpContext ctx, CreateCustomerReq req) => Idem.Wrap(ctx, Db.I, () =>
{
    var db = Db.I;
    var err = Guards.Check(req.Name, "Name") ?? Guards.Check(req.TaxId, "TaxId") ?? Guards.CheckEmail(req.Email);
    if (err is not null) return (Envelope.FailBody("VALIDATION_ERROR", err), 400);
    var u = User(ctx);
    if (!u.Has("customers:write")) return (Envelope.FailBody("FORBIDDEN", "customers:write permission required."), 403);
    var c = db.WithLock(() =>
    {
        var customer = new Customer(db.NextDoc("CUS"), db.Tenant.Id, db.Entity.Id, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        db.Customers.Add(customer);
        Audit.Log(db, u, "create", "customer", customer.Id, $"Customer '{req.Name}' created");
        return customer;
    });
    return (Envelope.OkBody(c), 201);
}));

api.MapGet("/sales-orders", (HttpContext ctx) =>
    Envelope.Ok<object>(Db.I.WithLock(() => Db.I.SalesOrders.Select(s => new
    {
        s.Id, s.CustomerId, s.Status, s.Lines, s.CreatedBy, s.CreatedAt, s.ApprovedBy, s.ApprovedAt, s.RejectionReason,
        total = s.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero)),
    }).ToList())));

api.MapPost("/sales-orders", (HttpContext ctx, CreateSoReq req) => Idem.Wrap(ctx, Db.I, () =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("so:write")) return (Envelope.FailBody("FORBIDDEN", "so:write permission required."), 403);
    var lineErr = Guards.CheckLines(req.Lines);
    if (lineErr is not null) return (Envelope.FailBody("VALIDATION_ERROR", lineErr), 400);
    if (!db.WithLock(() => db.Customers.Any(c => c.TenantId == db.Tenant.Id && c.LegalEntityId == db.Entity.Id && c.Id == req.CustomerId)))
        return (Envelope.FailBody("CUSTOMER_NOT_FOUND", "Customer does not exist."), 404);
    var so = db.WithLock(() => SalesService.Create(db, u, req.CustomerId, req.Lines));
    return (Envelope.OkBody(so), 201);
}));

api.MapPost("/sales-orders/{id}/submit", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (updated, code) = db.WithLock(() =>
    {
        var so = db.SalesOrders.FirstOrDefault(s => s.TenantId == db.Tenant.Id && s.LegalEntityId == db.Entity.Id && s.Id == id);
        if (so is null) return ((SalesOrder?)null, (string?)"SO_NOT_FOUND");
        if (so.Status != SoStatus.Draft) return ((SalesOrder?)null, (string?)"SO_NOT_SUBMITTABLE");
        db.SalesOrders[db.SalesOrders.IndexOf(so)] = so with { Status = SoStatus.PendingApproval };
        Audit.Log(db, u, "submit", "sales_order", id, "SO submitted for approval");
        return ((SalesOrder?)db.SalesOrders.First(s => s.Id == id), null);
    });
    return updated is null ? Envelope.Fail(code!, code == "SO_NOT_FOUND" ? "Sales order not found." : "Only draft sales orders can be submitted.", StatusFor(code!)) : Envelope.Ok<object>(updated);
});

api.MapPost("/sales-orders/{id}/approvals", (HttpContext ctx, string id, ApproveSoReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    var (so, code, error) = db.WithLock(() => SalesService.Approve(db, u, id, req.Reason));
    return error is not null ? Envelope.Fail(code!, error, StatusFor(code!)) : Envelope.Ok<object>(so!);
});

api.MapPost("/sales-orders/{id}/shipments", (HttpContext ctx, string id, ShipReq req) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("shipments:write")) return Envelope.Fail("FORBIDDEN", "shipments:write permission required.", 403);
    var (shipment, journal, code, error) = db.WithLock(() => SalesService.Ship(db, u, id, req.ItemSku, req.Qty));
    return error is not null ? Envelope.Fail(code!, error, StatusFor(code!)) : Envelope.Ok<object>(new { shipment, journal }, 201);
});

api.MapPost("/sales-orders/{id}/invoices", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var u = User(ctx);
    if (!u.Has("so:write")) return Envelope.Fail("FORBIDDEN", "so:write permission required.", 403);
    var (journal, code, error) = db.WithLock(() => SalesService.Invoice(db, u, id));
    return error is not null ? Envelope.Fail(code!, error, StatusFor(code!)) : Envelope.Ok<object>(journal!, 201);
});

api.MapGet("/journals", (HttpContext ctx) =>
{
    var u = User(ctx);
    return u.Has("journals:read")
        ? Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Journals))
        : Envelope.Fail("FORBIDDEN", "journals:read permission required.", 403);
});

api.MapPost("/journals/{id}/reversals", (HttpContext ctx, string id) =>
{
    var db = Db.I;
    var (rev, code, error) = db.WithLock(() => JournalService.Reverse(db, User(ctx), id));
    return error is not null ? Envelope.Fail(code!, error, StatusFor(code!)) : Envelope.Ok<object>(rev!, 201);
});

api.MapGet("/stock", (HttpContext ctx) => Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Stock)));

api.MapGet("/audit-events", (HttpContext ctx) =>
{
    var u = User(ctx);
    return u.Has("*")
        ? Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Audit))
        : Envelope.Fail("FORBIDDEN", "admin permission required.", 403);
});

api.MapGet("/outbox", (HttpContext ctx) => // dev visibility only
{
    var u = User(ctx);
    return u.Has("*")
        ? Envelope.Ok<object>(Db.I.WithLock(() => Db.I.Outbox))
        : Envelope.Fail("FORBIDDEN", "admin permission required.", 403);
});

app.Run();

public partial class Program;
