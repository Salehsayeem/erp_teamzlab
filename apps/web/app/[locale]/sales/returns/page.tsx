"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createSalesReturn,
  fetchSalesOrders,
  listSalesReturns,
  type SalesOrderRow,
  type SalesReturnRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { hasPermission, salesActionError, salesTone, salesStatusLabel, SalesMeError, useSalesMe } from "../_lib/helpers";
import { localStatus } from "../_lib/deferred";
import { fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";

type ReturnLineForm = { itemSku: string; qty: string; reason: string; condition: string };
const EMPTY_LINE: ReturnLineForm = { itemSku: "", qty: "", reason: "", condition: "Sellable" };

const CONDITIONS = ["Sellable", "Damaged", "Defective"];

export default function ReturnsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.returns;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [rows, setRows] = useState<SalesReturnRow[] | null>(null);
  const [orders, setOrders] = useState<SalesOrderRow[]>([]);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");

  const [orderId, setOrderId] = useState("");
  const [lines, setLines] = useState<ReturnLineForm[]>([{ ...EMPTY_LINE }]);
  const [override, setOverride] = useState("");
  const [errors, setErrors] = useState<{ order?: string; lines?: string }>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [overrideHint, setOverrideHint] = useState(false);
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
    const res = await listSalesReturns(token);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchSalesOrders(token).then((res) => { if (!cancelled && res.ok) setOrders(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  function setLine(i: number, patch: Partial<ReturnLineForm>) {
    setLines((prev) => prev.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)));
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { order?: string; lines?: string } = {};
    if (!orderId) errs.order = tc.orderRequired;
    const valid = lines.filter((ln) => ln.itemSku.trim() && Number(ln.qty) > 0 && Number.isFinite(Number(ln.qty)) && ln.reason.trim());
    if (valid.length === 0) errs.lines = tc.errors.lineRequired;
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setCreating(true);
    setCreateError(null);
    const res = await createSalesReturn(token, {
      orderId,
      lines: valid.map((ln) => ({ itemSku: ln.itemSku.trim(), qty: Number(ln.qty), reason: ln.reason.trim(), condition: ln.condition })),
      ...(override.trim() ? { overrideJustification: override.trim() } : {}),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(salesActionError(t, res.code, res.message));
      if (res.code === "RETURN_QTY_EXCEEDED") setOverrideHint(true);
      return;
    }
    pushToast("success", tc.created + " " + res.data.id);
    setOrderId("");
    setLines([{ ...EMPTY_LINE }]);
    setOverride("");
    setOverrideHint(false);
    await load();
  }

  const writeGate = hasPermission(permissions, "returns:write");
  const shippableOrders = orders.filter((o) => o.status === "Shipped" || o.status === "Invoiced");

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = rows ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        {shippableOrders.length === 0 ? (
          <p className="t-caption">{tc.noOrders}</p>
        ) : (
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Select
                id="rtn-order"
                label={tc.order}
                value={orderId}
                onChange={(e) => setOrderId(e.target.value)}
                options={shippableOrders.map((o) => ({ value: o.id, label: o.id + " (" + salesStatusLabel(t, o.status) + ")" }))}
                placeholder={tc.order}
                error={errors.order}
              />
            </div>
            {lines.map((ln, i) => (
              <div className="form-grid" key={i}>
                <Input id={`rtn-sku-${i}`} label={`${tc.line.sku} ${i + 1}`} value={ln.itemSku} onChange={(e) => setLine(i, { itemSku: e.target.value })} />
                <Input id={`rtn-qty-${i}`} label={`${tc.line.qty} ${i + 1}`} type="number" min="0" step="any" value={ln.qty} onChange={(e) => setLine(i, { qty: e.target.value })} />
                <Input id={`rtn-reason-${i}`} label={`${tc.line.reason} ${i + 1}`} value={ln.reason} onChange={(e) => setLine(i, { reason: e.target.value })} />
                <Select
                  id={`rtn-cond-${i}`}
                  label={tc.line.condition}
                  value={ln.condition}
                  onChange={(e) => setLine(i, { condition: e.target.value })}
                  options={CONDITIONS.map((c) => ({ value: c, label: c }))}
                />
                <div className="field">
                  <Button type="button" variant="secondary" small aria-label={`${tc.removeLine} ${i + 1}`} disabled={lines.length === 1} onClick={() => setLines((prev) => prev.filter((_, idx) => idx !== i))}>
                    {tc.removeLine}
                  </Button>
                </div>
              </div>
            ))}
            {errors.lines ? <p className="field__error">{errors.lines}</p> : null}
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button type="button" variant="secondary" small onClick={() => setLines((prev) => [...prev, { ...EMPTY_LINE }])}>{tc.addLine}</Button>
            </div>
            {overrideHint ? (
              <>
                <Input id="rtn-override" label={tc.overrideJustification} hint={tc.overrideHint} value={override} onChange={(e) => setOverride(e.target.value)} />
                {createError ? <p className="banner" role="alert">{createError}</p> : null}
              </>
            ) : createError ? (
              <p className="banner" role="alert">{createError}</p>
            ) : null}
            <p><Button type="submit" loading={creating} disabled={writeGate === false}>{tc.create}</Button></p>
          </form>
        )}
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.return}</th>
                  <th scope="col">{tc.columns.order}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.authorized}</th>
                  <th scope="col">{tc.columns.received}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((r) => (
                  <tr key={r.id}>
                    <td data-label={tc.columns.return} className="mono"><Link href={"/" + locale + "/sales/returns/" + r.id}>{r.id}</Link></td>
                    <td data-label={tc.columns.order} className="mono">{r.salesOrderId}</td>
                    <td data-label={tc.columns.customer} className="mono">{r.customerId}</td>
                    <td data-label={tc.columns.status}><Badge tone={salesTone(r.status)}>{localStatus(t, tc.statusLabels, r.status)}</Badge></td>
                    <td data-label={tc.columns.authorized} className="mono">{fmtNumber(r.authorizedQty, locale)}</td>
                    <td data-label={tc.columns.received} className="mono">{fmtNumber(r.receivedQty, locale)}</td>
                    <td data-label={t.journals.actions}><Link href={"/" + locale + "/sales/returns/" + r.id}>{tc.open}</Link></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
