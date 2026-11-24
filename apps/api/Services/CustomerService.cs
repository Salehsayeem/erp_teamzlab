using Erp.Api.Interfaces;
using Erp.Api.Dtos;

namespace Erp.Api.Services;

public sealed class CustomerService(
    ICustomerRepository customers, INumberSeries numbers, IAuditRepository audit, EntityContext ec) : ICustomerService
{
    public object List() =>
        customers.All().Select(c => new
        {
            c.Id, c.Name, c.TaxId, c.Email, c.CreatedAt, c.CreatedBy,
            c.CreditLimit, c.PaymentTermNetDays, c.RemindersSuppressed, // T7 customer extensions
        }).ToList();

    public (object data, string? code, string? message) Create(CurrentUser u, CreateCustomerRequest req)
    {
        var customer = new Customer(numbers.Next("CUS"), ec.TenantId, ec.EntityId, req.Name, req.TaxId, req.Email,
            DateTimeOffset.UtcNow, u.Id, req.CreditLimit, req.PaymentTermNetDays, req.RemindersSuppressed);
        customers.Add(customer);
        audit.Log(u, "create", "customer", customer.Id,
            $"Customer '{req.Name}' created" +
            (customer.CreditLimit is { } limit ? $" with credit limit {limit:0.00}" : "") +
            $" and NET{customer.PaymentTermNetDays} terms");
        customers.Save();
        return (customer, null, null);
    }
}
