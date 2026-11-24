namespace Erp.Api;

// R-T7e exposure basis shared by SO approval (057 AC2) and the exposure read:
// open posted invoices (Total − approved credits + approved debits) + open Confirmed/CreditHold
// SO totals, plain BDT sums (FX settlement deferred). Money rounding is the house AwayFromZero.
public static class CreditMath
{
    // open amount of one posted invoice given its adjustments (046: credits lower it, debits raise it)
    public static decimal InvoiceOpen(Invoice invoice, IEnumerable<AdjustmentDocument> adjustments)
    {
        var approved = adjustments.Where(a => a.Status == AdjustmentStatus.Approved).ToList();
        return invoice.Total
            - approved.Where(a => a.Kind == AdjustmentKind.Credit).Sum(a => a.Total)
            + approved.Where(a => a.Kind == AdjustmentKind.Debit).Sum(a => a.Total);
    }

    public static decimal OrderTotal(SalesOrder so) =>
        so.Lines.Sum(l => decimal.Round(l.Qty * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
}
