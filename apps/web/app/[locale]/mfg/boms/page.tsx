"use client";

import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  addBomLine,
  approveBom,
  createBom,
  fetchBoms,
  retireBom,
  type BomRow,
} from "@/lib/services/mfg";
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
import { fmtDay, hasPermission, mfgActionError, mfgStatusLabel, mfgTone, useMfgMe } from "../_lib/helpers";

const BOM_STATUSES = ["Draft", "Approved", "Retired"];
const ISSUE_POLICIES = ["Manual", "Backflush"];

export default function BomsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.boms;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady } = useMfgMe(token);
  const canApprove = hasPermission(permissions, "boms:approve");
  const canWrite = hasPermission(permissions, "boms:write");

  const [boms, setBoms] = useState<BomRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const [form, setForm] = useState({ outputSku: "", revision: "1", outputQtyPer: "1", effectiveFrom: "", effectiveTo: "" });
  const [errors, setErrors] = useState<{ sku?: string; revision?: string; qty?: string; from?: string; to?: string }>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const [lineFor, setLineFor] = useState<string | null>(null);
  const [lineForm, setLineForm] = useState({ componentSku: "", qtyPer: "", unit: "", issuePolicy: "Manual", unitCost: "" });
  const [lineErrors, setLineErrors] = useState<{ component?: string; qty?: string; unit?: string; cost?: string }>({});
  const [addingLine, setAddingLine] = useState(false);

  const [approveId, setApproveId] = useState<BomRow | null>(null);
  const [retireId, setRetireId] = useState<BomRow | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
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
    setErrorCode(null);
    const res = await fetchBoms(token, undefined, statusFilter || undefined);
    if (res.ok) {
      setBoms(res.data);
      setPhase("success");
    } else {
      setBoms(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  async function submitBom(e: FormEvent) {
    e.preventDefault();
    const errs: { sku?: string; revision?: string; qty?: string; from?: string; to?: string } = {};
    if (!form.outputSku.trim()) errs.sku = tc.errors.skuRequired;
    const rev = Number(form.revision);
    if (!form.revision.trim() || !Number.isInteger(rev) || rev < 1) errs.revision = tc.errors.revisionInvalid;
    const qty = Number(form.outputQtyPer);
    if (!form.outputQtyPer.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!form.effectiveFrom) errs.from = tc.errors.fromRequired;
    if (form.effectiveTo && form.effectiveTo < form.effectiveFrom) errs.to = tc.errors.toInvalid;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createBom(token, {
      outputSku: form.outputSku.trim(), revision: rev, outputQtyPer: qty,
      effectiveFrom: form.effectiveFrom, effectiveTo: form.effectiveTo || null,
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(mfgActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.id);
    setBoms((prev) => (prev ? [res.data, ...prev] : prev));
    setForm({ outputSku: "", revision: "1", outputQtyPer: "1", effectiveFrom: "", effectiveTo: "" });
  }


  async function submitLine(e: FormEvent) {
    e.preventDefault();
    if (!lineFor || !token) return;
    const qty = Number(lineForm.qtyPer);
    const cost = Number(lineForm.unitCost);
    const errs: { component?: string; qty?: string; unit?: string; cost?: string } = {};
    if (!lineForm.componentSku.trim()) errs.component = tc.errors.componentRequired;
    if (!lineForm.qtyPer.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!lineForm.unit.trim()) errs.unit = tc.errors.unitRequired;
    if (!lineForm.unitCost.trim() || !Number.isFinite(cost) || cost < 0) errs.cost = tc.errors.costInvalid;
    setLineErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setAddingLine(true);
    setActionError(null);
    const res = await addBomLine(token, lineFor, {
      componentSku: lineForm.componentSku.trim(), qtyPer: qty, unit: lineForm.unit.trim(),
      issuePolicy: lineForm.issuePolicy, unitCost: cost,
    });
    setAddingLine(false);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.lineAdded);
    const updated = res.data;
    setBoms((prev) => (prev ? prev.map((b) => (b.id === lineFor ? { ...b, lines: [...b.lines, updated] } : b)) : prev));
    setLineForm({ componentSku: "", qtyPer: "", unit: "", issuePolicy: "Manual", unitCost: "" });
    setLineFor(null);
  }

  async function decide(kind: "approve" | "retire", row: BomRow) {
    if (!token) return;
    setBusyId(row.id);
    setActionError(null);
    const res = kind === "approve"
      ? await approveBom(token, row.id, null)
      : await retireBom(token, row.id, null);
    setBusyId(null);
    if (!res.ok) {
      setActionError(mfgActionError(t, res.code, res.message));
      return;
    }
    const updated = res.data;
    setBoms((prev) => (prev ? prev.map((b) => (b.id === updated.id ? updated : b)) : prev));
    pushToast("success", kind === "approve" ? tc.approvedNote : tc.retiredNote);
    setApproveId(null);
    setRetireId(null);
  }

  const isAuthor = (b: BomRow) => meId !== null && b.createdBy === meId;
  const showApprove = (b: BomRow) => b.status === "Draft" && canApprove !== false && !isAuthor(b);

  const lineColumns: Column<BomRow["lines"][number]>[] = useMemo(() => [
    { key: "component", label: tc.lineColumns.component, mono: true, render: (l) => l.componentSku },
    { key: "qtyPer", label: tc.lineColumns.qtyPer, mono: true, render: (l) => fmtNumber(l.qtyPer, locale) },
    { key: "unit", label: tc.lineColumns.unit, render: (l) => l.unit },
    { key: "policy", label: tc.lineColumns.policy, render: (l) => <Badge tone={mfgTone(l.issuePolicy)}>{mfgStatusLabel(t, l.issuePolicy)}</Badge> },
    { key: "unitCost", label: tc.lineColumns.unitCost, mono: true, render: (l) => fmtMoney(l.unitCost, locale, "BDT") },
  ], [t, tc, locale]);

  const columns: Column<BomRow>[] = useMemo(() => [
    {
      key: "bom", label: tc.columns.bom, mono: true,
      render: (b) => (
        <Fragment>
          <button
            type="button"
            style={{ padding: 0, background: "none", border: "none", color: "inherit", font: "inherit", cursor: "pointer", textDecoration: "underline" }}
            aria-expanded={expandedId === b.id}
            onClick={() => setExpandedId(expandedId === b.id ? null : b.id)}
          >
            {b.outputSku + " · r" + b.revision}
          </button>
          <span className="t-caption">{" — " + b.id}</span>
        </Fragment>
      ),
    },
  ], [t, tc, locale, expandedId]);

  const columns2: Column<BomRow>[] = useMemo(() => [
    ...columns,
    { key: "status", label: tc.columns.status, render: (b) => <Badge tone={mfgTone(b.status)}>{mfgStatusLabel(t, b.status)}</Badge> },
    { key: "batch", label: tc.columns.batch, mono: true, render: (b) => fmtNumber(b.outputQtyPer, locale) },
    { key: "effective", label: tc.columns.effective, render: (b) => (
      <span className="t-caption">{fmtDay(b.effectiveFrom, locale)} … {b.effectiveTo ? fmtDay(b.effectiveTo, locale) : tc.effectiveOpen}</span>
    ) },
    { key: "lines", label: tc.columns.lines, mono: true, render: (b) => String(b.lines.length) },
    {
      key: "actions", label: "",
      render: (b) => (
        <div className="row-actions">
          <Button small variant="secondary" onClick={() => setExpandedId(expandedId === b.id ? null : b.id)}>
            {expandedId === b.id ? tc.hideLines : tc.showLines}
          </Button>
          {b.status === "Draft" && canWrite !== false ? (
            <Button small variant="secondary" disabled={!meReady || busyId !== null} onClick={() => {
              if (lineFor === b.id) { setLineFor(null); return; }
              setLineFor(b.id);
              setLineErrors({});
            }}>{tc.addLine}</Button>
          ) : null}
          {showApprove(b) ? (
            <Button small disabled={!meReady || busyId !== null} onClick={() => setApproveId(b)}>{tc.approve}</Button>
          ) : null}
          {b.status === "Draft" && isAuthor(b) ? (
            <p className="t-caption" role="status">{t.mfg.makerChecker}</p>
          ) : null}
          {b.status === "Approved" && canWrite !== false ? (
            <Button small variant="danger" disabled={!meReady || busyId !== null} onClick={() => setRetireId(b)}>{tc.retire}</Button>
          ) : null}
        </div>
      ),
    },
  ], [t, tc, locale, expandedId, columns, canWrite, meReady, busyId, lineFor, showApprove, isAuthor]);

  if (!ready || (phase === "loading" && boms === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    if (errorCode === "FORBIDDEN" || errorCode === "HTTP_403") {
      return (
        <div className="stack">
          <div className="page-header">
            <h1 className="t-display">{tc.title}</h1>
            <p className="t-caption">{tc.subtitle}</p>
          </div>
          <div className="state state--error" role="alert"><p className="state__title">{tc.forbidden}</p></div>
        </div>
      );
    }
    return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  }

  const list = boms ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        <p className="t-caption">{tc.effectiveHint}</p>
        <form onSubmit={(e) => void submitBom(e)}>
          <div className="form-grid">
            <Input id="bom-sku" label={tc.outputSku} value={form.outputSku} onChange={(e) => setForm({ ...form, outputSku: e.target.value })} error={errors.sku} />
            <Input id="bom-rev" label={tc.revision} type="number" min="1" step="1" value={form.revision} onChange={(e) => setForm({ ...form, revision: e.target.value })} error={errors.revision} />
            <Input id="bom-qty" label={tc.outputQtyPer} type="number" min="0" step="any" value={form.outputQtyPer} onChange={(e) => setForm({ ...form, outputQtyPer: e.target.value })} error={errors.qty} />
            <Input id="bom-from" label={tc.effectiveFrom} type="date" value={form.effectiveFrom} onChange={(e) => setForm({ ...form, effectiveFrom: e.target.value })} error={errors.from} />
            <Input id="bom-to" label={tc.effectiveTo} type="date" value={form.effectiveTo} onChange={(e) => setForm({ ...form, effectiveTo: e.target.value })} error={errors.to} />
          </div>
          <p><Button type="submit" loading={creating}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        <div className="form-grid">
          <Select id="bom-status-filter" label={tc.statusFilter} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} options={BOM_STATUSES.map((s) => ({ value: s, label: mfgStatusLabel(t, s) }))} placeholder={t.mfg.statusFilterAll} />
        </div>
        {list.length === 0 ? <EmptyState title={statusFilter ? tc.noMatch : tc.empty} /> : (
          <Table columns={columns2} rows={list} caption={tc.title} />
        )}
        {list.length > 0 && expandedId !== null ? (
          <LinePanel
            bom={list.find((b) => b.id === expandedId) ?? null}
            lineFor={lineFor}
            lineForm={lineForm}
            lineErrors={lineErrors}
            addingLine={addingLine}
            lineColumns={lineColumns}
            tc={tc}
            t={t}
            onSubmit={(e) => void submitLine(e)}
            onField={(patch) => setLineForm({ ...lineForm, ...patch })}
            onCancel={() => setLineFor(null)}
          />
        ) : null}
      </section>

      <Modal open={approveId !== null} onClose={() => setApproveId(null)} title={tc.approveConfirmTitle} closeLabel={t.common.cancel} busy={busyId !== null}>
        <p>{tc.approveConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setApproveId(null)}>{t.common.cancel}</Button>
          <Button loading={busyId !== null} onClick={() => { if (approveId) void decide("approve", approveId); }}>{tc.approve}</Button>
        </div>
      </Modal>

      <Modal open={retireId !== null} onClose={() => setRetireId(null)} title={tc.retireConfirmTitle} closeLabel={t.common.cancel} busy={busyId !== null}>
        <p>{tc.retireConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setRetireId(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={busyId !== null} onClick={() => { if (retireId) void decide("retire", retireId); }}>{tc.retire}</Button>
        </div>
      </Modal>
    </div>
  );
}

function LinePanel({ bom, lineFor, lineForm, lineErrors, addingLine, lineColumns, tc, t, onSubmit, onField, onCancel }: {
  bom: BomRow | null;
  lineFor: string | null;
  lineForm: { componentSku: string; qtyPer: string; unit: string; issuePolicy: string; unitCost: string };
  lineErrors: { component?: string; qty?: string; unit?: string; cost?: string };
  addingLine: boolean;
  lineColumns: Column<BomRow["lines"][number]>[];
  tc: ReturnType<typeof getDictionary>["mfg"]["boms"];
  t: ReturnType<typeof getDictionary>;
  onSubmit: (e: FormEvent) => void;
  onField: (patch: Partial<{ componentSku: string; qtyPer: string; unit: string; issuePolicy: string; unitCost: string }>) => void;
  onCancel: () => void;
}) {
  if (!bom) return null;
  return (
    <div className="stack" style={{ marginBlockStart: 12 }}>
      <Table columns={lineColumns} rows={bom.lines} caption={bom.id} />
      {bom.lines.length === 0 ? <p className="t-caption">{tc.noLines}</p> : null}
      {bom.status === "Draft" && lineFor === bom.id ? (
        <form onSubmit={onSubmit}>
          <h3 className="t-body-strong">{tc.lineFormTitle}</h3>
          <p className="t-caption">{tc.lineFormHint}</p>
          <div className="form-grid">
            <Input id="bl-component" label={tc.componentSku} value={lineForm.componentSku} onChange={(e) => onField({ componentSku: e.target.value })} error={lineErrors.component} />
            <Input id="bl-qty" label={tc.qtyPer} type="number" min="0" step="any" value={lineForm.qtyPer} onChange={(e) => onField({ qtyPer: e.target.value })} error={lineErrors.qty} />
            <Input id="bl-unit" label={tc.unit} value={lineForm.unit} onChange={(e) => onField({ unit: e.target.value })} error={lineErrors.unit} />
            <Select id="bl-policy" label={tc.issuePolicy} value={lineForm.issuePolicy} onChange={(e) => onField({ issuePolicy: e.target.value })} options={["Manual", "Backflush"].map((p) => ({ value: p, label: mfgStatusLabel(t, p) }))} hint={tc.issuePolicyHint} />
            <Input id="bl-cost" label={tc.unitCost} type="number" min="0" step="any" value={lineForm.unitCost} onChange={(e) => onField({ unitCost: e.target.value })} error={lineErrors.cost} />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={addingLine}>{tc.addLine}</Button>
            <Button type="button" variant="secondary" small onClick={onCancel}>{t.common.cancel}</Button>
          </div>
        </form>
      ) : null}
    </div>
  );
}
