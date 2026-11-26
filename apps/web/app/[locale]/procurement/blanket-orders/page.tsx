"use client";

import { Fragment } from "react";
import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchBlanketOrders, fetchBlanketOrder, createBlanketOrder, createBlanketRelease, type BlanketOrder } from "@/lib/services/procurement";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";
type Detail = Awaited<ReturnType<typeof fetchBlanketOrder>>;
type OkResult = Extract<Detail, { ok: true }> | { ok: false; message: string };

export default function BlanketOrdersPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [rows, setRows] = useState<BlanketOrder[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [sup, setSup] = useState("");
  const [cur, setCur] = useState("BDT");
  const [ceil, setCeil] = useState("");
  const [vf, setVf] = useState("");
  const [vt, setVt] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [detail, setDetail] = useState<Extract<Detail, { ok: true }>["data"] | null>(null);
  const [releaseAmt, setReleaseAmt] = useState("");
  const [releaseFor, setReleaseFor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchBlanketOrders(token);
    if (res.ok) { setRows(res.data); setPhase("success"); }
    else { setRows(null); setPhase("error"); }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function openDetail(id: string) {
    if (!token) return;
    setExpanded(id);
    const res = await fetchBlanketOrder(token, id);
    setDetail(res.ok ? res.data : null);
  }

  async function run(fn: () => Promise<{ ok: true } | { ok: false; message: string }>) {
    setBusy(true); setErr(null);
    const res = await fn();
    setBusy(false);
    if (!res.ok) { setErr(`${t.procurement.common.actionFailed}: ${res.message}`); return false; }
    if (expanded) await openDetail(expanded);
    await load();
    return true;
  }

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!token) return;
    const amount = Number(ceil);
    if (!sup || !Number.isFinite(amount) || amount <= 0 || !vf || !vt) return;
    const ok = await run(() => createBlanketOrder(token, {
      supplierId: sup, currency: cur, ceilingAmount: amount, validFrom: vf, validTo: vt,
    }));
    if (ok) { setSup(""); setCeil(""); }
  }

  async function onRelease(e: React.FormEvent) {
    e.preventDefault();
    if (!releaseFor || !token) return;
    const amount = Number(releaseAmt);
    if (!Number.isFinite(amount) || amount <= 0) return;
    const ok = await run(() => createBlanketRelease(token, releaseFor, { amount }));
    if (ok) { setReleaseFor(null); setReleaseAmt(""); }
  }

  const statusLabel = (s: string) => (t.common.status as Record<string, string>)[s] ?? s;
  const list = rows ?? [];

  if (!ready || (phase === "loading" && rows === null)) return <LoadingSkeleton rows={5} />;
  if (phase === "error") return <ErrorState message={t.procurement.blanket.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.procurement.blanket.title}</h1>
        <p className="t-caption">{t.procurement.blanket.subtitle}</p>
      </div>
      {err ? <p className="banner" role="alert">{err}</p> : null}

      <section className="card">
        <h2 className="t-title">{t.procurement.blanket.title}</h2>
        {list.length === 0 ? <EmptyState title={t.procurement.blanket.empty} /> : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">ID</th>
                  <th scope="col">{t.procurement.blanket.supplier}</th>
                  <th scope="col">{t.procurement.blanket.ceiling}</th>
                  <th scope="col">{t.procurement.blanket.released}</th>
                  <th scope="col">{t.procurement.blanket.remaining}</th>
                  <th scope="col">{t.procurement.blanket.validTo}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((b) => (
                  <Fragment>
                    <tr key={b.id}>
                      <td className="mono">{b.id}</td>
                      <td>{b.supplierId}</td>
                      <td className="mono">{fmtMoney(b.ceilingAmount, locale, b.currency)}</td>
                      <td className="mono">{fmtMoney(b.releasedAmount, locale, b.currency)}</td>
                      <td className="mono">{fmtMoney(b.remaining, locale, b.currency)}</td>
                      <td><span className="t-caption">{fmtDate(b.validTo, locale)}</span></td>
                      <td>
                        <div className="row-actions">
                          <Button small variant="secondary" onClick={() => void openDetail(b.id)}>{t.po.showLines}</Button>
                          <Button small onClick={() => { setReleaseFor(b.id); setReleaseAmt(""); }}>{t.procurement.blanket.release}</Button>
                        </div>
                      </td>
                    </tr>
                    {expanded === b.id && detail ? (
                      <tr className="sub-row"><td colSpan={7}>
                        <p className="t-caption mono">{t.procurement.blanket.releases}</p>
                        <ul>
                          {detail.releases.map((rel) => (
                            <li key={rel.id} className="mono">{rel.id} · {fmtMoney(rel.amount, locale, detail.currency)} · {rel.releasedBy}</li>
                          ))}
                        </ul>
                      </td></tr>
                    ) : null}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <form onSubmit={(e) => void onCreate(e)} className="stack" style={{ marginTop: "1rem" }}>
          <div className="form-grid">
            <Input id="b-sup" label={t.procurement.blanket.supplier} value={sup} onChange={(e) => setSup(e.target.value)} required />
            <Input id="b-cur" label={t.procurement.blanket.currency} value={cur} onChange={(e) => setCur(e.target.value)} required />
            <Input id="b-ceil" label={t.procurement.blanket.ceiling} type="number" min="0" step="any" value={ceil} onChange={(e) => setCeil(e.target.value)} required />
            <Input id="b-vf" label={t.procurement.blanket.validFrom} type="date" value={vf} onChange={(e) => setVf(e.target.value)} required />
            <Input id="b-vt" label={t.procurement.blanket.validTo} type="date" value={vt} onChange={(e) => setVt(e.target.value)} required />
          </div>
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.blanket.create}</Button>
          </div>
        </form>
      </section>

      <Modal open={releaseFor !== null} onClose={() => setReleaseFor(null)} title={t.procurement.blanket.release} closeLabel={t.common.cancel}>
        <form onSubmit={(e) => void onRelease(e)} className="stack">
          <p className="mono">{releaseFor}</p>
          <Input id="rel-amt" label={t.procurement.blanket.releaseAmount} type="number" min="0" step="any" value={releaseAmt} onChange={(e) => setReleaseAmt(e.target.value)} required />
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="submit" small loading={busy} disabled={busy}>{t.procurement.blanket.releaseConfirm}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
