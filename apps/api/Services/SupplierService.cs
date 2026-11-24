using Erp.Api.Dtos;
using Erp.Api.Interfaces;

namespace Erp.Api.Services;

public sealed class SupplierService(
    ISupplierRepository suppliers, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ISupplierService
{
    public object List() =>
        suppliers.All().Select(s => new { s.Id, s.Name, s.TaxId, s.Email, s.CreatedAt, s.CreatedBy, s.Status, s.StatusReason }).ToList();

    // read-only gate list (status + exception windows as stored, 023 AC1/AC3)
    public (object? data, string? code, string? message) Gates(string supplierId)
    {
        if (suppliers.Find(supplierId) is null) return (null, "SUPPLIER_NOT_FOUND", "Supplier not found.");
        return (suppliers.GatesFor(supplierId).Select(ProjGate).ToList(), null, null);
    }

    public (object data, string? code, string? message) Create(CurrentUser u, CreateSupplierRequest req)
    {
        var supplier = new Supplier(numbers.Next("SUP"), ec.TenantId, ec.EntityId, req.Name, req.TaxId, req.Email, DateTimeOffset.UtcNow, u.Id);
        suppliers.Add(supplier);
        audit.Log(u, "create", "supplier", supplier.Id, $"Supplier '{req.Name}' created");
        suppliers.Save();
        return (supplier, null, null);
    }

    // 047 AC1: lifecycle transition with a mandatory reason (validated in the controller)
    public (object? data, string? code, string? message) SetStatus(CurrentUser user, string supplierId, SetSupplierStatusRequest request)
    {
        var supplier = suppliers.Find(supplierId);
        if (supplier is null) return (null, "SUPPLIER_NOT_FOUND", "Supplier not found.");
        suppliers.SetSupplierStatus(supplier, request.Status, request.Reason);
        audit.Log(user, "status", "supplier", supplierId, $"Supplier {supplierId} → {request.Status}: {request.Reason}");
        suppliers.Save();
        return (Proj(supplier), null, null);
    }

    // 023 AC1 / 044: effective-dated eligibility criterion; re-POSTing an existing gate WITH an
    // exceptionUntil records the time-bounded exception on that gate (023 AC3, R-T6e: granted
    // under suppliers:write)
    public (object? data, string? code, string? message) AddGate(CurrentUser user, string supplierId, AddSupplierGateRequest request)
    {
        if (suppliers.Find(supplierId) is null) return (null, "SUPPLIER_NOT_FOUND", "Supplier not found.");
        var existing = suppliers.FindGate(supplierId, request.GateCode, request.EffectiveFrom);
        if (existing is not null)
        {
            if (request.ExceptionUntil is not { } until)
                return (null, "GATE_DUPLICATE", $"Gate {request.GateCode} effective {request.EffectiveFrom:yyyy-MM-dd} already exists.");
            suppliers.SetGateException(existing, user.Id, ToUtc(until));
            audit.Log(user, "except", "supplier_gate", existing.Id,
                $"Time-bounded exception on gate {request.GateCode} until {until:u}, granted by {user.Id}");
            suppliers.Save();
            return (ProjGate(existing), null, null);
        }

        var gate = new SupplierGate($"{supplierId}:{request.GateCode}:{request.EffectiveFrom:yyyyMMdd}",
            ec.TenantId, ec.EntityId, supplierId, request.GateCode, request.Severity, request.EffectiveFrom,
            request.EffectiveTo, request.EvidenceRef, request.ExceptionUntil is { } exceptBy ? user.Id : null,
            request.ExceptionUntil is { } exceptAt ? ToUtc(exceptAt) : null);
        suppliers.AddGate(gate);
        audit.Log(user, "add", "supplier_gate", gate.Id,
            $"Gate {request.GateCode} ({request.Severity}) on {supplierId} effective {request.EffectiveFrom:yyyy-MM-dd}"
            + (request.ExceptionUntil is { } e ? $" excepted until {e:u}" : ""));
        suppliers.Save();
        return (ProjGate(gate), null, null);
    }

    private static object Proj(Supplier s) => new
    {
        s.Id, s.Name, s.TaxId, s.Email, s.CreatedAt, s.CreatedBy, s.Status, s.StatusReason,
    };

    // date-only or local-offset client instants deserialize with a non-zero offset — Npgsql
    // refuses to write those to timestamptz, so every client-supplied instant normalizes to UTC
    private static DateTimeOffset ToUtc(DateTimeOffset v) => new(v.UtcDateTime);

    private static object ProjGate(SupplierGate g) => new
    {
        g.Id, g.SupplierId, g.GateCode, g.Severity, g.EffectiveFrom, g.EffectiveTo, g.EvidenceRef,
        g.ExceptionBy, g.ExceptionUntil,
    };
}
