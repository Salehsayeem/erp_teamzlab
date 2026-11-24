"use client";

import { useCallback, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createTransfer, fetchLocations, type StockLocation, type StockTransfer } from "@/lib/services/api";
import { fmtNumber } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { useListResource } from "@/hooks/useListResource";
import { Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select } from "@/components/uikit";

type FormState = { fromLocationId: string; toLocationId: string; itemSku: string; qty: string };
type FormErrors = Partial<Record<keyof FormState, string>>;

export default function TransfersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const locFetcher = useCallback((tok: string) => fetchLocations(tok), []);
  const { data, phase, load } = useListResource<StockLocation>(token, locFetcher);

  const [form, setForm] = useState<FormState>({ fromLocationId: "", toLocationId: "", itemSku: "", qty: "" });
  const [errors, setErrors] = useState<FormErrors>({});
  const [posting, setPosting] = useState(false);
  const [postError, setPostError] = useState<string | null>(null);
  const [posted, setPosted] = useState<StockTransfer | null>(null);

  const locations = data ?? [];
  const locCode = useMemo(() => new Map(locations.map((l) => [l.id, l.code])), [locations]);
  const locOptions = useMemo(
    () => locations.map((l) => ({ value: l.id, label: `${l.code} — ${l.name}` })),
    [locations],
  );

  function validate(): FormErrors {
    const errs: FormErrors = {};
    if (!form.fromLocationId) errs.fromLocationId = t.inventory.transfers.errors.fromRequired;
    if (!form.toLocationId) errs.toLocationId = t.inventory.transfers.errors.toRequired;
    else if (form.fromLocationId && form.fromLocationId === form.toLocationId) errs.toLocationId = t.inventory.transfers.errors.sameLocation;
    if (!form.itemSku.trim()) errs.itemSku = t.inventory.transfers.errors.skuRequired;
    const qty = Number(form.qty);
    if (!form.qty.trim()) errs.qty = t.inventory.transfers.errors.qtyRequired;
    else if (!Number.isFinite(qty) || qty <= 0) errs.qty = t.inventory.transfers.errors.qtyInvalid;
    return errs;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs = validate();
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setPosting(true);
    setPostError(null);
    setPosted(null);
    const res = await createTransfer(token, {
      fromLocationId: form.fromLocationId,
      toLocationId: form.toLocationId,
      itemSku: form.itemSku.trim(),
      qty: Number(form.qty),
    });
    setPosting(false);
    if (!res.ok) {
      setPostError(res.code === "NETWORK" ? t.common.networkError : `${t.inventory.transfers.postFailed} ${res.message}`);
      return;
    }
    setPosted(res.data);
    setForm({ fromLocationId: form.fromLocationId, toLocationId: "", itemSku: "", qty: "" });
  }

  if (!ready || (phase === "loading" && data === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.inventory.transfers.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  if (locations.length < 2) {
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{t.inventory.transfers.title}</h1>
          <p className="t-caption">{t.inventory.transfers.subtitle}</p>
        </div>
        <section className="card">
          <EmptyState title={t.inventory.transfers.noLocations} />
        </section>
      </div>
    );
  }

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.inventory.transfers.title}</h1>
        <p className="t-caption">{t.inventory.transfers.subtitle}</p>
      </div>
      {posted ? (
        <p className="banner" role="status">
          {t.inventory.transfers.posted} <span className="mono">{posted.id}</span>: {locCode.get(posted.fromLocationId) ?? posted.fromLocationId} → {locCode.get(posted.toLocationId) ?? posted.toLocationId} · <span className="mono">{posted.itemSku} × {fmtNumber(posted.qty, locale)}</span>
        </p>
      ) : null}
      {postError ? <p className="banner" role="alert">{postError}</p> : null}
      <section className="card">
        <h2 className="t-title">{t.inventory.transfers.formTitle}</h2>
        <form onSubmit={(e) => void submit(e)}>
          <div className="form-grid">
            <Select
              id="tr-from"
              label={t.inventory.transfers.fromLocation}
              placeholder={t.inventory.transfers.fromLocation}
              options={locOptions}
              value={form.fromLocationId}
              onChange={(e) => setForm({ ...form, fromLocationId: e.target.value })}
              error={errors.fromLocationId}
            />
            <Select
              id="tr-to"
              label={t.inventory.transfers.toLocation}
              placeholder={t.inventory.transfers.toLocation}
              options={locOptions}
              value={form.toLocationId}
              onChange={(e) => setForm({ ...form, toLocationId: e.target.value })}
              error={errors.toLocationId}
            />
            <Input id="tr-sku" label={t.inventory.transfers.itemSku} className="input mono" value={form.itemSku} onChange={(e) => setForm({ ...form, itemSku: e.target.value })} error={errors.itemSku} />
            <Input id="tr-qty" label={t.inventory.transfers.qty} type="number" min="0" step="any" className="input mono" value={form.qty} onChange={(e) => setForm({ ...form, qty: e.target.value })} error={errors.qty} />
          </div>
          <p><Button type="submit" loading={posting}>{t.inventory.transfers.post}</Button></p>
        </form>
      </section>
    </div>
  );
}
