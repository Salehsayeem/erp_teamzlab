"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchNumberSeries, voidNumber, type NumberSeriesRow } from "@/lib/services/finance";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, Input, LoadingSkeleton, Modal, Table } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

// Finance admin: number-series view (016) over the shared doc_ sequences, plus the
// void-with-reason action confirmed through a Modal naming the exact number.
export default function NumberSeriesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const s = t.fin.series;
  const { token, ready } = useAuth(locale);
  const [rows, setRows] = useState<NumberSeriesRow[] | null>(null);
  const [phase, setPhase] = useState<string>("loading");
  const [banner, setBanner] = useState<string | null>(null);
  const [voidTarget, setVoidTarget] = useState<NumberSeriesRow | null>(null);
  const [voidForm, setVoidForm] = useState({ number: "", reason: "" });
  const [voidFormError, setVoidFormError] = useState<string | null>(null);
  const [voidBusy, setVoidBusy] = useState(false);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchNumberSeries(token);
    if (res.ok) {
      setRows(res.data);
      setPhase("success");
    } else {
      setRows(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitVoid() {
    if (!token || !voidTarget) return;
    setVoidFormError(null);
    if (!voidForm.number.trim() || !voidForm.reason.trim()) { setVoidFormError(s.actionFailed); return; }
    setVoidBusy(true);
    const res = await voidNumber(token, voidTarget.prefix, { number: voidForm.number.trim(), reason: voidForm.reason.trim() });
    setVoidBusy(false);
    setVoidTarget(null);
    if (!res.ok) {
      setBanner(res.code === "NETWORK" ? t.common.networkError : `${s.actionFailed}: ${res.message}`);
      return;
    }
    await load();
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} label={t.common.loading} />;
  if (phase === "error") return <ErrorState message={s.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{s.title}</h1>
        <p className="t-caption">{s.subtitle}</p>
      </div>
      {banner ? <p className="banner" role="alert">{banner}</p> : null}

      <section className="card">
        {rows !== null && rows.length === 0 ? (
          <EmptyState title={s.empty} />
        ) : (
          <Table
            caption={s.title}
            rows={rows ?? []}
            columns={[
              { key: "prefix", label: s.columns.prefix, mono: true, render: (r) => r.prefix },
              { key: "lastNumber", label: s.columns.lastNumber, mono: true, render: (r) => r.lastNumber },
              { key: "voided", label: s.columns.voided, render: (r) => (
                r.voidedCount > 0 ? <Badge tone="danger">{String(r.voidedCount)}</Badge> : <span className="t-caption">0</span>
              ) },
              { key: "actions", label: t.journals.actions, render: (r) => (
                <Button small variant="secondary" onClick={() => { setVoidTarget(r); setVoidForm({ number: "", reason: "" }); }}>
                  {s.voidAction}
                </Button>
              ) },
            ]}
          />
        )}
      </section>

      <Modal open={voidTarget !== null} onClose={() => setVoidTarget(null)} title={s.voidTitle} closeLabel={t.common.cancel} busy={voidBusy}>
        {voidTarget ? (
          <div className="stack">
            <p>{s.voidBody.replace("{number}", voidForm.number || "—").replace("{prefix}", voidTarget.prefix)}</p>
            <Input id="void-number" label={s.numberLabel} className="input mono" value={voidForm.number}
              onChange={(e) => setVoidForm({ ...voidForm, number: e.target.value.toUpperCase() })} />
            <Input id="void-reason" label={s.reasonLabel} value={voidForm.reason}
              onChange={(e) => setVoidForm({ ...voidForm, reason: e.target.value })} error={voidFormError ?? undefined} />
            <div className="row-actions">
              <Button variant="secondary" onClick={() => setVoidTarget(null)}>{t.common.cancel}</Button>
              <Button variant="danger" loading={voidBusy} disabled={voidBusy} onClick={() => void submitVoid()}>{s.void}</Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
