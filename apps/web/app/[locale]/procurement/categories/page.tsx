"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchCategories, createCategory, deprecateCategory, fetchUnits, createUom, createConversion, type ProcurementCategory, type UnitOfMeasure } from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function CategoriesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [rows, setRows] = useState<ProcurementCategory[] | null>(null);
  const [units, setUnits] = useState<UnitOfMeasure[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [name, setName] = useState("");
  const [parent, setParent] = useState("");
  const [uCode, setUCode] = useState("");
  const [uDim, setUDim] = useState("Count");
  const [uPrec, setUPrec] = useState("2");
  const [uBase, setUBase] = useState(false);
  const [convFrom, setConvFrom] = useState("");
  const [convTo, setConvTo] = useState("");
  const [convFactor, setConvFactor] = useState("");
  const [convFrom0, setConvFrom0] = useState("");
  const [dep, setDep] = useState<ProcurementCategory | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const [c, u] = await Promise.all([fetchCategories(token), fetchUnits(token)]);
    if (c.ok && u.ok) { setRows(c.data); setUnits(u.data); setPhase("success"); }
    else { setRows(null); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusy(true); setErr(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    await load();
    return true;
  }

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createCategory(token, { name, parentId: parent || null }));
    if (ok) { setName(""); setParent(""); }
  }

  async function onUnit(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const prec = Number(uPrec);
    if (!Number.isFinite(prec) || prec < 0 || prec > 6) return;
    const ok = await run(() => createUom(token, { code: uCode, name: uCode, dimension: uDim as UnitOfMeasure["dimension"], precision: prec, isBase: uBase }));
    if (ok) setUCode("");
  }

  async function onConversion(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const factor = Number(convFactor);
    if (!convFrom || !convTo || !Number.isFinite(factor) || factor <= 0 || !convFrom0) return;
    const ok = await run(() => createConversion(token, convFrom, {
      toCode: convTo, factor, effectiveFrom: convFrom0, effectiveTo: null,
    }));
    if (ok) { setConvFactor(""); setConvFrom0(""); }
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;
  const catList = rows ?? [];
  const unitList = units ?? [];

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.procurement.categories.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const categoryRows = catList.map((c) => (
    <tr key={c.id}>
      <td className="mono">{c.id}</td>
      <td>{c.name}</td>
      <td className="mono">{c.parentId ?? t.procurement.categories.none}</td>
      <td><Badge tone={c.status === "Deprecated" ? "muted" : statusTone(c.status)}>{statusLabel(c.status)}</Badge></td>
      <td><span className="t-caption">{fmtDate(c.effectiveFrom, locale)}</span></td>
      <td>
        {c.status === "Active" ? (
          <Button small variant="secondary" onClick={() => setDep(c)}>{t.procurement.categories.deprecate}</Button>
        ) : <span className="t-caption">{t.procurement.categories.readOnlyDeprecated}</span>}
      </td>
    </tr>
  ));

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.procurement.categories.title}</h1>
        <p className="t-caption">{t.procurement.categories.subtitle}</p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}

      <section className="card">
        <h2 className="t-title">{t.procurement.categories.title}</h2>
        {catList.length === 0 ? <EmptyState title={t.procurement.categories.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{t.procurement.categories.code}</th>
                  <th scope="col">{t.procurement.categories.name}</th>
                  <th scope="col">{t.procurement.categories.parent}</th>
                  <th scope="col">{t.procurement.categories.status}</th>
                  <th scope="col">{t.procurement.categories.effectiveFrom}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>{categoryRows}</tbody>
            </table>
          </div>
        )}
        <form onSubmit={(e) => void onCreate(e)} className="stack" style={{ marginTop: "1rem" }}>
          <div className="form-grid">
            <Input id="cat-name" label={t.procurement.categories.name} value={name} onChange={(e) => setName(e.target.value)} required />
            <div className="field">
              <label htmlFor="cat-parent">{t.procurement.categories.parent}</label>
              <select id="cat-parent" className="input" value={parent} onChange={(e) => setParent(e.target.value)}>
                <option value="">{t.procurement.categories.none}</option>
                {catList.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </div>
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.categories.create}</Button>
          </div>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.categories.unitsTitle}</h2>
        {unitList.length === 0 ? <EmptyState title={t.procurement.categories.unitsEmpty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{t.procurement.categories.code}</th>
                  <th scope="col">{t.procurement.categories.dimension}</th>
                  <th scope="col">{t.procurement.categories.precision}</th>
                  <th scope="col">{t.procurement.categories.isBase}</th>
                </tr>
              </thead>
              <tbody>
                {unitList.map((u) => (
                  <tr key={u.code}>
                    <td className="mono">{u.code}</td>
                    <td>{u.dimension}</td>
                    <td className="mono">{u.precision}</td>
                    <td className="mono">{u.isBase ? "✓" : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <form onSubmit={(e) => void onUnit(e)} className="stack" style={{ marginTop: "1rem" }}>
          <div className="form-grid">
            <Input id="u-code" label={t.procurement.categories.code} value={uCode} onChange={(e) => setUCode(e.target.value)} required />
            <div className="field">
              <label htmlFor="u-dim">{t.procurement.categories.dimension}</label>
              <select id="u-dim" className="input" value={uDim} onChange={(e) => setUDim(e.target.value)}>
                {["Count", "Length", "Mass", "Volume", "Area", "Time"].map((d) => <option key={d} value={d}>{d}</option>)}
              </select>
            </div>
            <Input id="u-prec" label={t.procurement.categories.precision} type="number" min="0" max="6" value={uPrec} onChange={(e) => setUPrec(e.target.value)} required />
            <div className="field">
              <label htmlFor="u-base">{t.procurement.categories.isBase}</label>
              <input id="u-base" type="checkbox" checked={uBase} onChange={(e) => setUBase(e.target.checked)} />
            </div>
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-end" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.categories.addUnit}</Button>
          </div>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{t.procurement.categories.conversions}</h2>
        <form onSubmit={(e) => void onConversion(e)} className="stack">
          <div className="form-grid">
            <div className="field">
              <label htmlFor="cv-from">{t.procurement.categories.code}</label>
              <select id="cv-from" className="input" value={convFrom} onChange={(e) => setConvFrom(e.target.value)} required>
                <option value="">—</option>
                {unitList.map((u) => <option key={u.code} value={u.code}>{u.code}</option>)}
              </select>
            </div>
            <div className="field">
              <label htmlFor="cv-to">{t.procurement.categories.conversionTo}</label>
              <select id="cv-to" className="input" value={convTo} onChange={(e) => setConvTo(e.target.value)} required>
                <option value="">—</option>
                {unitList.filter((u) => u.isBase).map((u) => <option key={u.code} value={u.code}>{u.code}</option>)}
              </select>
            </div>
            <Input id="cv-factor" label={t.procurement.categories.conversionFactor} type="number" min="0" step="any" value={convFactor} onChange={(e) => setConvFactor(e.target.value)} required />
            <Input id="cv-date" label={t.procurement.categories.conversionFrom} type="date" value={convFrom0} onChange={(e) => setConvFrom0(e.target.value)} required />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.categories.addConversion}</Button>
          </div>
        </form>
      </section>

      <Modal open={dep !== null} onClose={() => setDep(null)} title={t.procurement.categories.deprecate} closeLabel={t.common.cancel}>
        <p>{dep ? `${dep.id} — ${dep.name}` : ""}</p>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button small loading={busy} disabled={busy || !dep} onClick={() => {
            if (!dep || !token) return;
            void run(() => deprecateCategory(token, dep.id)).then((ok) => { if (ok) setDep(null); });
          }}>{t.procurement.categories.deprecateConfirm}</Button>
        </div>
      </Modal>
    </div>
  );
}
