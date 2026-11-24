"use client";

import { Fragment, useCallback, useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchJournals, reverseJournal, type Journal } from "@/lib/services/api";
import { fmtDate, fmtMoney } from "@/lib/format";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, EmptyState, ErrorState, LoadingSkeleton, statusTone } from "@/components/uikit";

type Phase = "loading" | "error" | "success";

export default function JournalsPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const { token, ready } = useAuth(locale);
  const [journals, setJournals] = useState<Journal[] | null>(null);
  const [phase, setPhase] = useState<Phase>("loading");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    const res = await fetchJournals(token);
    if (res.ok) {
      setJournals(res.data);
      setPhase("success");
    } else {
      setJournals(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  function statusLabel(s: string): string {
    const labels = t.common.status as Record<string, string>;
    return labels[s] ?? s;
  }

  async function reverse(j: Journal) {
    if (!token) return;
    setBusyId(j.id);
    setActionError(null);
    const res = await reverseJournal(token, j.id);
    setBusyId(null);
    if (!res.ok) {
      setActionError(res.code === "NETWORK" ? t.common.networkError : `${t.journals.actionFailed}: ${res.message}`);
      return;
    }
    await load();
  }

  if (!ready || phase === "loading") return <LoadingSkeleton rows={6} />;
  if (phase === "error") return <ErrorState message={t.journals.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.journals.title}</h1>
        <p className="t-caption">{t.journals.subtitle}</p>
      </div>
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      <section className="card">
        {journals === null ? null : journals.length === 0 ? (
          <EmptyState title={t.journals.empty} />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{t.journals.id}</th>
                  <th scope="col">{t.journals.memo}</th>
                  <th scope="col">{t.journals.status}</th>
                  <th scope="col">{t.journals.created}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {journals.map((j) => (
                  <Fragment key={j.id}>
                    <tr>
                      <td data-label={t.journals.id} className="mono">{j.id}</td>
                      <td data-label={t.journals.memo}>{j.memo}</td>
                      <td data-label={t.journals.status}><Badge tone={statusTone(j.status)}>{statusLabel(j.status)}</Badge></td>
                      <td data-label={t.journals.created}><span className="t-caption">{fmtDate(j.createdAt, locale)}</span></td>
                      <td data-label={t.journals.actions}>
                        <div className="row-actions">
                          <Button small variant="secondary" aria-expanded={expandedId === j.id} onClick={() => setExpandedId(expandedId === j.id ? null : j.id)}>
                            {expandedId === j.id ? t.journals.hideLines : t.journals.showLines}
                          </Button>
                          {j.status === "Posted" ? (
                            <Button small loading={busyId === j.id} disabled={busyId !== null} onClick={() => { if (window.confirm(t.journals.confirmReverse)) void reverse(j); }}>
                              {t.journals.reverse}
                            </Button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                    {expandedId === j.id ? (
                      <tr className="sub-row">
                        <td colSpan={5}>
                          <table className="table table--lines">
                            <thead>
                              <tr>
                                <th scope="col">{t.journals.line.account}</th>
                                <th scope="col">{t.journals.line.name}</th>
                                <th scope="col">{t.journals.line.debit}</th>
                                <th scope="col">{t.journals.line.credit}</th>
                              </tr>
                            </thead>
                            <tbody>
                              {j.lines.map((ln, idx) => (
                                <tr key={idx}>
                                  <td data-label={t.journals.line.account} className="mono">{ln.accountCode}</td>
                                  <td data-label={t.journals.line.name}>{ln.accountName}</td>
                                  <td data-label={t.journals.line.debit} className="mono">{fmtMoney(ln.debit, locale, "BDT")}</td>
                                  <td data-label={t.journals.line.credit} className="mono">{fmtMoney(ln.credit, locale, "BDT")}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}