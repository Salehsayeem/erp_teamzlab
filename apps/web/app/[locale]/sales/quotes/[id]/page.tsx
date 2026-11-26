"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  acceptQuoteRevision,
  approveQuoteRevision,
  getQuote,
  revalidateQuote,
  type QuoteDetail,
  type QuoteRevisionRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay, hasPermission, salesActionError, salesStatusLabel, SalesMeError, useSalesMe } from "../../_lib/helpers";
import { localStatus, localTone } from "../../_lib/deferred";
import { fmtDate, fmtMoney } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  ToastRegion,
  type ToastItem,
  type BadgeTone,
} from "@/components/uikit";

const TONE_MAP: Record<string, BadgeTone> = {
  Accepted: "success",
  Expired: "danger",
  Rejected: "danger",
  PendingApproval: "warning",
};

function backLink(locale: Locale): string {
  return "/" + locale + "/sales/quotes";
}

export default function QuoteDetailPage() {
  const params = useParams<{ locale: string; id: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const quoteId = params.id;
  const t = getDictionary(locale);
  const tc = t.sales.quotes;
  const td = tc.detail;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [quote, setQuote] = useState<QuoteDetail | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "notFound" | "success">("loading");

  const [approveFor, setApproveFor] = useState<number | null>(null);
  const [rejectFor, setRejectFor] = useState<number | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [acceptOpen, setAcceptOpen] = useState(false);
  const [acceptRev, setAcceptRev] = useState<number | null>(null);
  const [acceptMethod, setAcceptMethod] = useState("Email");
  const [acceptEventId, setAcceptEventId] = useState("");
  const [acceptEvidence, setAcceptEvidence] = useState("");
  const [acceptErrors, setAcceptErrors] = useState<{ method?: string }>({});
  const [revalidateOpen, setRevalidateOpen] = useState(false);
  const [revalidateExpiry, setRevalidateExpiry] = useState("");
  const [revalidateReason, setRevalidateReason] = useState("");
  const [revalidateErrors, setRevalidateErrors] = useState<{ expiry?: string; reason?: string }>({});
  const [busy, setBusy] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);
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
    const res = await getQuote(token, quoteId);
    if (res.ok) {
      setQuote(res.data);
      setPhase("success");
    } else if (res.code === "QUOTE_NOT_FOUND" || res.code === "HTTP_404") {
      setQuote(null);
      setPhase("notFound");
    } else {
      setQuote(null);
      setPhase("error");
    }
  }, [token, quoteId]);
  useEffect(() => { void load(); }, [load]);

  async function runAction(run: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>): Promise<boolean> {
    setBusy(true);
    setModalError(null);
    const res = await run();
    setBusy(false);
    if (!res.ok) {
      setModalError(salesActionError(t, res.code, res.message));
      return false;
    }
    await load();
    return true;
  }

  async function confirmApprove(revision: number) {
    const ok = await runAction(() => approveQuoteRevision(token ?? "", quoteId, revision, null));
    if (ok) {
      setApproveFor(null);
      pushToast("success", td.approvedNote);
    }
  }

  async function submitReject(e: FormEvent) {
    e.preventDefault();
    if (!rejectReason.trim() || rejectFor === null || !token) return;
    const ok = await runAction(() => approveQuoteRevision(token, quoteId, rejectFor, rejectReason.trim()));
    if (ok) {
      setRejectFor(null);
      setRejectReason("");
      pushToast("info", td.rejectedNote);
    }
  }

  async function submitAccept(e: FormEvent) {
    e.preventDefault();
    if (acceptRev === null || !token) return;
    const errs: { method?: string } = {};
    if (!acceptMethod) errs.method = td.errors.methodRequired;
    setAcceptErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await runAction(() => acceptQuoteRevision(token, quoteId, acceptRev, {
      method: acceptMethod,
      ...(acceptEventId.trim() ? { externalEventId: acceptEventId.trim() } : {}),
      ...(acceptEvidence.trim() ? { evidenceRef: acceptEvidence.trim() } : {}),
    }));
    if (ok) {
      setAcceptOpen(false);
      pushToast("success", td.acceptedNote + " " + String(acceptRev));
      setAcceptEventId("");
      setAcceptEvidence("");
    }
  }

  async function submitRevalidate(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { expiry?: string; reason?: string } = {};
    if (!revalidateExpiry) errs.expiry = td.errors.expiryRequired;
    if (!revalidateReason.trim()) errs.reason = td.errors.reasonRequired;
    setRevalidateErrors(errs);
    if (Object.keys(errs).length > 0) return;
    const ok = await runAction(() => revalidateQuote(token, quoteId, { expiresAt: revalidateExpiry, reason: revalidateReason.trim() }));
    if (ok) {
      setRevalidateOpen(false);
      setRevalidateExpiry("");
      setRevalidateReason("");
      pushToast("success", td.revalidatedNote);
    }
  }

  const approveGate = hasPermission(permissions, "quotes:approve");
  const acceptGate = hasPermission(permissions, "quotes:write");

  if (!ready || (phase === "loading" && quote === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "notFound") return <EmptyState title={td.notFound} />;
  if (phase === "error" || quote === null) return <ErrorState message={td.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const expired = quote.expiresAt !== null && new Date(quote.expiresAt) < new Date(new Date().toDateString());
  const isIssuer = meId !== null && quote.createdBy === meId;
  const acceptedRev = quote.acceptedRevision;
  return (
    <div className="stack">
      <div className="page-header">
        <p className="t-caption"><Link href={backLink(locale)}>{td.backToList}</Link></p>
        <h1 className="t-display">{td.metaTitle} <span className="mono">{quote.id}</span></h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      {acceptedRev !== null ? <p className="banner" role="status">{td.acceptedBanner}: <span className="mono">{acceptedRev}</span></p> : null}
      {expired && quote.status !== "Accepted" ? (
        <section className="card">
          <h2 className="t-title">{td.revalidateTitle}</h2>
          <p className="t-caption">{td.revalidateHint}</p>
          {isIssuer ? (
            <p className="t-caption" role="status">{t.sales.makerChecker}</p>
          ) : (
            <Button disabled={!meReady || approveGate === null || busy} onClick={() => { setRevalidateOpen(true); setModalError(null); }}>{td.revalidate}</Button>
          )}
        </section>
      ) : null}

      <section className="card">
        <div className="form-grid">
          <p><span className="t-caption">{td.customer}: </span><span className="mono">{quote.customerId}</span></p>
          <p><span className="t-caption">{td.currency}: </span><span className="mono">{quote.currency}</span></p>
          <p><span className="t-caption">{td.terms}: </span>{quote.terms ?? "—"}</p>
          <p><span className="t-caption">{td.expiresAt}: </span><span className="t-caption">{quote.expiresAt ? fmtDay(quote.expiresAt, locale) : "—"}</span></p>
          <p><span className="t-caption">{td.status}: </span><Badge tone={localTone(TONE_MAP, quote.status)}>{localStatus(t, tc.statusLabels, quote.status)}</Badge></p>
          <p><span className="t-caption">{td.createdBy}: </span><span className="mono">{quote.createdBy}</span></p>
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{td.revisionsTitle}</h2>
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{td.revColumns.revision}</th>
                <th scope="col">{td.revColumns.status}</th>
                <th scope="col">{td.revColumns.subTotal}</th>
                <th scope="col">{td.revColumns.discount}</th>
                <th scope="col">{td.revColumns.tax}</th>
                <th scope="col">{td.revColumns.total}</th>
                <th scope="col">{td.revColumns.by}</th>
                <th scope="col">{t.journals.actions}</th>
              </tr>
            </thead>
            <tbody>
              {quote.revisions.map((rev) => {
                const maker = meId !== null && rev.createdBy === meId;
                return (
                  <tr key={rev.id}>
                    <td data-label={td.revColumns.revision} className="mono">{rev.revision}</td>
                    <td data-label={td.revColumns.status}><Badge tone={localTone(TONE_MAP, rev.status)}>{salesStatusLabel(t, rev.status)}</Badge></td>
                    <td data-label={td.revColumns.subTotal} className="mono">{fmtMoney(rev.subTotal, locale, quote.currency)}</td>
                    <td data-label={td.revColumns.discount} className="mono">{fmtMoney(rev.discountTotal, locale, quote.currency)}</td>
                    <td data-label={td.revColumns.tax} className="mono">{fmtMoney(rev.taxTotal, locale, quote.currency)}</td>
                    <td data-label={td.revColumns.total} className="mono">{fmtMoney(rev.total, locale, quote.currency)}</td>
                    <td data-label={td.revColumns.by} className="mono">{rev.createdBy}</td>
                    <td data-label={t.journals.actions}>
                      {rev.status === "PendingApproval" ? (
                        maker ? (
                          <p className="t-caption" role="status">{t.sales.makerChecker}</p>
                        ) : (
                          <div className="row-actions">
                            <Button small disabled={!meReady || approveGate === null || busy} onClick={() => { setApproveFor(rev.revision); setModalError(null); }}>{td.approve}</Button>
                            <Button small variant="danger" disabled={!meReady || approveGate === null || busy} onClick={() => { setRejectFor(rev.revision); setRejectReason(""); setModalError(null); }}>{td.reject}</Button>
                          </div>
                        )
                      ) : null}
                      {rev.status === "Approved" && acceptedRev === null && acceptGate !== false ? (
                        <Button small disabled={!meReady || busy} onClick={() => { setAcceptRev(rev.revision); setAcceptOpen(true); setModalError(null); }}>{td.acceptBtn}</Button>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>
      {quote.revisions.map((rev) => (
        <section className="card" key={"panel-" + rev.id}>
          <h2 className="t-title">{td.revisionLabel} <span className="mono">{rev.revision}</span> · <Badge tone={localTone(TONE_MAP, rev.status)}>{salesStatusLabel(t, rev.status)}</Badge></h2>
          <h3 className="t-body-strong">{td.approvalsTitle}</h3>
          {rev.approvals.length === 0 ? (
            <p className="t-caption">{td.noApprovals}</p>
          ) : (
            <div className="table-scroll">
              <table className="table table--lines">
                <thead>
                  <tr>
                    <th scope="col">{td.apprColumns.approver}</th>
                    <th scope="col">{td.apprColumns.status}</th>
                    <th scope="col">{td.apprColumns.reason}</th>
                    <th scope="col">{td.apprColumns.at}</th>
                  </tr>
                </thead>
                <tbody>
                  {rev.approvals.map((ap, i) => (
                    <tr key={rev.id + "-" + i}>
                      <td data-label={td.apprColumns.approver} className="mono">{ap.approverId}</td>
                      <td data-label={td.apprColumns.status}>{salesStatusLabel(t, ap.status)}</td>
                      <td data-label={td.apprColumns.reason}>{ap.reason ?? "—"}</td>
                      <td data-label={td.apprColumns.at}><span className="t-caption">{fmtDate(ap.createdAt, locale)}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <h3 className="t-body-strong">{td.evidenceTitle}</h3>
          {(rev.priceEvidence ?? []).length === 0 ? (
            <p className="t-caption">{td.noEvidence}</p>
          ) : (
            <div className="table-scroll">
              <table className="table table--lines">
                <thead>
                  <tr>
                    <th scope="col">{td.evColumns.sku}</th>
                    <th scope="col">{td.evColumns.book}</th>
                    <th scope="col">{td.evColumns.rule}</th>
                    <th scope="col">{td.evColumns.from}</th>
                    <th scope="col">{td.evColumns.to}</th>
                    <th scope="col">{td.evColumns.base}</th>
                    <th scope="col">{td.evColumns.source}</th>
                  </tr>
                </thead>
                <tbody>
                  {(rev.priceEvidence ?? []).map((ev, i) => (
                    <tr key={rev.id + "-ev-" + i}>
                      <td data-label={td.evColumns.sku} className="mono">{ev.itemSku}</td>
                      <td data-label={td.evColumns.book} className="mono">{ev.priceBookId ?? td.noBook}</td>
                      <td data-label={td.evColumns.rule} className="mono">{ev.rule}</td>
                      <td data-label={td.evColumns.from}><span className="t-caption">{fmtDay(ev.effectiveFrom, locale)}</span></td>
                      <td data-label={td.evColumns.to}><span className="t-caption">{ev.effectiveTo ? fmtDay(ev.effectiveTo, locale) : "—"}</span></td>
                      <td data-label={td.evColumns.base} className="mono">{fmtMoney(ev.baseValue, locale, quote.currency)}</td>
                      <td data-label={td.evColumns.source} className="mono">{ev.sourceRef}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ))}


      <section className="card">
        <h2 className="t-title">{td.acceptancesTitle}</h2>
        {quote.acceptances.length === 0 ? (
          <EmptyState title={td.noAcceptances} />
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">{td.accColumns.id}</th>
                  <th scope="col">{td.accColumns.revision}</th>
                  <th scope="col">{td.accColumns.method}</th>
                  <th scope="col">{td.accColumns.externalEvent}</th>
                  <th scope="col">{td.accColumns.evidence}</th>
                  <th scope="col">{td.accColumns.by}</th>
                  <th scope="col">{td.accColumns.at}</th>
                </tr>
              </thead>
              <tbody>
                {quote.acceptances.map((a) => (
                  <tr key={a.id}>
                    <td data-label={td.accColumns.id} className="mono">{a.id}</td>
                    <td data-label={td.accColumns.revision} className="mono">{a.revision}</td>
                    <td data-label={td.accColumns.method} className="mono">{a.method}</td>
                    <td data-label={td.accColumns.externalEvent} className="mono">{a.externalEventId ?? "—"}</td>
                    <td data-label={td.accColumns.evidence} className="mono">{a.evidenceRef ?? "—"}</td>
                    <td data-label={td.accColumns.by} className="mono">{a.acceptedBy}</td>
                    <td data-label={td.accColumns.at}><span className="t-caption">{fmtDate(a.acceptedAt, locale)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={approveFor !== null} onClose={() => setApproveFor(null)} title={td.approveConfirmTitle} closeLabel={t.common.cancel}>
        <p>{td.approveConfirm}</p>
        <p className="t-caption">{td.revisionLabel}: <span className="mono">{approveFor}</span></p>
        {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setApproveFor(null)}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => { if (approveFor !== null) void confirmApprove(approveFor); }}>{td.approve}</Button>
        </div>
      </Modal>

      <Modal open={rejectFor !== null} onClose={() => setRejectFor(null)} title={td.confirmReject} closeLabel={t.common.cancel}>
        <form onSubmit={(e) => void submitReject(e)}>
          <Input id="quote-reject-reason" label={td.rejectReason} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setRejectFor(null)}>{t.common.cancel}</Button>
            <Button type="submit" variant="danger" loading={busy} disabled={!rejectReason.trim()}>{td.confirmReject}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={acceptOpen} onClose={() => setAcceptOpen(false)} title={td.acceptConfirmTitle} closeLabel={t.common.cancel}>
        <p>{td.acceptConfirm}</p>
        <p className="t-caption">{td.revisionLabel}: <span className="mono">{acceptRev}</span></p>
        <form onSubmit={(e) => void submitAccept(e)}>
          <div className="form-grid">
            <Select
              id="quote-accept-method"
              label={td.method}
              value={acceptMethod}
              onChange={(e) => setAcceptMethod(e.target.value)}
              options={[
                { value: "Email", label: td.methodOptions.Email },
                { value: "Paper", label: td.methodOptions.Paper },
                { value: "Portal", label: td.methodOptions.Portal },
              ]}
            />
            <Input id="quote-accept-event" label={td.externalEventId} value={acceptEventId} onChange={(e) => setAcceptEventId(e.target.value)} />
            <Input id="quote-accept-evidence" label={td.evidenceRef} value={acceptEvidence} onChange={(e) => setAcceptEvidence(e.target.value)} />
            {acceptErrors.method ? <p className="field__error">{acceptErrors.method}</p> : null}
          </div>
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setAcceptOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{td.acceptBtn}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={revalidateOpen} onClose={() => setRevalidateOpen(false)} title={td.revalidateTitle} closeLabel={t.common.cancel}>
        <p>{td.revalidateHint}</p>
        <form onSubmit={(e) => void submitRevalidate(e)}>
          <div className="form-grid">
            <Input id="quote-revalidate-expiry" label={td.newExpiry} type="date" value={revalidateExpiry} onChange={(e) => setRevalidateExpiry(e.target.value)} error={revalidateErrors.expiry} />
            <Input id="quote-revalidate-reason" label={td.revalidateReason} value={revalidateReason} onChange={(e) => setRevalidateReason(e.target.value)} error={revalidateErrors.reason} />
          </div>
          {modalError ? <p className="banner" role="alert">{modalError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setRevalidateOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{td.revalidateBtn}</Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
