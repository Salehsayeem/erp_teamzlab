"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import {
  addSupplierGate,
  fetchSupplierGates,
  fetchSupplierRows,
  procError,
  setSupplierStatus,
  type GateSeverity,
  type SupplierGate,
  type SupplierLifecycleStatus,
  type SupplierRow,
} from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { GATE_SEVERITY_TONES, hasPerm, SUPPLIER_STATUS_TONES, isoDate, statusLabel } from "../../_lib/ui";

type Phase = "loading" | "error" | "success";
type GateForm = { gateCode: string; severity: GateSeverity; effectiveFrom: string; effectiveTo: string; evidenceRef: string };

const EMPTY_GATE: GateForm = { gateCode: "", severity: "Hard", effectiveFrom: "", effectiveTo: "", evidenceRef: "" };

export default function SupplierDetailPage() {
  const params = useParams<{ locale: string; supplierId: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const supplierId = params.supplierId;
  const t = getDictionary(locale);
  const d = t.procurement.supplierDetail;
  const { token, ready } = useAuth(locale);

  const [supplier, setSupplier] = useState<SupplierRow | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [meId, setMeId] = useState<string | null>(null);
  const [canWrite, setCanWrite] = useState<boolean | null>(null);

  const [statusDraft, setStatusDraft] = useState<SupplierLifecycleStatus>("Active");
  const [reason, setReason] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [savingStatus, setSavingStatus] = useState(false);

  const [gates, setGates] = useState<SupplierGate[]>([]);
  const [gatesRead, setGatesRead] = useState(false);
  const [gateForm, setGateForm] = useState<GateForm>(EMPTY_GATE);
  const [gateErrors, setGateErrors] = useState<Partial<Record<keyof GateForm, string>>>({});
  const [gateError, setGateError] = useState<string | null>(null);
  const [addingGate, setAddingGate] = useState(false);
  const [exceptionFor, setExceptionFor] = useState<string | null>(null);
  const [exceptionUntil, setExceptionUntil] = useState("");
  const [exceptionError, setExceptionError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (!cancelled && res.ok) {
        setMeId(res.data.user.id);
        setCanWrite(hasPerm(res.data.permissions, "suppliers:write"));
      } else setCanWrite(false);
    });
    return () => { cancelled = true; };
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const listRes = await fetchSupplierRows(token);
    const row = listRes.ok ? listRes.data.find((s) => s.id === supplierId) : undefined;
    if (!row) {
      setSupplier(null);
      setPhase(listRes.ok ? "error" : "error");
      return;
    }
    setSupplier(row);
    setPhase("success");
    // Gates read (landing in parallel) — merged into the in-session list when it answers.
    const gatesRes = await fetchSupplierGates(token, supplierId);
    if (gatesRes.ok) {
      setGates(gatesRes.data);
      setGatesRead(true);
    }
  }, [token, supplierId]);
  useEffect(() => { void load(); }, [load]);

  async function submitStatus(e: FormEvent) {
    e.preventDefault();
    if (!token || !supplier) return;
    if (!reason.trim()) {
      setReasonError(t.procurement.common.actionFailed);
      return;
    }
    setSavingStatus(true);
    setStatusError(null);
    const res = await setSupplierStatus(token, supplier.id, statusDraft, reason.trim());
    setSavingStatus(false);
    if (!res.ok) {
      setStatusError(procError(res.code, res.message, { FORBIDDEN: t.procurement.common.forbidden }, d.lifecycle.statusFailed));
      return;
    }
    setSupplier(res.data);
    setReason("");
    setReasonError(null);
  }

  async function submitGate(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: Partial<Record<keyof GateForm, string>> = {};
    if (!gateForm.gateCode.trim()) errs.gateCode = d.gates.errors.codeRequired;
    if (!gateForm.effectiveFrom) errs.effectiveFrom = d.gates.errors.fromRequired;
    if (gateForm.effectiveTo && gateForm.effectiveTo < gateForm.effectiveFrom) errs.effectiveTo = d.gates.errors.toInvalid;
    setGateErrors(errs);
    setGateError(null);
    if (Object.keys(errs).length > 0) return;
    setAddingGate(true);
    const res = await addSupplierGate(token, supplierId, {
      gateCode: gateForm.gateCode.trim(),
      severity: gateForm.severity,
      effectiveFrom: gateForm.effectiveFrom,
      effectiveTo: gateForm.effectiveTo || null,
      evidenceRef: gateForm.evidenceRef.trim() || null,
    });
    setAddingGate(false);
    if (!res.ok) {
      const mapped: Record<string, string> = { GATE_DUPLICATE: d.gates.duplicate, FORBIDDEN: t.procurement.common.forbidden };
      setGateError(procError(res.code, res.message, mapped, d.gates.addFailed));
      return;
    }
    setGates((prev) => [res.data, ...prev.filter((g) => g.id !== res.data.id)]);
    setGateForm(EMPTY_GATE);
  }

  async function grantException(gate: SupplierGate) {
    if (!token) return;
    if (!exceptionUntil) {
      setExceptionError(d.gates.errors.exceptionRequired);
      return;
    }
    setExceptionError(null);
    const res = await addSupplierGate(token, supplierId, {
      gateCode: gate.gateCode,
      severity: gate.severity,
      effectiveFrom: gate.effectiveFrom,
      effectiveTo: gate.effectiveTo,
      evidenceRef: gate.evidenceRef,
      exceptionUntil: new Date(exceptionUntil).toISOString(),
    });
    if (!res.ok) {
      const mapped: Record<string, string> = { GATE_DUPLICATE: d.gates.duplicate, FORBIDDEN: t.procurement.common.forbidden };
      setExceptionError(procError(res.code, res.message, mapped, d.gates.exceptionFailed));
      return;
    }
    setGates((prev) => [res.data, ...prev.filter((g) => g.id !== res.data.id)]);
    setExceptionFor(null);
    setExceptionUntil("");
  }

  const gateColumns: Column<SupplierGate>[] = [
    { key: "gateCode", label: d.gates.columns.gateCode, mono: true, render: (g) => g.gateCode },
    { key: "severity", label: d.gates.columns.severity, render: (g) => <Badge tone={GATE_SEVERITY_TONES[g.severity] ?? "muted"}>{statusLabel(t, g.severity)}</Badge> },
    { key: "effective", label: d.gates.columns.effective, mono: true, render: (g) => `${isoDate(g.effectiveFrom)} → ${g.effectiveTo ? isoDate(g.effectiveTo) : "—"}` },
    { key: "evidence", label: d.gates.columns.evidence, mono: true, render: (g) => g.evidenceRef ?? "—" },
    {
      key: "exception",
      label: d.gates.columns.exception,
      render: (g) =>
        g.exceptionUntil ? (
          <Badge tone="warning">{fmtDate(g.exceptionUntil, locale)}</Badge>
        ) : canWrite === true ? (
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button small variant="secondary" onClick={() => { setExceptionFor(exceptionFor === g.id ? null : g.id); setExceptionUntil(""); setExceptionError(null); }}>
              {d.gates.grantException}
            </Button>
          </div>
        ) : null,
    },
  ];

  if (!ready || phase === "loading") return <LoadingSkeleton rows={5} />;
  if (phase === "error" || supplier === null) {
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{d.loadFailed}</h1>
          <p className="t-caption"><Link href={`/${locale}/procurement/suppliers`}>{d.backToList}</Link></p>
        </div>
        <ErrorState message={d.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />
      </div>
    );
  }

  const blocking = supplier.status === "Suspended" || supplier.status === "Blocked";

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{supplier.name}</h1>
        <p className="t-caption"><Link href={`/${locale}/procurement/suppliers`}>{d.backToList}</Link></p>
      </div>
      {statusError ? <p className="banner" role="alert">{statusError}</p> : null}
      {gateError ? <p className="banner" role="alert">{gateError}</p> : null}
      {exceptionError ? <p className="banner" role="alert">{exceptionError}</p> : null}

      <section className="card">
        <h2 className="t-title">{d.identity.title}</h2>
        <div className="form-grid">
          <p className="t-caption">{d.identity.name}: {supplier.name}</p>
          <p className="t-caption">{d.identity.taxId}: <span className="mono">{supplier.taxId}</span></p>
          <p className="t-caption">{d.identity.email}: {supplier.email}</p>
          <p className="t-caption">{d.identity.created}: {fmtDate(supplier.createdAt, locale)}</p>
          <p className="t-caption mono">{supplier.id}</p>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">
          {d.lifecycle.title}{" "}
          <Badge tone={blocking ? "danger" : SUPPLIER_STATUS_TONES[supplier.status] ?? "muted"}>{statusLabel(t, supplier.status)}</Badge>
        </h2>
        {blocking ? <p className="banner" role="status">{d.lifecycle.blocking}</p> : null}
        {canWrite === false ? null : (
          <form onSubmit={(e) => void submitStatus(e)}>
            <div className="form-grid">
              <Select
                id="sup-status"
                label={d.lifecycle.status}
                options={(["Active", "Pending", "Suspended", "Blocked", "Inactive"] as SupplierLifecycleStatus[]).map((v) => ({ value: v, label: statusLabel(t, v) }))}
                value={statusDraft}
                onChange={(e) => setStatusDraft(e.target.value as SupplierLifecycleStatus)}
              />
              <Input
                id="sup-reason"
                label={d.lifecycle.reason}
                hint={d.lifecycle.reasonHint}
                value={reason}
                onChange={(e) => { setReason(e.target.value); setReasonError(null); }}
                error={reasonError ?? undefined}
              />
            </div>
            <p><Button type="submit" loading={savingStatus} disabled={meId === null}>{d.lifecycle.update}</Button></p>
          </form>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{d.gates.title}</h2>
        <p className="t-caption">{d.gates.sessionNote}</p>
        {gates.length === 0 ? (
          <EmptyState title={d.gates.empty} />
        ) : (
          <>
            <Table<SupplierGate> columns={gateColumns} rows={gates} caption={d.gates.title} />
            {gates.map((g) =>
              exceptionFor === g.id ? (
                <div className="stack" key={`exc-${g.id}`}>
                  <p className="t-caption">{d.gates.exceptionHint}</p>
                  <Input
                    id={`exc-${g.id}`}
                    label={d.gates.exceptionUntil}
                    type="datetime-local"
                    value={exceptionUntil}
                    onChange={(e) => { setExceptionUntil(e.target.value); setExceptionError(null); }}
                    error={exceptionError ?? undefined}
                  />
                  <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                    <Button small onClick={() => void grantException(g)} disabled={!exceptionUntil}>{d.gates.grantException}</Button>
                    <Button small variant="secondary" onClick={() => setExceptionFor(null)}>{t.common.cancel}</Button>
                  </div>
                </div>
              ) : null,
            )}
          </>
        )}
        {canWrite === false ? null : (
          <form onSubmit={(e) => void submitGate(e)}>
            <h3 className="t-body-strong">{d.gates.addTitle}</h3>
            <div className="form-grid">
              <Input id="g-code" label={d.gates.gateCode} value={gateForm.gateCode} onChange={(e) => setGateForm({ ...gateForm, gateCode: e.target.value })} error={gateErrors.gateCode} />
              <Select
                id="g-severity"
                label={d.gates.severity}
                options={(["Hard", "Warning"] as GateSeverity[]).map((v) => ({ value: v, label: v === "Hard" ? d.gates.severityHard : d.gates.severityWarning }))}
                value={gateForm.severity}
                onChange={(e) => setGateForm({ ...gateForm, severity: e.target.value as GateSeverity })}
              />
              <Input id="g-from" label={d.gates.effectiveFrom} type="date" value={gateForm.effectiveFrom} onChange={(e) => setGateForm({ ...gateForm, effectiveFrom: e.target.value })} error={gateErrors.effectiveFrom} />
              <Input id="g-to" label={d.gates.effectiveTo} type="date" value={gateForm.effectiveTo} onChange={(e) => setGateForm({ ...gateForm, effectiveTo: e.target.value })} error={gateErrors.effectiveTo} />
              <Input id="g-evidence" label={d.gates.evidenceRef} value={gateForm.evidenceRef} onChange={(e) => setGateForm({ ...gateForm, evidenceRef: e.target.value })} />
            </div>
            <p><Button type="submit" loading={addingGate} disabled={meId === null}>{d.gates.addTitle}</Button></p>
          </form>
        )}
      </section>
    </div>
  );
}
