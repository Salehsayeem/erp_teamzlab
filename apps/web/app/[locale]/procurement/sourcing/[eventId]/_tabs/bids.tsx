"use client";

import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import {
  fetchBids,
  fetchSupplierRows,
  openBids,
  procError,
  submitBid,
  type Bid,
  type BidLine,
  type BidReceipt,
  type SupplierRow,
} from "@/lib/services/procurement";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
import type { Locale } from "@/lib/i18n";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Select } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { BID_STATUS_TONES, hasPerm, statusLabel, type WorkspaceCtx } from "../../../_lib/ui";

type LineDraft = { qty: string; unitPrice: string; discountPct: string };

export function BidsTab({ ctx, active }: { ctx: WorkspaceCtx; active: boolean }) {
  const { token, t, locale, event, detail, meId, perms } = ctx;
  const b = t.procurement.sourcing.workspace.bids;
  const canWrite = hasPerm(perms, "sourcing:write");
  const canApprove = hasPerm(perms, "sourcing:approve");

  const [bids, setBids] = useState<Bid[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [suppliers, setSuppliers] = useState<SupplierRow[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const [recordOpen, setRecordOpen] = useState(false);
  const [bidSupplier, setBidSupplier] = useState("");
  const [lines, setLines] = useState<Record<string, LineDraft>>({});
  const [lineError, setLineError] = useState<string | null>(null);
  const [recording, setRecording] = useState(false);
  const [receipt, setReceipt] = useState<BidReceipt | null>(null);

  const [openOpen, setOpenOpen] = useState(false);
  const [opening, setOpening] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchBids(token, event.id);
    if (res.ok) {
      setBids(res.data);
      setPhase("success");
    } else {
      setBids(null);
      setPhase("error");
    }
  }, [token, event.id]);
  useEffect(() => { if (active) void load(); }, [active, load]);
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchSupplierRows(token).then((res) => {
      if (!cancelled && res.ok) setSuppliers(res.data.filter((s) => s.status !== "Suspended" && s.status !== "Blocked"));
    });
    return () => { cancelled = true; };
  }, [token]);

  const errMap: Record<string, string> = {
    BID_CLOSED: b.openTooEarly,
    BIDS_SEALED: b.openTooEarly,
    BIDS_ALREADY_OPEN: b.sealedNote,
    BID_OPENING_APPROVAL_ERROR: t.procurement.common.makerChecker,
    EVENT_NOT_PUBLISHED: b.recordNote,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  const now = Date.now();
  const windowClosed = now > new Date(event.closesAt).getTime();
  const isOpenable = canApprove && windowClosed && meId !== null && meId !== event.createdBy;
  const isMaker = meId !== null && meId === event.createdBy;
  const eligibleSuppliers = suppliers.filter((s) => s.status === "Active" || s.status === "Pending");

  const lineTotal = (draft: LineDraft) => {
    const qty = Number(draft.qty) || 0;
    const price = Number(draft.unitPrice) || 0;
    const discount = Number(draft.discountPct) || 0;
    return qty * price * (1 - discount / 100);
  };

  function initLines() {
    const draft: Record<string, LineDraft> = {};
    for (const lot of detail.lots) draft[lot.id] = { qty: "", unitPrice: "", discountPct: "0" };
    setLines(draft);
    setBidSupplier("");
    setLineError(null);
    setReceipt(null);
  }

  async function submitRecord(e: FormEvent) {
    e.preventDefault();
    if (!bidSupplier) {
      setLineError(b.errors.supplierRequired);
      return;
    }
    const payload = detail.lots
      .map((lot) => ({ lot, draft: lines[lot.id] }))
      .filter((x): x is { lot: (typeof detail.lots)[number]; draft: LineDraft } => x.draft !== undefined && x.draft.qty !== "")
      .map(({ lot, draft }) => ({
        lotId: lot.id,
        qty: Number(draft.qty),
        unitPrice: Number(draft.unitPrice) || 0,
        discountPct: Number(draft.discountPct) || 0,
      }));
    if (payload.length === 0 || payload.some((l) => !(l.qty > 0))) {
      setLineError(b.errors.qtyInvalid);
      return;
    }
    if (payload.some((l) => l.unitPrice < 0 || l.discountPct < 0 || l.discountPct > 100)) {
      setLineError(b.errors.priceInvalid);
      return;
    }
    setRecording(true);
    setLineError(null);
    const res = await submitBid(token, event.id, { supplierId: bidSupplier, lines: payload });
    setRecording(false);
    if (!res.ok) {
      setLineError(procError(res.code, res.message, errMap, b.bidFailed));
      return;
    }
    setReceipt(res.data);
    setRecordOpen(false);
    void load();
  }

  async function runOpen() {
    setOpening(true);
    setActionError(null);
    const res = await openBids(token, event.id);
    setOpening(false);
    if (!res.ok) {
      setActionError(procError(res.code, res.message, errMap, b.openFailed));
      return;
    }
    setOpenOpen(false);
    void load();
  }

  const unsealedView = (bid: Bid) => bid.status === "Opened" || canApprove || bid.contentDigest !== undefined;
  const supplierName = useMemo(() => new Map(suppliers.map((s) => [s.id, s.name])), [suppliers]);

  const columns: Column<Bid>[] = [
    { key: "supplier", label: b.columns.supplier, render: (bid) => supplierName.get(bid.supplierId) ?? bid.supplierId },
    { key: "status", label: b.columns.status, render: (bid) => <Badge tone={BID_STATUS_TONES[bid.status] ?? "muted"}>{statusLabel(t, bid.status)}</Badge> },
    { key: "submitted", label: b.columns.submitted, render: (bid) => <span className="t-caption">{fmtDate(bid.submittedAt, locale)}</span> },
    { key: "opened", label: b.columns.opened, render: (bid) => (bid.openedAt ? <span className="t-caption">{fmtDate(bid.openedAt, locale)} · <span className="mono">{bid.openedBy}</span></span> : bid.status === "Submitted" ? <Badge tone="warning">{b.sealedBadge}</Badge> : "—") },
    { key: "digest", label: b.columns.digest, mono: true, render: (bid) => (bid.contentDigest ? `${bid.contentDigest.slice(0, 12)}…` : "—") },
    {
      key: "actions",
      label: b.columns.actions,
      render: (bid) => (
        <div className="row-actions">
          <Button small variant="secondary" aria-expanded={expandedId === bid.bidId} onClick={() => setExpandedId(expandedId === bid.bidId ? null : bid.bidId)}>
            {expandedId === bid.bidId ? b.hideLines : b.linesBtn}
          </Button>
        </div>
      ),
    },
  ];

  if (phase === "loading") return <LoadingSkeleton rows={4} />;
  if (phase === "error") return <ErrorState message={b.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = bids ?? [];
  return (
    <div className="stack">
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      {receipt ? (
        <section className="card">
          <h3 className="t-title">{b.receiptTitle}</h3>
          <p className="t-caption">{b.receiptBid}: <span className="mono">{receipt.bidId}</span></p>
          <p className="t-caption">{b.receiptVersion}: <span className="mono">v{receipt.eventVersion}</span></p>
          <p className="t-caption">{b.receiptAt}: {fmtDate(receipt.submittedAt, locale)}</p>
          <p className="t-caption">{b.receiptDigest}: <span className="mono">{receipt.contentDigest}</span></p>
        </section>
      ) : null}

      <section className="card">
        <h3 className="t-title">{b.title}</h3>
        <p className="t-caption">{b.sealedNote}</p>
        {list.length === 0 ? (
          <EmptyState title={b.empty} />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>{columns.map((c) => <th key={c.key} scope="col">{c.label}</th>)}</tr>
              </thead>
              <tbody>
                {list.map((bid) => (
                  <FragmentRows
                    key={bid.bidId}
                    bid={bid}
                    columns={columns}
                    expanded={expandedId === bid.bidId}
                    labels={b.lineColumns}
                    locale={locale}
                    currency={event.currency}
                    unsealed={unsealedView(bid)}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          {canWrite && event.status === "Published" && !windowClosed && detail.lots.length > 0 ? (
            <Button onClick={() => { initLines(); setRecordOpen(true); }}>{b.recordBid}</Button>
          ) : null}
          {canApprove ? (
            isMaker ? (
              <p className="banner" role="status">{t.procurement.common.makerChecker}</p>
            ) : (
              <Button disabled={!isOpenable} onClick={() => { setActionError(null); setOpenOpen(true); }}>{b.openBids}</Button>
            )
          ) : null}
          {canApprove && !windowClosed ? <p className="t-caption">{b.openTooEarly}</p> : null}
        </div>
      </section>

      <Modal open={recordOpen} onClose={() => setRecordOpen(false)} title={b.recordTitle} closeLabel={t.common.cancel} busy={recording}>
        <p className="t-caption">{b.recordNote}</p>
        <form onSubmit={(e) => void submitRecord(e)}>
          <Select
            id="bid-supplier"
            label={b.supplier}
            placeholder={b.supplier}
            options={eligibleSuppliers.map((s) => ({ value: s.id, label: `${s.name} (${s.taxId})` }))}
            value={bidSupplier}
            onChange={(e) => setBidSupplier(e.target.value)}
            error={lineError ?? undefined}
          />
          <div className="stack" style={{ marginBlockStart: 12 }}>
            {detail.lots.map((lot) => {
              const draft = lines[lot.id] ?? { qty: "", unitPrice: "", discountPct: "0" };
              return (
                <div className="form-grid" key={lot.id}>
                  <p className="t-body-strong">{lot.lotNo} — {lot.description}</p>
                  <Input id={`bl-qty-${lot.id}`} label={b.qty} type="number" min="0" step="any" value={draft.qty} onChange={(e) => setLines({ ...lines, [lot.id]: { ...draft, qty: e.target.value } })} />
                  <Input id={`bl-price-${lot.id}`} label={b.unitPrice} type="number" min="0" step="any" value={draft.unitPrice} onChange={(e) => setLines({ ...lines, [lot.id]: { ...draft, unitPrice: e.target.value } })} />
                  <Input id={`bl-disc-${lot.id}`} label={b.discountPct} type="number" min="0" max="100" step="any" value={draft.discountPct} onChange={(e) => setLines({ ...lines, [lot.id]: { ...draft, discountPct: e.target.value } })} />
                  <p className="t-caption">{b.lineTotal}: <span className="mono">{fmtMoney(lineTotal(draft), locale, event.currency)}</span></p>
                </div>
              );
            })}
          </div>
          {lineError ? <p className="banner" role="alert">{lineError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setRecordOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={recording}>{b.submitBid}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={openOpen} onClose={() => setOpenOpen(false)} title={b.openTitle} closeLabel={t.common.cancel} busy={opening}>
        <p>{b.openConsequence}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setOpenOpen(false)}>{t.common.cancel}</Button>
          <Button loading={opening} onClick={() => void runOpen()}>{b.openBids}</Button>
        </div>
      </Modal>
    </div>
  );
}

// Bid row + expandable sealed/unsealed line sub-row (PO sub-row idiom).
function FragmentRows({
  bid,
  columns,
  expanded,
  labels,
  locale,
  currency,
  unsealed,
}: {
  bid: Bid;
  columns: Column<Bid>[];
  expanded: boolean;
  labels: { lot: string; qty: string; unitPrice: string; discountPct: string; lineTotal: string };
  locale: Locale;
  currency: string;
  unsealed: boolean;
}) {
  return (
    <Fragment key={bid.bidId}>
      <tr>
        {columns.map((c) => (
          <td key={c.key} data-label={c.label} className={c.key === "digest" ? "mono" : undefined}>
            {c.render(bid)}
          </td>
        ))}
      </tr>
      {expanded ? (
        <tr className="sub-row">
          <td colSpan={columns.length}>
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{labels.lot}</th>
                  <th scope="col">{labels.qty}</th>
                  {unsealed ? <th scope="col">{labels.unitPrice}</th> : null}
                  {unsealed ? <th scope="col">{labels.discountPct}</th> : null}
                  {unsealed ? <th scope="col">{labels.lineTotal}</th> : null}
                </tr>
              </thead>
              <tbody>
                {bid.lines.map((l) => (
                  <tr key={l.lotId}>
                    <td data-label={labels.lot} className="mono">{l.lotId}</td>
                    <td data-label={labels.qty} className="mono">{fmtNumber(l.qty, locale)}</td>
                    {unsealed ? <td data-label={labels.unitPrice} className="mono">{fmtMoney(l.unitPrice ?? 0, locale, currency)}</td> : null}
                    {unsealed ? <td data-label={labels.discountPct} className="mono">{fmtNumber(l.discountPct ?? 0, locale)}</td> : null}
                    {unsealed ? <td data-label={labels.lineTotal} className="mono">{fmtMoney(l.lineTotal ?? 0, locale, currency)}</td> : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </td>
        </tr>
      ) : null}
    </Fragment>
  );
}
