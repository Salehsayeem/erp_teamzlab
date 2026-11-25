using Erp.Api.Interfaces;

namespace Erp.Api.Services;

// Q-MF1 binding: the in-wire IRequisitionSink stub — the INTERFACE is the deliverable; the
// PROCUREMENT slice replaces this registration with a real requisition creator (POST
// /api/v1/requisitions with source=mrp:{runId} idempotency + the PROC FinanceGuards) without
// touching MrpService. The deterministic reference keeps MRP replay/audit coherent in the meantime.
public sealed class MfgRequisitionSinkStub : IRequisitionSink
{
    public (string Ref, string? Error) CreateForMrpLine(CurrentUser user, MrpLine line, string note) =>
        ($"REQ-STUB-{line.RunId}-{line.LineNo}", null);
}
