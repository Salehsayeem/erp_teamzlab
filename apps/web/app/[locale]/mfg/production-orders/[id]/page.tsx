"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  closeOrder,
  fetchBoms,
  fetchCloseCheck,
  fetchProductionOrders,
  issueMaterial,
  reportCompletion,
  reportScrap,
  releaseOrder,
  reopenOrder,
  returnIssue,
  transitionOrder,
} from "@/lib/services/mfg";
import type { BomRow, CloseCheckRow, ProductionOrderRow } from "@/lib/services/mfg";
import { useAuth } from "@/hooks/useAuth";
import { fmtMoney, fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, hasPermission, mfgActionError, mfgStatusLabel, mfgTone, useMfgMe } from "../../_lib/helpers";

type SessionIssue = { id: string; componentSku: string; qty: number; value: number; journalId: string | null };

// The posting endpoints answer NESTED payloads ({ issue, journal } / { completion, journal } /
// { scrap, journal }) — never read them flat. Tolerant narrowers keep `any` out and degrade to
// the submitted form values when a field is missing.
function asRecord(v: unknown): Record<string, unknown> | null {
  return typeof v === "object" && v !== null ? (v as Record<string, unknown>) : null;
}
function strOf(v: unknown): string | null {
  return typeof v === "string" && v.length > 0 ? v : null;
}
function numOf(v: unknown): number | null {
  return typeof v === "number" && Number.isFinite(v) ? v : null;
}
type SessionCompletion = { id: string; qty: number; note: string | null; journalId: string | null };
type SessionScrap = { id: string; itemSku: string; qty: number; reasonCode: string; disposition: string; journalId: string | null };

