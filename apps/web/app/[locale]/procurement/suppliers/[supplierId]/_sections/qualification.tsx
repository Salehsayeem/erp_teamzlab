"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchQualification, fetchEvidences, createEvidence, fetchRiskProfile,
  createScreening, disposeScreening, fetchScorecards, createScorecard, fetchScreeningsBySupplier,
  type QualificationStatus, type Evidence, type RiskProfile, type Scorecard, type ScreeningRow,
} from "@/lib/services/procurement";
import { fmtDate } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function QualificationSection({ supplierId }: { supplierId: string }) {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [qual, setQual] = useState<QualificationStatus | null>(null);
  const [evidences, setEvidences] = useState<Evidence[] | null>(null);
  const [risk, setRisk] = useState<RiskProfile | null>(null);
  const [screenings, setScreenings] = useState<ScreeningRow[] | null>(null);
  const [scorecards, setScorecards] = useState<Scorecard[] | null>(null);
  const [evType, setEvType] = useState("");
  const [evExpiry, setEvExpiry] = useState("");
  const [scSource, setScSource] = useState("Watchlist");
  const [scRef, setScRef] = useState("");
  const [scmFor, setScmFor] = useState<string | null>(null);
  const [scmDisposition, setScmDisposition] = useState("FalsePositive");
  const [scmReason, setScmReason] = useState("");
  const [genFrom, setGenFrom] = useState("");
  const [genTo, setGenTo] = useState("");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    const [q, e, r, s, sc] = await Promise.all([
      fetchQualification(token, supplierId), fetchEvidences(token, supplierId),
      fetchRiskProfile(token, supplierId), fetchScreeningsBySupplier(token, supplierId), fetchScorecards(token, supplierId),
    ]);
    if (q.ok && e.ok && r.ok && s.ok && sc.ok) { setQual(q.data); setEvidences(e.data); setRisk(r.data); setScreenings(s.data); setScorecards(sc.data); }
  }, [token, supplierId]);
  useEffect(() => { void load(); }, [load]);

  async function run(fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusy(true); setErr(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    await load();
    return true;
  }

  async function onEvidence(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createEvidence(token, supplierId, {
      evidenceType: evType, metadataJson: "{}", expiresOn: evExpiry || null, supersedesId: null,
    }));
    if (ok) setEvType("");
  }

  async function onScreening(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createScreening(token, supplierId, { source: scSource, sourceRef: scRef || null }));
    if (ok) { setScRef(""); setScSource("Watchlist"); }
  }

  async function onScreeningDispose() {
    if (!scmFor) return;
    const ok = await run(() => disposeScreening(token ?? "", scmFor, { disposition: scmDisposition, reason: scmReason }));
    if (ok) { setScmFor(null); setScmReason(""); }
  }

  async function onGenerate(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const ok = await run(() => createScorecard(token, {
      supplierId, periodFrom: genFrom, periodTo: genTo,
    }));
    if (ok) { setGenFrom(""); setGenTo(""); }
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;
  const evidenceList = evidences ?? [];
  const screeningList = screenings ?? [];
  const scorecardList = scorecards ?? [];

  if (!ready) return <LoadingSkeleton rows={3} />;

  return (
    <section className="card">
      <h2 className="t-title">{t.procurement.qual.title}</h2>
      <p className="t-caption">{t.procurement.qual.subtitle}</p>
      {err ? <p className="banner" role="alert">{err}</p> : null}
      {qual ? (
        <p>
          <Badge tone={qual.eligible ? "success" : "danger"}>{qual.eligible ? t.procurement.qual.eligible : t.procurement.qual.notEligible}</Badge>
          {qual.expiredEvidences.length > 0 ? (
            <span className="t-caption"> {t.procurement.qual.expiredEvidence}: {qual.expiredEvidences.map((e) => e.evidenceType).join(", ")}</span>
          ) : null}
        </p>
      ) : null}
      {risk ? (
        <p className="t-caption mono">
          {t.procurement.qual.riskProfile}: {risk.score} / {risk.band} / {risk.disposition} · v{risk.ruleVersion}
        </p>
      ) : null}
      <h3 className="t-title">{t.procurement.qual.evidences}</h3>
      {evidenceList.length === 0 ? <EmptyState title={t.procurement.qual.evidencesEmpty} /> : (
        <ul className="stack">
          {evidenceList.map((e) => (
            <li key={e.id}>
              <strong className="mono">{e.id}</strong> · {e.evidenceType}
              {e.expiresOn ? ` · ${t.procurement.qual.expiresOn} ${fmtDate(e.expiresOn, locale)}` : ""}
              {e.supersedesId ? ` · ${t.procurement.qual.supersedes} ${e.supersedesId}` : ""}
              {e.verifiedBy ? ` · ${t.procurement.qual.verifiedBy} ${e.verifiedBy}` : ""}
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={(e) => void onEvidence(e)} className="stack">
        <div className="form-grid">
          <Input id="ev-type" label={t.procurement.qual.evidenceType} value={evType} onChange={(e) => setEvType(e.target.value)} required />
          <Input id="ev-exp" label={t.procurement.qual.expiresOn} type="date" value={evExpiry} onChange={(e) => setEvExpiry(e.target.value)} />
        </div>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.qual.addEvidence}</Button>
        </div>
      </form>
      <h3 className="t-title">{t.procurement.qual.screening}</h3>
      {screeningList.length === 0 ? <EmptyState title={t.procurement.qual.screeningEmpty} /> : (
        <ul className="stack">
          {screeningList.map((s) => (
            <li key={s.id}>
              <strong className="mono">{s.id}</strong> · {s.source} · <Badge tone={s.outcome === "Clear" ? "success" : s.outcome === "Unknown" ? "muted" : "warning"}>{s.outcome}</Badge>
              {" "}{s.status}
              {s.disposition ? ` · ${s.disposition} (${s.dispositionBy ?? ""})` : ""}
              {s.status === "PendingDisposition" ? (
                <span className="row-actions" style={{ justifyContent: "flex-start" }}>
                  <Button small onClick={() => { setScmFor(s.id); setScmDisposition("FalsePositive"); setScmReason(""); }}>{t.procurement.qual.dispose}</Button>
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={(e) => void onScreening(e)} className="stack">
        <div className="form-grid">
          <Input id="sc-source" label={t.procurement.qual.screeningSource} value={scSource} onChange={(e) => setScSource(e.target.value)} required />
          <Input id="sc-ref" label={t.procurement.qual.screeningRef} value={scRef} onChange={(e) => setScRef(e.target.value)} />
        </div>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.qual.newScreening}</Button>
        </div>
      </form>
      <h3 className="t-title">{t.procurement.qual.scorecards}</h3>
      {scorecardList.length === 0 ? <EmptyState title={t.procurement.qual.scorecardsEmpty} /> : (
        <ul className="stack">
          {scorecardList.map((sc) => (
            <li key={sc.id}>
              <strong className="mono">{sc.id}</strong> · <Badge tone={sc.status === "Rated" ? "success" : "muted"}>{sc.status === "NotRated" ? t.procurement.qual.notRated : sc.status}</Badge>
              {" "}{sc.periodFrom} → {sc.periodTo} · v{sc.calcVersion}
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={(e) => void onGenerate(e)} className="stack">
        <div className="form-grid">
          <Input id="sc-from" label={t.procurement.qual.periodFrom} type="date" value={genFrom} onChange={(e) => setGenFrom(e.target.value)} required />
          <Input id="sc-to" label={t.procurement.qual.periodTo} type="date" value={genTo} onChange={(e) => setGenTo(e.target.value)} required />
        </div>
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.qual.generate}</Button>
        </div>
      </form>
      <Modal open={scmFor !== null} onClose={() => setScmFor(null)} title={t.procurement.qual.dispose} closeLabel={t.common.cancel}>
        <p className="t-caption">{t.procurement.qual.dispositionQueue}</p>
        <form onSubmit={(e) => { e.preventDefault(); void onScreeningDispose(); }} className="stack">
          <div className="form-grid">
            <div className="field">
              <label htmlFor="scm-disp">{t.procurement.qual.disposition}</label>
              <select id="scm-disp" className="input" value={scmDisposition} onChange={(e) => setScmDisposition(e.target.value)}>
                {["ConfirmedClear", "FalsePositive", "ConfirmedMatch"].map((d) => <option key={d} value={d}>{d}</option>)}
              </select>
            </div>
            <Input id="scm-reason" label={t.procurement.qual.reason} value={scmReason} onChange={(e) => setScmReason(e.target.value)} required />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.qual.dispositionConfirm}</Button>
          </div>
        </form>
      </Modal>
    </section>
  );
}
