"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createQuote,
  fetchCustomers,
  listQuotes,
  type CustomerRow,
  type QuoteRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay, hasPermission, salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";
import { localStatus, localTone } from "../_lib/deferred";
import { fmtMoney } from "@/lib/format";
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
  type BadgeTone,
} from "@/components/uikit";

type QuoteLineForm = { itemSku: string; qty: string; unitPrice: string; discountPct: string };
const EMPTY_LINE: QuoteLineForm = { itemSku: "", qty: "", unitPrice: "", discountPct: "" };

const TONE_MAP: Record<string, BadgeTone> = {
  Accepted: "success",
  Expired: "danger",
  Rejected: "danger",
  PendingApproval: "warning",
};

// Plain concatenation keeps line-row labels readable and avoids inline template noise.
function lbl(label: string, n: number): string {
  return label + " " + (n + 1);
}
function qLink(locale: Locale, id: string): string {
  return "/" + locale + "/sales/quotes/" + id;
}

export default function SalesQuotesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.quotes;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [quotes, setQuotes] = useState<QuoteRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [customers, setCustomers] = useState<CustomerRow[]>([]);

  const [customerId, setCustomerId] = useState("");
  const [terms, setTerms] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [lines, setLines] = useState<QuoteLineForm[]>([{ ...EMPTY_LINE }]);
  const [createErrors, setCreateErrors] = useState<{ customer?: string; line?: string }>({});
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
    const res = await listQuotes(token);
    if (res.ok) {
      setQuotes(res.data);
      setPhase("success");
    } else {
      setQuotes(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  function setLine(i: number, patch: Partial<QuoteLineForm>) {
    setLines((prev) => prev.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)));
  }

  async function submitCreate(e: FormEvent) {
    e.preventDefault();
    const errs: { customer?: string; line?: string } = {};
    if (!customerId) errs.customer = tc.customerRequired;
    const parsed = lines.map((ln) => ({
      ...ln,
      qtyNum: Number(ln.qty),
      priceNum: Number(ln.unitPrice),
      discountNum: Number(ln.discountPct || 0),
    }));
    if (parsed.length === 0 || parsed.some((ln) => !ln.itemSku.trim())) errs.line = tc.errors.lineRequired;
    else if (parsed.some((ln) => !ln.qty.trim() || !Number.isFinite(ln.qtyNum) || ln.qtyNum <= 0)) errs.line = tc.errors.qtyInvalid;
    else if (parsed.some((ln) => ln.unitPrice.trim() && (!Number.isFinite(ln.priceNum) || ln.priceNum < 0))) errs.line = t.sales.errors.validation;
    else if (parsed.some((ln) => !Number.isFinite(ln.discountNum) || ln.discountNum < 0 || ln.discountNum >= 100)) errs.line = tc.errors.discountInvalid;
    setCreateErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const res = await createQuote(token, {
      customerId,
      currency: "BDT",
      lines: parsed.map((ln) => ({
        itemSku: ln.itemSku.trim(),
        qty: ln.qtyNum,
        ...(ln.unitPrice.trim() ? { unitPrice: ln.priceNum } : {}),
        ...(ln.discountPct.trim() ? { discountPct: ln.discountNum } : {}),
      })),
      ...(terms.trim() ? { terms: terms.trim() } : {}),
      ...(expiresAt ? { expiresAt } : {}),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", `${tc.created} ${res.data.id}`);
    setCustomerId("");
    setTerms("");
    setExpiresAt("");
    setLines([{ ...EMPTY_LINE }]);
    await load();
  }

  const quoteGate = hasPermission(permissions, "quotes:write");

  if (!ready || (phase === "loading" && quotes === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = quotes ?? [];

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
        {customers.length === 0 ? <EmptyState title={tc.noCustomers} /> : (
          <form onSubmit={(e) => void submitCreate(e)}>
            <div className="form-grid">
              <Select
                id="quote-customer"
                label={tc.customer}
                value={customerId}
                onChange={(e) => setCustomerId(e.target.value)}
                options={customers.map((c) => ({ value: c.id, label: `${c.name} (${c.id})` }))}
                placeholder={tc.customer}
                error={createErrors.customer}
              />
              <Input id="quote-currency" label={tc.currency} value="BDT" readOnly />
              <Input id="quote-terms" label={tc.terms} value={terms} onChange={(e) => setTerms(e.target.value)} />
              <Input id="quote-expires" label={tc.expiresAt} type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
            </div>
            {lines.map((ln, i) => (
              <div className="form-grid" key={i}>
                <Input id={"quote-sku-" + i} label={lbl(tc.line.sku, i)} value={ln.itemSku} onChange={(e) => setLine(i, { itemSku: e.target.value })} />
                <Input id={"quote-qty-" + i} label={lbl(tc.line.qty, i)} type="number" min="0" step="any" value={ln.qty} onChange={(e) => setLine(i, { qty: e.target.value })} />
                <Input id={"quote-price-" + i} label={lbl(tc.line.unitPrice, i)} type="number" min="0" step="any" value={ln.unitPrice} onChange={(e) => setLine(i, { unitPrice: e.target.value })} />
                <Input id={"quote-discount-" + i} label={lbl(tc.line.discountPct, i)} type="number" min="0" max="99" step="any" value={ln.discountPct} onChange={(e) => setLine(i, { discountPct: e.target.value })} />
                <div className="field">
                  <Button type="button" variant="secondary" small aria-label={lbl(tc.removeLine, i + 1)} disabled={lines.length === 1} onClick={() => setLines((prev) => prev.filter((_, idx) => idx !== i))}>
                    {tc.removeLine}
                  </Button>
                </div>
              </div>
            ))}
            {lines.some((ln) => Number(ln.discountPct || 0) > 15) ? <p className="t-caption" role="status">{tc.discountApprovalNote}</p> : null}
            {createErrors.line ? <p className="field__error">{createErrors.line}</p> : null}
            {createError ? <p className="banner" role="alert">{createError}</p> : null}
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button type="button" variant="secondary" small onClick={() => setLines((prev) => [...prev, { ...EMPTY_LINE }])}>{tc.addLine}</Button>
              <Button type="submit" loading={creating}>{tc.create}</Button>
            </div>
          </form>
        )}
      </section>

      <section className="card">
        {list.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{tc.columns.quote}</th>
                  <th scope="col">{tc.columns.customer}</th>
                  <th scope="col">{tc.columns.revision}</th>
                  <th scope="col">{tc.columns.status}</th>
                  <th scope="col">{tc.columns.total}</th>
                  <th scope="col">{tc.columns.expires}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((q) => (
                  <tr key={q.id}>
                    <td data-label={tc.columns.quote} className="mono">{q.id}</td>
                    <td data-label={tc.columns.customer} className="mono">{q.customerId}</td>
                    <td data-label={tc.columns.revision} className="mono">{q.revision}</td>
                    <td data-label={tc.columns.status}><Badge tone={localTone(TONE_MAP, q.status)}>{localStatus(t, tc.statusLabels, q.status)}</Badge></td>
                    <td data-label={tc.columns.total} className="mono">{q.total !== null ? fmtMoney(q.total, locale, q.currency) : "—"}</td>
                    <td data-label={tc.columns.expires}><span className="t-caption">{q.expiresAt ? fmtDay(q.expiresAt, locale) : "—"}</span></td>
                    <td data-label={t.journals.actions}>
                      {quoteGate !== false ? <Link className="btn btn--secondary btn--sm" href={qLink(locale, q.id)}>{tc.open}</Link> : null}
                    </td>
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
