"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { closeCount, fetchLocations, fetchMe, openCount, type CountCloseResult, type StockAdjustment, type StockCount, type StockLocation } from "@/lib/services/api";
import { fmtMoney, fmtNumber } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { useListResource } from "@/hooks/useListResource";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, statusTone, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

type LineForm = { itemSku: string; countedQty: string };
const EMPTY_LINE: LineForm = { itemSku: "", countedQty: "" };

export default function StockCountsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const locFetcher = useCallback((tok: string) => fetchLocations(tok), []);
  const { data, phase, load } = useListResource<StockLocation>(token, locFetcher);

  const [locId, setLocId] = useState("");
  const [locError, setLocError] = useState<string | null>(null);
  const [lines, setLines] = useState<LineForm[]>([EMPTY_LINE]);
  const [lineErrors, setLineErrors] = useState<Record<number, { field: "sku" | "qty"; message: string }>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [opening, setOpening] = useState(false);
  const [openCountData, setOpenCountData] = useState<StockCount | null>(null);
  const [closing, setClosing] = useState(false);
  const [closeError, setCloseError] = useState<string | null>(null);
  const [closeResult, setCloseResult] = useState<CountCloseResult | null>(null);
  const [meId, setMeId] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (!cancelled && res.ok) setMeId(res.data.user.id);
    });
    return () => { cancelled = true; };
  }, [token]);

  const locations = data ?? [];
  const locCode = useMemo(() => new Map(locations.map((l) => [l.id, l.code])), [locations]);

  function setLine(i: number, patch: Partial<LineForm>) {
    setLines((prev) => prev.map((ln, idx) => (idx === i ? { ...ln, ...patch } : ln)));
  }

  function validate(): boolean {
    const errs: Record<number, { field: "sku" | "qty"; message: string }> = {};
    setLocError(locId ? null : t.inventory.counts.errors.locationRequired);
    const seen = new Set<string>();
    lines.forEach((ln, i) => {
      const sku = ln.itemSku.trim();
      if (!sku) errs[i] = { field: "sku", message: t.inventory.counts.errors.skuRequired };
      else if (seen.has(sku.toLowerCase())) errs[i] = { field: "sku", message: t.inventory.counts.errors.duplicateSku };
      else if (!ln.countedQty.trim()) errs[i] = { field: "qty", message: t.inventory.counts.errors.qtyRequired };
      else {
        const qty = Number(ln.countedQty);
        if (!Number.isFinite(qty) || qty < 0) errs[i] = { field: "qty", message: t.inventory.counts.errors.qtyInvalid };
      }
      if (sku) seen.add(sku.toLowerCase());
    });
    setLineErrors(errs);
    return !locId || Object.keys(errs).length > 0;
  }

  async function submitOpen(e: FormEvent) {
    e.preventDefault();
    setFormError(null);
    setCloseError(null);
    if (validate() || !token) return;
    setOpening(true);
    const res = await openCount(token, {
      locationId: locId,
      items: lines.map((ln) => ({ itemSku: ln.itemSku.trim(), countedQty: Number(ln.countedQty) })),
    });
    setOpening(false);
    if (!res.ok) {
      setFormError(res.code === "NETWORK" ? t.common.networkError : `${t.inventory.counts.openFailed} ${res.message}`);
      return;
    }
    setOpenCountData(res.data);
    setLines([EMPTY_LINE]);
    setLocId("");
    setLineErrors({});
  }

  async function submitClose() {
    if (!token || !openCountData) return;
    if (!window.confirm(t.inventory.counts.closeConfirm)) return;
    setClosing(true);
    setCloseError(null);
    const res = await closeCount(token, openCountData.id);
    setClosing(false);
    if (!res.ok) {
      setCloseError(res.code === "NETWORK" ? t.common.networkError : `${t.inventory.counts.closeFailed} ${res.message}`);
      return;
    }
    setCloseResult(res.data);
    setOpenCountData(null);
  }

  const countColumns: Column<{ itemSku: string; countedQty: number; systemQty: number }>[] = [
    { key: "itemSku", label: t.inventory.counts.columns.itemSku, mono: true, render: (ln) => ln.itemSku },
    { key: "countedQty", label: t.inventory.counts.columns.countedQty, mono: true, render: (ln) => fmtNumber(ln.countedQty, locale) },
    { key: "systemQty", label: t.inventory.counts.columns.systemQty, mono: true, render: (ln) => fmtNumber(ln.systemQty, locale) },
    { key: "variance", label: t.inventory.counts.columns.variance, mono: true, render: (ln) => fmtNumber(ln.countedQty - ln.systemQty, locale) },
  ];
  const adjColumns: Column<StockAdjustment>[] = [
    { key: "itemSku", label: t.inventory.counts.adjColumns.itemSku, mono: true, render: (a) => a.itemSku },
    { key: "qtyVariance", label: t.inventory.counts.adjColumns.qtyVariance, mono: true, render: (a) => fmtNumber(a.qtyVariance, locale) },
    { key: "unitCost", label: t.inventory.counts.adjColumns.unitCost, mono: true, render: (a) => fmtMoney(a.unitCost, locale, "BDT") },
    { key: "amount", label: t.inventory.counts.adjColumns.amount, mono: true, render: (a) => fmtMoney(a.amount, locale, "BDT") },
    { key: "journal", label: t.inventory.counts.adjColumns.journal, mono: true, render: (a) => a.journalId },
  ];

  if (!ready || (phase === "loading" && data === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.inventory.counts.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.inventory.counts.title}</h1>
        <p className="t-caption">{t.inventory.counts.subtitle}</p>
      </div>

      {formError ? <p className="banner" role="alert">{formError}</p> : null}
      {closeError ? <p className="banner" role="alert">{closeError}</p> : null}

      {locations.length === 0 ? (
        <section className="card">
          <EmptyState title={t.inventory.counts.noLocations} />
        </section>
      ) : openCountData === null ? (
        <section className="card">
          <h2 className="t-title">{t.inventory.counts.formTitle}</h2>
          <form onSubmit={(e) => void submitOpen(e)}>
            <Select
              id="cnt-loc"
              label={t.inventory.counts.location}
              placeholder={t.inventory.counts.location}
              options={locations.map((l) => ({ value: l.id, label: `${l.code} — ${l.name}` }))}
              value={locId}
              onChange={(e) => { setLocId(e.target.value); setLocError(null); }}
              error={locError ?? undefined}
            />
            <h3 className="t-body-strong">{t.inventory.counts.lines}</h3>
            {lines.map((ln, i) => {
              const err = lineErrors[i];
              const skuError = err?.field === "sku" ? err.message : undefined;
              const qtyError = err?.field === "qty" ? err.message : undefined;
              return (
                <div className="form-grid" key={i}>
                  <Input
                    id={`cnt-sku-${i}`}
                    label={`${t.inventory.counts.itemSku} ${i + 1}`}
                    value={ln.itemSku}
                    onChange={(e) => setLine(i, { itemSku: e.target.value })}
                    error={skuError}
                  />
                  <Input
                    id={`cnt-qty-${i}`}
                    label={`${t.inventory.counts.countedQty} ${i + 1}`}
                    type="number"
                    min="0"
                    step="any"
                    value={ln.countedQty}
                    onChange={(e) => setLine(i, { countedQty: e.target.value })}
                    error={qtyError}
                  />
                  <div className="field">
                    <span className="sr-only">{`${t.inventory.counts.removeLine} ${i + 1}`}</span>
                    <Button
                      type="button"
                      variant="secondary"
                      small
                      aria-label={`${t.inventory.counts.removeLine} ${i + 1}`}
                      disabled={lines.length === 1}
                      onClick={() => setLines((prev) => prev.filter((_, idx) => idx !== i))}
                    >
                      {t.inventory.counts.removeLine}
                    </Button>
                  </div>
                </div>
              );
            })}
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button type="button" variant="secondary" small onClick={() => setLines((prev) => [...prev, EMPTY_LINE])}>
                {t.inventory.counts.addLine}
              </Button>
              <Button type="submit" loading={opening}>{t.inventory.counts.open}</Button>
            </div>
          </form>
        </section>
      ) : (
        <section className="card">
          <h2 className="t-title">
            {openCountData.id} · {locCode.get(openCountData.locationId) ?? openCountData.locationId}{" "}
            <Badge tone={statusTone(openCountData.status)}>
              {(t.common.status as Record<string, string>)[openCountData.status] ?? openCountData.status}
            </Badge>
          </h2>
          <Table columns={countColumns} rows={openCountData.items} caption={t.inventory.counts.title} />
          {openCountData.createdBy === meId && meId !== null ? (
            <p className="banner" role="status">{t.inventory.counts.makerChecker}</p>
          ) : closeError ? null : (
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button loading={closing} onClick={() => void submitClose()}>{t.inventory.counts.close}</Button>
            </div>
          )}
        </section>
      )}

      {closeResult ? (
        <section className="card">
          <h2 className="t-title">
            {t.inventory.counts.adjustmentsTitle} · {closeResult.count.id}{" "}
            <Badge tone={statusTone(closeResult.count.status)}>
              {(t.common.status as Record<string, string>)[closeResult.count.status] ?? closeResult.count.status}
            </Badge>
          </h2>
          {closeResult.adjustments.length === 0 ? (
            <EmptyState title={t.inventory.counts.noAdjustments} />
          ) : (
            <Table columns={adjColumns} rows={closeResult.adjustments} caption={t.inventory.counts.adjustmentsTitle} />
          )}
        </section>
      ) : null}
    </div>
  );
}
