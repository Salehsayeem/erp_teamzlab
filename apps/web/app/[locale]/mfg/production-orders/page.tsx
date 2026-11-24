"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createProductionOrder, fetchBoms, fetchProductionOrders, type BomRow, type ProductionOrderRow } from "@/lib/services/mfg";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { fmtDay, hasPermission, mfgActionError, mfgStatusLabel, mfgTone, useMfgMe } from "../_lib/helpers";

const ORDER_STATUSES = ["Draft", "Planned", "Released", "InProcess", "Completed", "Closed", "Cancelled"];

export default function ProductionOrdersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.orders;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = useMfgMe(token);
  const canWrite = hasPermission(permissions, "mfg:write");

  const [orders, setOrders] = useState<ProductionOrderRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("");

  const [boms, setBoms] = useState<BomRow[]>([]);
  const [form, setForm] = useState({ outputSku: "", demandedQty: "", unit: "", overTolerancePct: "0" });
  const [errors, setErrors] = useState<{ sku?: string; qty?: string; unit?: string; tolerance?: string }>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
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
    const res = await fetchProductionOrders(token, statusFilter || undefined);
    if (res.ok) {
      setOrders(res.data);
      setPhase("success");
    } else {
      setOrders(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, statusFilter]);
  useEffect(() => { void load(); }, [load]);

  // BOM picker: approved revisions only — the release gate needs an effective recipe (014 AC2)
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchBoms(token, undefined, "Approved").then((res) => { if (!cancelled && res.ok) setBoms(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  const skuOptions = useMemo(() => {
    const seen = new Set<string>();
    for (const b of boms) seen.add(b.outputSku);
    return [...seen].map((sku) => ({ value: sku, label: sku }));
  }, [boms]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: { sku?: string; qty?: string; unit?: string; tolerance?: string } = {};
    if (!form.outputSku.trim()) errs.sku = tc.errors.skuRequired;
    const qty = Number(form.demandedQty);
    if (!form.demandedQty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!form.unit.trim()) errs.unit = tc.errors.unitRequired;
    const tol = Number(form.overTolerancePct);
    if (!form.overTolerancePct.trim() || !Number.isFinite(tol) || tol < 0) errs.tolerance = tc.errors.toleranceInvalid;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createProductionOrder(token, {
      outputSku: form.outputSku.trim(), demandedQty: qty, unit: form.unit.trim(), overTolerancePct: tol,
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(mfgActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.id);
    setOrders((prev) => (prev ? [res.data, ...prev] : prev));
    setForm({ outputSku: "", demandedQty: "", unit: "", overTolerancePct: "0" });
  }

  const columns: Column<ProductionOrderRow>[] = useMemo(() => [
    {
      key: "order", label: tc.columns.order, mono: true,
      render: (o) => <Link href={`/${locale}/mfg/production-orders/${o.id}`}>{o.orderNo}</Link>,
    },
    { key: "sku", label: tc.columns.outputSku, mono: true, render: (o) => o.outputSku },
    { key: "status", label: tc.columns.status, render: (o) => <Badge tone={mfgTone(o.status)}>{mfgStatusLabel(t, o.status)}</Badge> },
    { key: "demanded", label: tc.columns.demanded, mono: true, render: (o) => fmtNumber(o.demandedQty, locale) + " " + o.unit },
    { key: "planned", label: tc.columns.planned, mono: true, render: (o) => fmtNumber(o.plannedQty, locale) },
    { key: "completed", label: tc.columns.completed, mono: true, render: (o) => fmtNumber(o.completedQty, locale) },
    { key: "remaining", label: tc.columns.remaining, mono: true, render: (o) => fmtNumber(o.remainingQty, locale) },
  ], [t, tc, locale]);

  if (!ready || (phase === "loading" && orders === null)) return <LoadingSkeleton rows={6} />;
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

  const list = orders ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        <p className="t-caption">{tc.outputSkuHint}</p>
        {boms.length === 0 ? <p className="t-caption">{tc.noBoms}</p> : null}
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            {skuOptions.length > 0 ? (
              <Select
                id="po-sku" label={tc.outputSku} value={form.outputSku}
                onChange={(e) => setForm({ ...form, outputSku: e.target.value, unit: form.unit || (boms.find((b) => b.outputSku === e.target.value)?.lines[0]?.unit ?? "") })}
                options={skuOptions} placeholder={tc.outputSku} error={errors.sku}
              />
            ) : (
              <Input id="po-sku-input" label={tc.outputSku} value={form.outputSku} onChange={(e) => setForm({ ...form, outputSku: e.target.value })} hint={tc.noBomMatch} error={errors.sku} />
            )}
            <Input id="po-qty" label={tc.demandedQty} type="number" min="0" step="any" value={form.demandedQty} onChange={(e) => setForm({ ...form, demandedQty: e.target.value })} error={errors.qty} />
            <Input id="po-unit" label={tc.unit} value={form.unit} onChange={(e) => setForm({ ...form, unit: e.target.value })} error={errors.unit} />
            <Input id="po-tol" label={tc.overTolerance} type="number" min="0" step="any" value={form.overTolerancePct} onChange={(e) => setForm({ ...form, overTolerancePct: e.target.value })} hint={tc.overToleranceHint} error={errors.tolerance} />
          </div>
          <p><Button type="submit" loading={creating} disabled={!meReady}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        <div className="form-grid">
          <Select id="po-status-filter" label={tc.statusFilter} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} options={ORDER_STATUSES.map((s) => ({ value: s, label: mfgStatusLabel(t, s) }))} placeholder={t.mfg.statusFilterAll} />
        </div>
        {list.length === 0 ? <EmptyState title={statusFilter ? tc.noMatch : tc.empty} /> : (
          <Table columns={columns} rows={list} caption={tc.title} />
        )}
      </section>
    </div>
  );
}
