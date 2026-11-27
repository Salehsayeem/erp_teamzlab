"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createDimensionSet,
  createDimensionValue,
  fetchDimensionSets,
  fetchDimensionValues,
  type DimensionSet,
  type DimensionValue,
} from "@/lib/services/finance";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Table } from "@/components/uikit";

export default function DimensionsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const d = t.fin.dimensions;
  const { token, ready } = useAuth(locale);
  const [sets, setSets] = useState<DimensionSet[] | null>(null);
  const [values, setValues] = useState<DimensionValue[] | null>(null);
  const [phase, setPhase] = useState<string>("loading");
  const [banner, setBanner] = useState<string | null>(null);
  const [setForm, setSetForm] = useState({ code: "", name: "", required: false });
  const [valueForm, setValueForm] = useState({ setCode: "", value: "", from: "", to: "", active: true });
  const [setBusy, setSetBusy] = useState(false);
  const [valueBusy, setValueBusy] = useState(false);
  const [setError, setSetError] = useState<string | null>(null);
  const [valueError, setValueError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [s, v] = await Promise.all([fetchDimensionSets(token), fetchDimensionValues(token)]);
    if (s.ok && v.ok) {
      setSets(s.data);
      setValues(v.data);
      setPhase("success");
    } else {
      setSets(null);
      setValues(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitSet() {
    if (!token) return;
    setSetError(null);
    if (!setForm.code.trim() || !setForm.name.trim()) { setSetError(d.actionFailed); return; }
    setSetBusy(true);
    const res = await createDimensionSet(token, { code: setForm.code.trim(), name: setForm.name.trim(), required: setForm.required });
    setSetBusy(false);
    if (!res.ok) {
      setBanner(res.code === "NETWORK" ? t.common.networkError : `${d.actionFailed}: ${res.message}`);
      return;
    }
    setSetForm({ code: "", name: "", required: false });
    await load();
  }

  async function submitValue() {
    if (!token) return;
    setValueError(null);
    if (!valueForm.setCode.trim() || !valueForm.value.trim() || !valueForm.from) { setValueError(d.actionFailed); return; }
    setValueBusy(true);
    const res = await createDimensionValue(token, {
      setCode: valueForm.setCode.trim(),
      value: valueForm.value.trim(),
      effectiveFrom: valueForm.from,
      effectiveTo: valueForm.to || undefined,
      active: valueForm.active,
    });
    setValueBusy(false);
    if (!res.ok) {
      setBanner(res.code === "NETWORK" ? t.common.networkError : `${d.actionFailed}: ${res.message}`);
      return;
    }
    setValueForm({ setCode: valueForm.setCode, value: "", from: "", to: "", active: true });
    await load();
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} label={t.common.loading} />;
  if (phase === "error") return <ErrorState message={d.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{d.title}</h1>
        <p className="t-caption">{d.subtitle}</p>
      </div>
      {banner ? <p className="banner" role="alert">{banner}</p> : null}

      <section className="card">
        <h2 className="t-title">{d.setsTitle}</h2>
        {sets !== null && sets.length === 0 ? (
          <EmptyState title={d.emptySets} />
        ) : (
          <Table
            caption={d.setsTitle}
            rows={sets ?? []}
            columns={[
              { key: "code", label: d.columns.code, mono: true, render: (s) => s.code },
              { key: "name", label: d.columns.name, render: (s) => s.name },
              { key: "required", label: d.columns.required, render: (s) => <Badge tone={s.required ? "warning" : "muted"}>{s.required ? d.columns.required : "—"}</Badge> },
              { key: "active", label: d.columns.active, render: (s) => <Badge tone={s.active ? "success" : "muted"}>{s.active ? d.columns.active : "—"}</Badge> },
            ]}
          />
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{d.valuesTitle}</h2>
        {values !== null && values.length === 0 ? (
          <EmptyState title={d.emptyValues} />
        ) : (
          <Table
            caption={d.valuesTitle}
            rows={values ?? []}
            columns={[
              { key: "set", label: d.columns.set, mono: true, render: (v) => v.setCode },
              { key: "value", label: d.columns.value, mono: true, render: (v) => v.value },
              { key: "from", label: d.columns.from, mono: true, render: (v) => v.effectiveFrom },
              { key: "to", label: d.columns.to, mono: true, render: (v) => v.effectiveTo ?? "—" },
              { key: "active", label: d.columns.active, render: (v) => <Badge tone={v.active ? "success" : "muted"}>{v.active ? d.columns.active : "—"}</Badge> },
            ]}
          />
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{d.createSetTitle}</h2>
        <div className="form">
          <Input id="dim-set-code" label={d.codeLabel} className="input mono" value={setForm.code}
            error={setError ?? undefined}
            onChange={(e) => setSetForm({ ...setForm, code: e.target.value })} />
          <Input id="dim-set-name" label={d.nameLabel} value={setForm.name}
            onChange={(e) => setSetForm({ ...setForm, name: e.target.value })} />
          <label className="field" htmlFor="dim-set-required">
            {d.requiredLabel}
            <input id="dim-set-required" type="checkbox" checked={setForm.required}
              onChange={(e) => setSetForm({ ...setForm, required: e.target.checked })} />
          </label>
          <div>
            <Button loading={setBusy} disabled={setBusy} onClick={() => void submitSet()}>{d.createSet}</Button>
          </div>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{d.createValueTitle}</h2>
        <div className="form">
          <Input id="dim-val-set" label={d.setLabel} className="input mono" value={valueForm.setCode}
            error={valueError ?? undefined}
            onChange={(e) => setValueForm({ ...valueForm, setCode: e.target.value })} />
          <Input id="dim-val-value" label={d.valueLabel} className="input mono" value={valueForm.value}
            onChange={(e) => setValueForm({ ...valueForm, value: e.target.value })} />
          <Input id="dim-val-from" label={d.fromLabel} type="date" className="input mono" value={valueForm.from}
            onChange={(e) => setValueForm({ ...valueForm, from: e.target.value })} />
          <Input id="dim-val-to" label={d.toLabel} type="date" className="input mono" value={valueForm.to}
            onChange={(e) => setValueForm({ ...valueForm, to: e.target.value })} />
          <label className="field" htmlFor="dim-val-active">
            {d.activeLabel}
            <input id="dim-val-active" type="checkbox" checked={valueForm.active}
              onChange={(e) => setValueForm({ ...valueForm, active: e.target.checked })} />
          </label>
          <div>
            <Button loading={valueBusy} disabled={valueBusy} onClick={() => void submitValue()}>{d.createValue}</Button>
          </div>
        </div>
      </section>
    </div>
  );
}
