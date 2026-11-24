"use client";

import { useCallback, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { createLocation, createWarehouse, fetchLocations, fetchWarehouses, type StockLocation, type Warehouse } from "@/lib/services/api";
import { useAuth } from "@/hooks/useAuth";
import { useListResource } from "@/hooks/useListResource";
import { Button, EmptyState, ErrorState, Input, LoadingSkeleton, Select, Table } from "@/components/uikit";
import type { Column } from "@/components/uikit";

type WhForm = { code: string; name: string };
type LocForm = { warehouseId: string; code: string; name: string };
type FormErrors = Partial<Record<keyof WhForm | keyof LocForm, string>>;

export default function WarehousesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const whFetcher = useCallback((tok: string) => fetchWarehouses(tok), []);
  const locFetcher = useCallback((tok: string) => fetchLocations(tok), []);
  const wh = useListResource<Warehouse>(token, whFetcher);
  const locs = useListResource<StockLocation>(token, locFetcher);

  const [whForm, setWhForm] = useState<WhForm>({ code: "", name: "" });
  const [locForm, setLocForm] = useState<LocForm>({ warehouseId: "", code: "", name: "" });
  const [whErrors, setWhErrors] = useState<FormErrors>({});
  const [locErrors, setLocErrors] = useState<FormErrors>({});
  const [createError, setCreateError] = useState<string | null>(null);
  const [whCreating, setWhCreating] = useState(false);
  const [locCreating, setLocCreating] = useState(false);

  const warehouses = wh.data ?? [];
  const locations = locs.data ?? [];
  const whCode = useMemo(() => new Map(warehouses.map((w) => [w.id, w.code])), [warehouses]);

  async function submitWarehouse(e: FormEvent) {
    e.preventDefault();
    const errs: FormErrors = {};
    if (!whForm.code.trim()) errs.code = t.inventory.warehouses.errors.codeRequired;
    if (!whForm.name.trim()) errs.name = t.inventory.warehouses.errors.nameRequired;
    setWhErrors(errs);
    if (errs.code || errs.name || !token) return;
    setWhCreating(true);
    setCreateError(null);
    const res = await createWarehouse(token, { code: whForm.code.trim(), name: whForm.name.trim() });
    setWhCreating(false);
    if (!res.ok) {
      setCreateError(res.code === "NETWORK" ? t.common.networkError : `${t.inventory.warehouses.createWhFailed} ${res.message}`);
      return;
    }
    wh.setData((prev) => [res.data, ...(prev ?? [])]);
    setWhForm({ code: "", name: "" });
  }

  async function submitLocation(e: FormEvent) {
    e.preventDefault();
    const errs: FormErrors = {};
    if (!locForm.warehouseId) errs.warehouseId = t.inventory.warehouses.errors.warehouseRequired;
    if (!locForm.code.trim()) errs.code = t.inventory.warehouses.errors.codeRequired;
    if (!locForm.name.trim()) errs.name = t.inventory.warehouses.errors.nameRequired;
    setLocErrors(errs);
    if (errs.warehouseId || errs.code || errs.name || !token) return;
    setLocCreating(true);
    setCreateError(null);
    const res = await createLocation(token, { warehouseId: locForm.warehouseId, code: locForm.code.trim(), name: locForm.name.trim() });
    setLocCreating(false);
    if (!res.ok) {
      setCreateError(res.code === "NETWORK" ? t.common.networkError : `${t.inventory.warehouses.createLocFailed} ${res.message}`);
      return;
    }
    locs.setData((prev) => [res.data, ...(prev ?? [])]);
    setLocForm({ warehouseId: locForm.warehouseId, code: "", name: "" });
  }

  function retryAll() {
    void wh.load();
    void locs.load();
  }

  const loading = !ready || (wh.phase === "loading" && wh.data === null) || (locs.phase === "loading" && locs.data === null);
  if (loading) return <LoadingSkeleton rows={6} />;
  if (wh.phase === "error" || locs.phase === "error") {
    return <ErrorState message={t.inventory.warehouses.loadFailed} retryLabel={t.common.retry} onRetry={retryAll} />;
  }

  const whColumns: Column<Warehouse>[] = [
    { key: "code", label: t.inventory.warehouses.columns.code, mono: true, render: (w) => w.code },
    { key: "name", label: t.inventory.warehouses.columns.name, render: (w) => w.name },
  ];
  const locColumns: Column<StockLocation>[] = [
    { key: "code", label: t.inventory.warehouses.columns.code, mono: true, render: (l) => l.code },
    { key: "name", label: t.inventory.warehouses.columns.name, render: (l) => l.name },
    { key: "warehouse", label: t.inventory.warehouses.columns.warehouse, mono: true, render: (l) => whCode.get(l.warehouseId) ?? l.warehouseId },
  ];

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.inventory.warehouses.title}</h1>
        <p className="t-caption">{t.inventory.warehouses.subtitle}</p>
      </div>
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{t.inventory.warehouses.whSection}</h2>
        {warehouses.length === 0 ? (
          <EmptyState title={t.inventory.warehouses.whEmpty} />
        ) : (
          <Table<Warehouse> columns={whColumns} rows={warehouses} caption={t.inventory.warehouses.whSection} />
        )}
        <form onSubmit={(e) => void submitWarehouse(e)}>
          <h3 className="t-body-strong">{t.inventory.warehouses.addWhTitle}</h3>
          <div className="form-grid">
            <Input id="wh-code" label={t.inventory.warehouses.code} className="input mono" value={whForm.code} onChange={(e) => setWhForm({ ...whForm, code: e.target.value })} error={whErrors.code} />
            <Input id="wh-name" label={t.inventory.warehouses.name} value={whForm.name} onChange={(e) => setWhForm({ ...whForm, name: e.target.value })} error={whErrors.name} />
          </div>
          <p><Button type="submit" loading={whCreating}>{t.inventory.warehouses.createWh}</Button></p>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.inventory.warehouses.locSection}</h2>
        {locations.length === 0 ? (
          <EmptyState title={t.inventory.warehouses.locEmpty} />
        ) : (
          <Table<StockLocation> columns={locColumns} rows={locations} caption={t.inventory.warehouses.locSection} />
        )}
        <form onSubmit={(e) => void submitLocation(e)}>
          <h3 className="t-body-strong">{t.inventory.warehouses.addLocTitle}</h3>
          <div className="form-grid">
            <Select
              id="loc-wh"
              label={t.inventory.warehouses.warehouse}
              placeholder={t.inventory.warehouses.warehouse}
              options={warehouses.map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
              value={locForm.warehouseId}
              onChange={(e) => setLocForm({ ...locForm, warehouseId: e.target.value })}
              error={locErrors.warehouseId}
            />
            <Input id="loc-code" label={t.inventory.warehouses.code} className="input mono" value={locForm.code} onChange={(e) => setLocForm({ ...locForm, code: e.target.value })} error={locErrors.code} />
            <Input id="loc-name" label={t.inventory.warehouses.name} value={locForm.name} onChange={(e) => setLocForm({ ...locForm, name: e.target.value })} error={locErrors.name} />
          </div>
          <p><Button type="submit" loading={locCreating}>{t.inventory.warehouses.createLoc}</Button></p>
        </form>
      </section>
    </div>
  );
}