export default function ProductionOrderDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.orderDetail;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady } = useMfgMe(token);
  const canRelease = hasPermission(permissions, "mfg:release");
  const canClose = hasPermission(permissions, "mfg:close");
  const canReopen = hasPermission(permissions, "mfg:reopen");

  const [order, setOrder] = useState<ProductionOrderRow | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [notFound, setNotFound] = useState(false);

  const [revision, setRevision] = useState<BomRow | null>(null);
  const [check, setCheck] = useState<CloseCheckRow | null>(null);

  const [sessionIssues, setSessionIssues] = useState<SessionIssue[]>([]);
  const [sessionCompletions, setSessionCompletions] = useState<SessionCompletion[]>([]);
  const [sessionScraps, setSessionScraps] = useState<SessionScrap[]>([]);

  const [move, setMove] = useState("");
  const [cancelReason, setCancelReason] = useState("");
  const [cancelOpen, setCancelOpen] = useState(false);
  const [releaseOpen, setReleaseOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [residualAck, setResidualAck] = useState("");
  const [reopenReason, setReopenReason] = useState("");
  const [amendQty, setAmendQty] = useState("");
  const [amendReason, setAmendReason] = useState("");
  const [amendErrors, setAmendErrors] = useState<{ qty?: string; reason?: string }>({});
  const [issueForm, setIssueForm] = useState({ componentSku: "", qty: "" });
  const [issueErrors, setIssueErrors] = useState<{ component?: string; qty?: string }>({});
  const [returnForm, setReturnForm] = useState({ issueId: "", qty: "" });
  const [returnErrors, setReturnErrors] = useState<{ issue?: string; qty?: string }>({});
  const [completionForm, setCompletionForm] = useState({ qty: "", note: "" });
  const [completionErrors, setCompletionErrors] = useState<{ qty?: string }>({});
  const [scrapForm, setScrapForm] = useState({ itemSku: "", qty: "", reasonCode: "", disposition: "Disposal" });
  const [scrapErrors, setScrapErrors] = useState<{ sku?: string; qty?: string; reason?: string }>({});

  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  function pushToast(tone: ToastItem["tone"], message: string) {
    setToasts((prev) => [...prev, { id: crypto.randomUUID(), tone, message }]);
  }
  function dismissToast(id: string) {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchProductionOrders(token);
    if (!res.ok) {
      setPhase("error");
      return;
    }
    const found = res.data.find((o) => o.id === params.id) ?? null;
    if (!found) {
      setNotFound(true);
      setPhase("success");
      return;
    }
    setOrder(found);
    setPhase("success");
  }, [token, params.id]);
  useEffect(() => { void load(); }, [load]);

  // frozen release revision lines feed the issue/return pickers; the close-check
  // view carries the reconciliation ledger (error.items shape on the wire)
  const loadSide = useCallback(async () => {
    if (!token || !order) return;
    if (order.releasedBomRevisionId) {
      const res = await fetchBoms(token, order.outputSku, "Approved");
      const found = res.ok ? res.data.find((b) => b.id === order.releasedBomRevisionId) : undefined;
      setRevision(found ?? null);
    } else {
      setRevision(null);
    }
    const chk = await fetchCloseCheck(token, order.id);
    setCheck(chk.ok ? chk.data : null);
  }, [token, order]);
  useEffect(() => { void loadSide(); }, [loadSide]);

  async function act(run: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>, note: string | null) {
    setBusy(true);
    setActionError(null);
    const res = await run();
    setBusy(false);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return false;
    }
    if (note !== null) pushToast("success", note);
    await load();
    await loadSide();
    return true;
  }

  // ---- lifecycle -----------------------------------------------------------------------
  // the legal map (007) mirrored client-side: releases/closures have their own
  // endpoints, transitions cover Plan/Start/Complete/Cancel
  const legalMoves = useMemo(() => {
    if (!order) return [] as string[];
    switch (order.status) {
      case "Draft": return ["Planned", "Cancelled"];
      case "Released": return ["InProcess"];
      case "InProcess": return ["Completed"];
      default: return [];
    }
  }, [order]);

  async function submitTransition(target: string, reason: string | null) {
    if (!order || !token) return false;
    const ok = await act(
      () => transitionOrder(token, order.id, { status: target, newPlannedQty: null, reason }),
      target === "Planned" ? tc.plannedNote : target === "InProcess" ? tc.startedNote : target === "Completed" ? tc.completedNote : tc.cancelledNote,
    );
    if (ok) {
      setMove("");
      setCancelReason("");
      setCancelOpen(false);
    }
    return ok;
  }

  function chooseMove(target: string) {
    if (target === "Cancelled") {
      setCancelOpen(true);
      return;
    }
    void submitTransition(target, null);
  }

  async function confirmRelease() {
    if (!order || !token) return;
    await act(() => releaseOrder(token, order.id, null), tc.releasedNote);
    setReleaseOpen(false);
  }

  async function submitAmend(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    const qty = Number(amendQty);
    const errs: { qty?: string; reason?: string } = {};
    if (!amendQty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    const postRelease = order.status !== "Draft" && order.status !== "Planned";
    if (postRelease && !amendReason.trim()) errs.reason = tc.amendReasonHint;
    setAmendErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await act(
      () => transitionOrder(token, order.id, { status: order.status, newPlannedQty: qty, reason: amendReason.trim() || null }),
      tc.amendedNote,
    );
    if (ok) {
      setAmendQty("");
      setAmendReason("");
    }
  }

  // ---- shop-floor reporting ----------------------------------------------------------------
  async function submitIssue(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    const qty = Number(issueForm.qty);
    const errs: { component?: string; qty?: string } = {};
    if (!issueForm.componentSku.trim()) errs.component = tc.errors.componentRequired;
    if (!issueForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setIssueErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setActionError(null);
    const res = await issueMaterial(token, order.id, { componentSku: issueForm.componentSku.trim(), qty });
    setBusy(false);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return;
    }
    const root = asRecord(res.data);
    const issue = asRecord(root?.issue);
    const journal = asRecord(root?.journal);
    const unitCost = numOf(issue?.unitCost) ?? 0;
    const row: SessionIssue = {
      id: strOf(issue?.id) ?? "—",
      componentSku: strOf(issue?.componentSku) ?? issueForm.componentSku.trim(),
      qty,
      value: numOf(issue?.lineValue) ?? qty * unitCost,
      journalId: strOf(journal?.id) ?? strOf(issue?.journalId),
    };
    setSessionIssues((prev) => [row, ...prev]);
    pushToast("success", tc.issuedNote);
    setIssueForm({ componentSku: "", qty: "" });
    await load();
    await loadSide();
  }

  async function submitReturn(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    const qty = Number(returnForm.qty);
    const errs: { issue?: string; qty?: string } = {};
    if (!returnForm.issueId) errs.issue = tc.errors.issueRequired;
    if (!returnForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setReturnErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setActionError(null);
    const res = await returnIssue(token, order.id, { issueId: returnForm.issueId, qty });
    setBusy(false);
    if (!res.ok) setActionError(mfgActionError(t, res.code, res.message));
    else {
      pushToast("success", tc.returnedNote);
      setReturnForm({ issueId: "", qty: "" });
      await load();
      await loadSide();
    }
  }

  async function submitCompletion(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    const qty = Number(completionForm.qty);
    if (!completionForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) {
      setCompletionErrors({ qty: tc.errors.qtyInvalid });
      return;
    }
    setCompletionErrors({});
    setBusy(true);
    setActionError(null);
    const res = await reportCompletion(token, order.id, { qty, note: completionForm.note.trim() || null });
    setBusy(false);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return;
    }
    const root = asRecord(res.data);
    const completion = asRecord(root?.completion);
    const journal = asRecord(root?.journal);
    const row: SessionCompletion = {
      id: strOf(completion?.id) ?? "—",
      qty: numOf(completion?.qty) ?? qty,
      note: strOf(completion?.note) ?? (completionForm.note.trim() || null),
      journalId: strOf(journal?.id) ?? strOf(completion?.journalId),
    };
    setSessionCompletions((prev) => [row, ...prev]);
    pushToast("success", tc.reportedNote);
    setCompletionForm({ qty: "", note: "" });
    await load();
    await loadSide();
  }

  async function submitScrap(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    const qty = Number(scrapForm.qty);
    const errs: { sku?: string; qty?: string; reason?: string } = {};
    if (!scrapForm.itemSku.trim()) errs.sku = tc.errors.skuRequired;
    if (!scrapForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!scrapForm.reasonCode.trim()) errs.reason = tc.errors.reasonRequired;
    setScrapErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setActionError(null);
    const res = await reportScrap(token, order.id, {
      itemSku: scrapForm.itemSku.trim(), qty, reasonCode: scrapForm.reasonCode.trim(), disposition: scrapForm.disposition,
    });
    setBusy(false);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return;
    }
    const root = asRecord(res.data);
    const scrap = asRecord(root?.scrap);
    const journal = asRecord(root?.journal);
    const row: SessionScrap = {
      id: strOf(scrap?.id) ?? "—",
      itemSku: strOf(scrap?.itemSku) ?? scrapForm.itemSku.trim(),
      qty: numOf(scrap?.qty) ?? qty,
      reasonCode: strOf(scrap?.reasonCode) ?? scrapForm.reasonCode.trim(),
      disposition: strOf(scrap?.disposition) ?? scrapForm.disposition,
      journalId: strOf(journal?.id) ?? strOf(scrap?.journalId),
    };
    setSessionScraps((prev) => [row, ...prev]);
    pushToast("success", tc.scrapReportedNote);
    setScrapForm({ itemSku: "", qty: "", reasonCode: "", disposition: "Disposal" });
    await load();
    await loadSide();
  }

  async function confirmClose(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    if (check?.wipRemainder !== undefined && check.wipRemainder > 0 && !residualAck.trim()) {
      setActionError(tc.residualAckRequired);
      return;
    }
    const ok = await act(
      () => closeOrder(token, order.id, residualAck.trim() || null),
      tc.closedNote + (check && check.wipRemainder > 0 ? "" : ""),
    );
    if (ok) {
      setCloseOpen(false);
      setResidualAck("");
    }
  }

  async function confirmReopen(e: FormEvent) {
    e.preventDefault();
    if (!order || !token) return;
    if (!reopenReason.trim()) {
      setActionError(tc.reopenReasonRequired);
      return;
    }
    const ok = await act(() => reopenOrder(token, order.id, reopenReason.trim()), tc.reopenedNote);
    if (ok) setReopenReason("");
  }

  // ---- derived ---------------------------------------------------------------------------
  const reportingOpen = order !== null && (order.status === "Released" || order.status === "InProcess" || order.status === "Completed");
  const isCreator = meId !== null && order?.createdBy === meId;
  const canAmend = order !== null && order.status !== "Closed" && order.status !== "Cancelled";
  const showResidual = check !== null && check.wipRemainder > 0;
  const componentOptions = (revision?.lines ?? []).map((l) => ({ value: l.componentSku, label: l.componentSku + " (" + l.unit + ")" }));
  const issueOptions = sessionIssues.map((i) => ({ value: i.id, label: i.id + " — " + i.componentSku + " × " + i.qty }));

  const checkColumns: Column<CloseCheckRow["items"][number]>[] = useMemo(() => [
    { key: "code", label: tc.checkColumns.code, mono: true, render: (i) => i.code },
    { key: "detail", label: tc.checkColumns.detail, render: (i) => i.detail },
    { key: "qty", label: tc.checkColumns.qty, mono: true, render: (i) => fmtNumber(i.qty, locale) },
  ], [tc, locale]);

  const scrapRowsColumns: Column<CloseCheckRow["scrap"][number]>[] = useMemo(() => [
    { key: "scrap", label: tc.scrapColumns.scrap, mono: true, render: (s) => s.scrapId },
    { key: "item", label: tc.scrapColumns.item, mono: true, render: (s) => s.itemSku },
    { key: "qty", label: tc.scrapColumns.qty, mono: true, render: (s) => fmtNumber(s.qty, locale) },
    { key: "unitCost", label: tc.scrapColumns.unitCost, mono: true, render: (s) => fmtMoney(s.unitCost, locale, "BDT") },
    { key: "reason", label: tc.scrapColumns.reason, mono: true, render: (s) => s.reasonCode },
    { key: "disposition", label: tc.scrapColumns.disposition, render: (s) => mfgStatusLabel(t, s.disposition) },
    { key: "value", label: tc.scrapColumns.value, mono: true, render: (s) => fmtMoney(s.value, locale, "BDT") },
    { key: "journal", label: tc.scrapColumns.journal, mono: true, render: (s) => s.journalId ?? "—" },
  ], [t, tc, locale]);

  if (!ready || phase === "loading") return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (notFound || !order) {
    return (
      <div className="stack">
        <div className="page-header"><h1 className="t-display">{tc.metaTitle}</h1></div>
        <div className="state state--error" role="alert"><p className="state__title">{tc.notFound}</p></div>
      </div>
    );
  }

  const qtyTiles = [
    { label: tc.demanded, value: order.demandedQty },
    { label: tc.planned, value: order.plannedQty },
    { label: tc.released, value: order.releasedQty },
    { label: tc.completed, value: order.completedQty },
    { label: tc.remaining, value: order.remainingQty },
  ];

  return (
    <div className="stack">
      <p className="t-caption"><Link href={`/${locale}/mfg/production-orders`}>{tc.backToList}</Link></p>
      <div className="page-header">
        <h1 className="t-display">{order.orderNo}</h1>
        <Badge tone={mfgTone(order.status)}>{mfgStatusLabel(t, order.status)}</Badge>
        <span className="t-caption mono">{order.outputSku}</span>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}

      <section className="tiles">
        {qtyTiles.map((tile) => (
          <div className="kpi" key={tile.label}>
            <p className="kpi__label">{tile.label}</p>
            <p className="kpi__value">{fmtNumber(tile.value, locale)}</p>
          </div>
        ))}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.metaTitle}</h2>
        <div className="form-grid">
          <MetaOD label={tc.orderNo} value={order.id} mono />
          <MetaOD label={tc.status} value={mfgStatusLabel(t, order.status)} />
          <MetaOD label={tc.createdBy} value={order.createdBy} mono />
          <MetaOD label={tc.plannedRevision} value={order.plannedBomRevisionId ?? tc.noRevision} mono />
          <MetaOD label={tc.releasedRevision} value={order.releasedBomRevisionId ?? tc.noRevision} mono />
          {order.cancelReason ? <MetaOD label={tc.cancelReasonLabel} value={order.cancelReason} /> : null}
          {order.closedBy ? <MetaOD label={tc.closedBy} value={order.closedBy + (order.closedAt ? " · " + fmtDay(order.closedAt, locale) : "")} mono /> : null}
          {order.reopenedBy ? <MetaOD label={tc.reopenedBy} value={order.reopenedBy + (order.reopenedAt ? " · " + fmtDay(order.reopenedAt, locale) : "")} mono /> : null}
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.lifecycleTitle}</h2>
        <p className="t-caption">{tc.releaseHint}</p>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          {legalMoves.filter((m) => m !== "Cancelled").map((target) => (
            <Button key={target} disabled={busy || !meReady} onClick={() => chooseMove(target)}>
              {target === "Planned" ? tc.plan : target === "InProcess" ? tc.start : tc.complete}
            </Button>
          ))}
          {order.status === "Draft" || order.status === "Planned" ? (
            <Button variant="danger" disabled={busy || !meReady} onClick={() => setCancelOpen(true)}>{tc.cancel}</Button>
          ) : null}
          {order.status === "Planned" && canRelease !== false && !isCreator ? (
            <Button disabled={busy || !meReady} onClick={() => setReleaseOpen(true)}>{tc.release}</Button>
          ) : null}
          {order.status === "Planned" && meId !== null && order.createdBy === meId ? (
            <p className="t-caption" role="status">{t.mfg.makerChecker}</p>
          ) : null}
          {order.status === "Completed" && canClose !== false && !isCreator ? (
            <Button disabled={busy || !meReady} onClick={() => setCloseOpen(true)}>{tc.close}</Button>
          ) : null}
          {order.status === "Completed" && meId !== null && order.createdBy === meId ? (
            <p className="t-caption" role="status">{t.mfg.makerChecker}</p>
          ) : null}
          {order.status === "Closed" && canReopen !== false ? (
            <div className="stack" style={{ inlineSize: "100%" }}>
              <Input id="od-reopen-reason" label={tc.reopenReason} value={reopenReason} onChange={(e) => setReopenReason(e.target.value)} />
              <p><Button variant="secondary" loading={busy} onClick={() => void confirmReopen({ preventDefault() { /* button inside a plain div */ } } as unknown as FormEvent)}>{tc.reopen}</Button></p>
            </div>
          ) : null}
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.amendTitle}</h2>
        <p className="t-caption">{tc.amendReasonHint}</p>
        {canAmend ? (
          <form onSubmit={(e) => void submitAmend(e)}>
            <div className="form-grid">
              <Input id="od-amend-qty" label={tc.newPlannedQty} type="number" min="0" step="any" value={amendQty} onChange={(e) => setAmendQty(e.target.value)} error={amendErrors.qty} />
              <Input id="od-amend-reason" label={tc.amendReason} value={amendReason} onChange={(e) => setAmendReason(e.target.value)} error={amendErrors.reason} />
            </div>
            <p><Button type="submit" small loading={busy} disabled={!meReady}>{tc.amend}</Button></p>
          </form>
        ) : null}
      </section>

      <section className="card">
        <div className="page-header" style={{ marginBlockEnd: 8 }}>
          <h2 className="t-title">{tc.reportingTitle}</h2>
          {order.status === "Closed" ? <Badge tone="muted">{tc.reportingFrozen}</Badge> : null}
        </div>
        <p className="t-caption">{tc.reportingHint}</p>
        {order.status === "Closed" ? <p className="t-caption">{tc.reportingFrozen}</p> : null}
        {!reportingOpen && order.status !== "Closed" ? <p className="t-caption">{tc.reportingStateBad}</p> : null}
        {reportingOpen ? (
          <div className="stack" style={{ marginBlockStart: 0 }}>
            <form onSubmit={(e) => void submitIssue(e)}>
              <h3 className="t-body-strong">{tc.issueTitle}</h3>
              {componentOptions.length === 0 ? <p className="t-caption">{tc.noReleasedLines}</p> : null}
              <div className="form-grid">
                {componentOptions.length > 0 ? (
                  <Select id="od-issue-component" label={tc.component} value={issueForm.componentSku} onChange={(e) => setIssueForm({ ...issueForm, componentSku: e.target.value })} options={componentOptions} placeholder={tc.component} error={issueErrors.component} />
                ) : (
                  <Input id="od-issue-component-input" label={tc.component} value={issueForm.componentSku} onChange={(e) => setIssueForm({ ...issueForm, componentSku: e.target.value })} error={issueErrors.component} />
                )}
                <Input id="od-issue-qty" label={tc.qty} type="number" min="0" step="any" value={issueForm.qty} onChange={(e) => setIssueForm({ ...issueForm, qty: e.target.value })} error={issueErrors.qty} />
              </div>
              <p><Button type="submit" small loading={busy} disabled={!meReady}>{tc.issue}</Button></p>
            </form>

            <form onSubmit={(e) => void submitReturn(e)}>
              <h3 className="t-body-strong">{tc.returnTitle}</h3>
              <p className="t-caption">{tc.returnHint}</p>
              {sessionIssues.length === 0 ? <p className="t-caption">{tc.noSessionIssues}</p> : null}
              <div className="form-grid">
                <Select id="od-return-issue" label={tc.issueId} value={returnForm.issueId} onChange={(e) => setReturnForm({ ...returnForm, issueId: e.target.value })} options={issueOptions} placeholder={tc.issueId} error={returnErrors.issue} hint={tc.issueSelectHint} />
                <Input id="od-return-qty" label={tc.returnQty} type="number" min="0" step="any" value={returnForm.qty} onChange={(e) => setReturnForm({ ...returnForm, qty: e.target.value })} error={returnErrors.qty} />
              </div>
              <p><Button type="submit" small loading={busy} disabled={!meReady || sessionIssues.length === 0}>{tc.submitReturn}</Button></p>
            </form>

            <form onSubmit={(e) => void submitCompletion(e)}>
              <h3 className="t-body-strong">{tc.completionTitle}</h3>
              <p className="t-caption">{tc.completeGateHint}</p>
              <div className="form-grid">
                <Input id="od-completion-qty" label={tc.completionQty} type="number" min="0" step="any" value={completionForm.qty} onChange={(e) => setCompletionForm({ ...completionForm, qty: e.target.value })} error={completionErrors.qty} />
                <Input id="od-completion-note" label={tc.note} value={completionForm.note} onChange={(e) => setCompletionForm({ ...completionForm, note: e.target.value })} />
              </div>
              <p><Button type="submit" small loading={busy} disabled={!meReady}>{tc.reportCompletion}</Button></p>
            </form>

            <form onSubmit={(e) => void submitScrap(e)}>
              <h3 className="t-body-strong">{tc.scrapTitle}</h3>
              <p className="t-caption">{tc.scrapHint}</p>
              <div className="form-grid">
                <Input id="od-scrap-sku" label={tc.itemSku} value={scrapForm.itemSku} onChange={(e) => setScrapForm({ ...scrapForm, itemSku: e.target.value })} error={scrapErrors.sku} />
                <Input id="od-scrap-qty" label={tc.qty} type="number" min="0" step="any" value={scrapForm.qty} onChange={(e) => setScrapForm({ ...scrapForm, qty: e.target.value })} error={scrapErrors.qty} />
                <Input id="od-scrap-reason" label={tc.reasonCode} value={scrapForm.reasonCode} onChange={(e) => setScrapForm({ ...scrapForm, reasonCode: e.target.value })} error={scrapErrors.reason} />
                <Select id="od-scrap-disp" label={tc.disposition} value={scrapForm.disposition} onChange={(e) => setScrapForm({ ...scrapForm, disposition: e.target.value })} options={["Disposal", "ReturnToStock"].map((d) => ({ value: d, label: mfgStatusLabel(t, d) }))} />
              </div>
              <p><Button type="submit" small loading={busy} disabled={!meReady}>{tc.reportScrap}</Button></p>
            </form>
          </div>
        ) : null}
      </section>

      <section className="card">
        <div className="page-header" style={{ marginBlockEnd: 8 }}>
          <h2 className="t-title">{tc.checkTitle}</h2>
          {check ? <Badge tone={check.open ? "warning" : "success"}>{check.open ? tc.checkOpen : tc.checkClean}</Badge> : null}
        </div>
        <p className="t-caption">{tc.checkHint}</p>
        {check === null ? <LoadingSkeleton rows={2} /> : null}
        {check ? (
          <div className="stack" style={{ marginBlockStart: 0 }}>
            <p className="t-caption">
              {tc.wipDebits}: <span className="mono">{fmtMoney(check.wipDebits, locale, "BDT")}</span> · {tc.wipCredits}: <span className="mono">{fmtMoney(check.wipCredits, locale, "BDT")}</span> · {tc.wipRemainder}: <span className="mono">{fmtMoney(check.wipRemainder, locale, "BDT")}</span>
            </p>
            {check.open ? (
              <Table columns={checkColumns} rows={check.items} caption={tc.checkTitle} />
            ) : (
              <p className="t-caption">{tc.checkClean}</p>
            )}
            <h3 className="t-body-strong">{tc.scrapRowsTitle}</h3>
            {check.scrap.length === 0 ? <p className="t-caption">{tc.noScrap}</p> : (
              <Table columns={scrapRowsColumns} rows={check.scrap} caption={tc.scrapRowsTitle} />
            )}
          </div>
        ) : null}
      </section>

      {sessionIssues.length > 0 || sessionCompletions.length > 0 || sessionScraps.length > 0 ? (
        <section className="card">
          <h2 className="t-title">{tc.sessionTitle}</h2>
          <p className="t-caption">{tc.sessionHint}</p>
          {sessionIssues.length > 0 ? (
            <Table
              columns={[
                { key: "issue", label: tc.sessionIssues.issue, mono: true, render: (r) => r.id },
                { key: "component", label: tc.sessionIssues.component, mono: true, render: (r) => r.componentSku },
                { key: "qty", label: tc.sessionIssues.qty, mono: true, render: (r) => fmtNumber(r.qty, locale) },
                { key: "value", label: tc.sessionIssues.value, mono: true, render: (r) => fmtMoney(r.value, locale, "BDT") },
                { key: "journal", label: tc.sessionIssues.journal, mono: true, render: (r) => r.journalId ?? "—" },
              ]}
              rows={sessionIssues}
              caption={tc.sessionTitle}
            />
          ) : null}

          {sessionCompletions.length > 0 ? (
            <Table
              columns={[
                { key: "completion", label: tc.sessionCompletions.completion, mono: true, render: (r) => r.id },
                { key: "qty", label: tc.sessionCompletions.qty, mono: true, render: (r) => fmtNumber(r.qty, locale) },
                { key: "note", label: tc.sessionCompletions.note, render: (r) => r.note ?? "—" },
                { key: "journal", label: tc.sessionCompletions.journal, mono: true, render: (r) => r.journalId ?? "—" },
              ]}
              rows={sessionCompletions}
              caption={tc.sessionCompletions.completion}
            />
          ) : null}
          {sessionScraps.length > 0 ? (
            <Table
              columns={[
                { key: "scrap", label: tc.sessionScraps.scrap, mono: true, render: (r) => r.id },
                { key: "item", label: tc.sessionScraps.item, mono: true, render: (r) => r.itemSku },
                { key: "qty", label: tc.sessionScraps.qty, mono: true, render: (r) => fmtNumber(r.qty, locale) },
                { key: "reason", label: tc.sessionScraps.reason, mono: true, render: (r) => r.reasonCode },
                { key: "disposition", label: tc.sessionScraps.disposition, render: (r) => mfgStatusLabel(t, r.disposition) },
                { key: "journal", label: tc.sessionScraps.journal, mono: true, render: (r) => r.journalId ?? "—" },
              ]}
              rows={sessionScraps}
              caption={tc.sessionScraps.scrap}
            />
          ) : null}
        </section>
      ) : null}

      <Modal open={cancelOpen} onClose={() => setCancelOpen(false)} title={tc.cancelConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void submitTransition("Cancelled", cancelReason.trim() || null)}>
          <p>{tc.cancelConfirm}</p>
          <Input id="od-cancel-reason" label={tc.cancelReason} value={cancelReason} onChange={(e) => setCancelReason(e.target.value)} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setCancelOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" variant="danger" loading={busy}>{tc.cancel}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={releaseOpen} onClose={() => setReleaseOpen(false)} title={tc.releaseConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.releaseConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setReleaseOpen(false)}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmRelease()}>{tc.release}</Button>
        </div>
      </Modal>

      <Modal open={closeOpen} onClose={() => setCloseOpen(false)} title={tc.closeConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmClose(e)}>
          <p>{tc.closeConfirm}</p>
          {showResidual ? (
            <div className="stack" style={{ marginBlockStart: 12 }}>
              <p className="t-caption">{tc.residualHint}</p>
              <Input id="od-residual-ack" label={tc.residualAck} value={residualAck} onChange={(e) => setResidualAck(e.target.value)} />
            </div>
          ) : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setCloseOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.close}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}

function MetaOD({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <p className="t-caption">
      <span className="t-body-strong">{label}: </span>
      <span className={mono ? "mono" : undefined}>{value}</span>
    </p>
  );
}
