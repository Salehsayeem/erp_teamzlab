using Erp.Api.Dtos;

namespace Erp.Api.Interfaces;

// P5 scorecards (062): disclosed metric definitions + period + calc version; insufficient volume
// → NotRated; corrective-action closure needs an internal reviewer outcome (062 AC3).
public interface ISupplierScorecardsService
{
    (SupplierScorecard? Scorecard, string? Code, string? Message) Create(CurrentUser u, string? supplierId, DateOnly periodFrom, DateOnly periodTo);
    object ForSupplier(string supplierId);
    object? Detail(string id);
    (SupplierScorecard? Scorecard, string? Code, string? Message) AddSupplierResponse(CurrentUser u, string id, string? response);
    (CorrectiveAction? Action, string? Code, string? Message) AddAction(CurrentUser u, string scorecardId, string? finding, string? action);
    (CorrectiveAction? Action, string? Code, string? Message) CloseAction(CurrentUser actor, string actionId, string? outcome);
}
