using Erp.Api.Interfaces;

namespace Erp.Api.Interfaces;

// Production-order lifecycle + shop-floor reporting (ERP-MFG-007/008/010/012/013/041/042/043/067/069).
// Every posting path: FinanceGuards.EnsurePeriodOpen, then journal + stock movement + order state +
// audit + outbox in ONE SaveChanges; status is a concurrency token and reporting serializes on the
// order row (FOR UPDATE) so the five-value ledger can never lose an update.
//
// Gate contracts (controller envelope): Release returns code MFG_RELEASE_BLOCKED with data =
// object[] of failed prerequisites; Close returns MFG_CLOSE_BLOCKED with data = object[] of
// unreconciled items — the controller lifts them into the error object.
public interface IProductionOrderService
{
    object List(ProductionOrderStatus? status, string? outputSku);
    (object? data, string? code, string? message) Create(CurrentUser user, Dtos.CreateProductionOrderRequest request);
    (object? data, string? code, string? message) Transition(CurrentUser user, string orderId, Dtos.TransitionOrderRequest request);
    (object? data, string? code, string? message) Release(CurrentUser releaser, string orderId, string? reason);
    (object? data, string? code, string? message) IssueMaterial(CurrentUser user, string orderId, Dtos.MaterialIssueRequest request);
    (object? data, string? code, string? message) ReturnIssue(CurrentUser user, string orderId, Dtos.IssueReturnRequest request);
    (object? data, string? code, string? message) ReportCompletion(CurrentUser user, string orderId, Dtos.CompletionRequest request);
    (object? data, string? code, string? message) ReportScrap(CurrentUser user, string orderId, Dtos.ScrapRequest request);
    (object? data, string? code, string? message) CloseCheck(string orderId);
    (object? data, string? code, string? message) Close(CurrentUser closer, string orderId, string? residualAcknowledgment);
    (object? data, string? code, string? message) Reopen(CurrentUser user, string orderId, string reason);
}
