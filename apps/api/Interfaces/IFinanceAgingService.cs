namespace Erp.Api.Interfaces;

public interface IFinanceAgingService
{
    // 031: buckets over open AR (account 1200) with dunning-ready worklist
    (object? aging, string? code, string? message) Receivables(DateOnly asOf, string basis);
}
