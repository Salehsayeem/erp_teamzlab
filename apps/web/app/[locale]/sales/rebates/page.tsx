"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  approveAccrual,
  approveRebateClaim,
  calculateAccrual,
  createRebateAgreement,
  createRebateClaim,
  fetchCustomers,
  listRebateAgreements,
  type CustomerRow,
  type RebateAgreementRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { hasPermission, salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";
import { localStatus, localTone } from "../_lib/deferred";
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
  Active: "success",
  Retired: "muted",
  Calculated: "warning",
  Approved: "success",
  Submitted: "warning",
  Rejected: "danger",
};

export default function RebatesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.rebates;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [agreements, setAgreements] = useState<RebateAgreementRow[] | null>(null);
  const [agreementsLoadError, setAgreementsLoadError] = useState<string | null>(null);
  const [customers, setCustomers] = useState<CustomerRow[]>([]);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");

  const [custId, setCustId] = useState("");
  const [basis, setBasis] = useState("");
  const [ratePct, setRatePct] = useState("");
  const [accrualPeriod, setAccrualPeriod] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [agErrors, setAgErrors] = useState<{ customer?: string; basis?: string; rate?: string; period?: string; from?: string }>({});
  const [creating, setCreating] = useState(false);
  const [agError, setAgError] = useState<string | null>(null);

  const [busyId, setBusyId] = useState<string | null>(null);
  const [accrualError, setAccrualError] = useState<string | null>(null);
  const [accrualPeriodInput, setAccrualPeriodInput] = useState("");

  const [claimOpen, setClaimOpen] = useState(false);
  const [claimAgreement, setClaimAgreement] = useState("");
  const [claimPeriod, setClaimPeriod] = useState("");
  const [claimAmount, setClaimAmount] = useState("");
  const [claimTxns, setClaimTxns] = useState("");
  const [claimErrors, setClaimErrors] = useState<{ agreement?: string; period?: string; amount?: string }>({});
  const [claiming, setClaiming] = useState(false);
  const [claimError, setClaimError] = useState<string | null>(null);

  const [settleId, setSettleId] = useState("");
  const [settleApproveId, setSettleApproveId] = useState<string | null>(null);
  const [settleNote, setSettleNote] = useState("");
  const [settleBusy, setSettleBusy] = useState(false);
  const [settleModalError, setSettleModalError] = useState<string | null>(null);

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
    const res = await listRebateAgreements(token);
    if (res.ok) {
      setAgreements(res.data);
      setAgreementsLoadError(null);
      setPhase("success");
    } else {
      setAgreements(null);
      // reading agreements needs its own permission; settling only needs
      // rebates:settle — a 403 here must not gate the whole page (R-FIN dead-end)
      setAgreementsLoadError(salesActionError(t, res.code, res.message));
      setPhase("success");
    }
  }, [token, t]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  async function submitAgreement(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { customer?: string; basis?: string; rate?: string; period?: string; from?: string } = {};
    if (!custId) errs.customer = tc.customerRequired;
    if (!basis.trim()) errs.basis = tc.basisRequired;
    if (!Number.isFinite(Number(ratePct)) || Number(ratePct) < 0) errs.rate = tc.rateInvalid;
    if (!accrualPeriod.trim()) errs.period = tc.accrualPeriodRequired;
    if (!validFrom) errs.from = tc.validFromRequired;
    setAgErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setCreating(true);
    setAgError(null);
    const res = await createRebateAgreement(token, {
      customerId: custId,
      basis: basis.trim(),
      ratePct: Number(ratePct),
      accrualPeriod: accrualPeriod.trim(),
      validFrom,
    });
    setCreating(false);
    if (!res.ok) {
      setAgError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.id);
    setBasis("");
    setRatePct("");
    await load();
  }

  async function accrualAction(agreementId: string, period: string, approve: boolean) {
    if (!token || !period.trim()) return;
    setBusyId(agreementId + period);
    setAccrualError(null);
    const res = approve ? await approveAccrual(token, agreementId, period) : await calculateAccrual(token, agreementId, period);
    setBusyId(null);
    if (!res.ok) {
      const error = salesActionError(t, res.code, res.message);
      setAccrualError(error);
      // the page banner sits above the agreements table — a row-action failure
      // must also reach the fixed toast region to be visible at the row (clickthrough F9)
      pushToast("error", error);
      return;
    }
    pushToast("success", approve ? tc.accrualApproved : tc.accrualCalculated);
    await load();
  }

  async function submitClaim(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { agreement?: string; period?: string; amount?: string } = {};
    if (!claimAgreement) errs.agreement = tc.agreementRequired;
    if (!claimPeriod.trim()) errs.period = tc.periodRequired;
    const num = Number(claimAmount);
    if (!claimAmount.trim() || !Number.isFinite(num) || num <= 0) errs.amount = tc.amountInvalid;
    setClaimErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setClaiming(true);
    setClaimError(null);
    const res = await createRebateClaim(token, {
      agreementId: claimAgreement,
      period: claimPeriod.trim(),
      amount: num,
      eligibleTxnIds: claimTxns.split(",").map((s) => s.trim()).filter((s) => s.length > 0),
    });
    setClaiming(false);
    if (!res.ok) {
      setClaimError(salesActionError(t, res.code, res.message));
      return;
    }
    setClaimOpen(false);
    setClaimPeriod("");
    setClaimAmount("");
    setClaimTxns("");
    pushToast("success", tc.claimCreated + " " + res.data.id);
  }


  // Settlement is approve-only: the API has no claim-reject semantics yet
  // (ApproveClaim unconditionally approves; claims-reject is ledgered with
  // the claims-GET read gap for the next slice).
  async function confirmSettle() {
    if (!token || settleApproveId === null) return;
    setSettleBusy(true);
    setSettleModalError(null);
    const res = await approveRebateClaim(token, settleApproveId, settleNote.trim() || null);
    setSettleBusy(false);
    if (!res.ok) {
      setSettleModalError(salesActionError(t, res.code, res.message));
      return;
    }
    setSettleApproveId(null);
    setSettleNote("");
    pushToast("success", tc.claimApproved);
  }

  function openSettle() {
    if (!settleId.trim()) return;
    setSettleApproveId(settleId.trim());
    setSettleNote("");
    setSettleModalError(null);
  }

  const writeGate = hasPermission(permissions, "rebates:write");
  const settleGate = hasPermission(permissions, "rebates:settle");

  if (!ready || (phase === "loading" && agreements === null)) return <LoadingSkeleton rows={7} />;

  const list = agreements ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}
      {accrualError ? <p className="banner" role="alert">{accrualError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.agreementsTitle}</h2>
        {agreementsLoadError ? (
          <ErrorState message={agreementsLoadError} retryLabel={t.common.retry} onRetry={() => void load()} />
        ) : list.length === 0 ? <EmptyState title={tc.noAgreements} /> : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.agreementColumns.id}</th>
                  <th scope="col">{tc.agreementColumns.customer}</th>
                  <th scope="col">{tc.agreementColumns.basis}</th>
                  <th scope="col">{tc.agreementColumns.rate}</th>
                  <th scope="col">{tc.agreementColumns.period}</th>
                  <th scope="col">{tc.agreementColumns.from}</th>
                  <th scope="col">{tc.agreementColumns.status}</th>
                  <th scope="col">{tc.period}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {list.map((ag) => (
                  <tr key={ag.id}>
                    <td data-label={tc.agreementColumns.id} className="mono">{ag.id}</td>
                    <td data-label={tc.agreementColumns.customer} className="mono">{ag.customerId}</td>
                    <td data-label={tc.agreementColumns.basis} className="mono">{ag.basis}</td>
                    <td data-label={tc.agreementColumns.rate} className="mono">{ag.ratePct}%</td>
                    <td data-label={tc.agreementColumns.period} className="mono">{ag.accrualPeriod}</td>
                    <td data-label={tc.agreementColumns.from}><span className="t-caption">{fmtDate(ag.validFrom, locale)}</span></td>
                    <td data-label={tc.agreementColumns.status}><Badge tone={localTone(TONE_MAP, ag.status)}>{localStatus(t, tc.statusLabels, ag.status)}</Badge></td>
                    <td data-label={tc.period}>
                      <Input
                        id={`rb-period-${ag.id}`}
                        label={tc.period}
                        value={accrualPeriodInput}
                        onChange={(e) => setAccrualPeriodInput(e.target.value)}
                        placeholder="2026-11"
                      />
                    </td>
                    <td data-label={t.journals.actions}>
                      <div className="row-actions">
                        <Button small disabled={!accrualPeriodInput.trim() || busyId !== null} loading={busyId === ag.id + accrualPeriodInput} onClick={() => void accrualAction(ag.id, accrualPeriodInput.trim(), false)}>
                          {tc.calculateAccrual}
                        </Button>
                        <Button small variant="secondary" disabled={!meReady || !accrualPeriodInput.trim() || busyId !== null} loading={busyId === ag.id + accrualPeriodInput} onClick={() => void accrualAction(ag.id, accrualPeriodInput.trim(), true)}>
                          {tc.approveAccrual}
                        </Button>
                        <Button small variant="secondary" disabled={writeGate === false} onClick={() => { setClaimAgreement(ag.id); setClaimOpen(true); setClaimError(null); }}>
                          {tc.createClaim}
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.createTitle}</h2>
        <form onSubmit={(e) => void submitAgreement(e)}>
          <div className="form-grid">
            <Select
              id="rb-customer"
              label={tc.customer}
              value={custId}
              onChange={(e) => setCustId(e.target.value)}
              options={customers.map((c) => ({ value: c.id, label: c.name + " (" + c.id + ")" }))}
              placeholder={tc.customer}
              error={agErrors.customer}
            />
            <Input id="rb-basis" label={tc.basis} value={basis} onChange={(e) => setBasis(e.target.value)} error={agErrors.basis} />
            <Input id="rb-rate" label={tc.ratePct} type="number" min="0" step="any" value={ratePct} onChange={(e) => setRatePct(e.target.value)} error={agErrors.rate} />
            <Input id="rb-period" label={tc.accrualPeriod} value={accrualPeriod} onChange={(e) => setAccrualPeriod(e.target.value)} error={agErrors.period} />
            <Input id="rb-from" label={tc.validFrom} type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} error={agErrors.from} />
          </div>
          {agError ? <p className="banner" role="alert">{agError}</p> : null}
          <p><Button type="submit" loading={creating} disabled={writeGate === false}>{tc.create}</Button></p>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.settleTitle}</h2>
        <p className="t-caption">{tc.settleHint}</p>
        <div className="form-grid">
          <Input id="rb-settle-id" label={tc.claimId} value={settleId} onChange={(e) => setSettleId(e.target.value)} />
        </div>
        {settleGate === false ? <p className="t-caption" role="status">{tc.forbidden}</p> : null}
        <div className="row-actions" style={{ justifyContent: "flex-start" }}>
          <Button disabled={!meReady || settleGate === false || !settleId.trim()} onClick={() => openSettle()}>{tc.approveClaim}</Button>
        </div>
      </section>

      <Modal open={claimOpen} onClose={() => setClaimOpen(false)} title={tc.createClaim} closeLabel={t.common.cancel}>
        <p className="t-caption">{tc.eligibleTxns}</p>
        <form onSubmit={(e) => void submitClaim(e)}>
          <div className="form-grid">
            <Input id="rb-claim-agreement" label={tc.claimColumns.agreement} value={claimAgreement} onChange={(e) => setClaimAgreement(e.target.value)} error={claimErrors.agreement} />
            <Input id="rb-claim-period" label={tc.period} value={claimPeriod} onChange={(e) => setClaimPeriod(e.target.value)} error={claimErrors.period} />
            <Input id="rb-claim-amount" label={tc.amount} type="number" min="0" step="any" value={claimAmount} onChange={(e) => setClaimAmount(e.target.value)} error={claimErrors.amount} />
            <Input id="rb-claim-txns" label={tc.eligibleTxns} value={claimTxns} onChange={(e) => setClaimTxns(e.target.value)} />
          </div>
          {claimError ? <p className="banner" role="alert">{claimError}</p> : null}
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setClaimOpen(false)}>{t.common.cancel}</Button>
            <Button type="submit" loading={claiming}>{tc.createClaim}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={settleApproveId !== null} onClose={() => setSettleApproveId(null)} title={tc.approveConfirmTitle} closeLabel={t.common.cancel}>
        <p>{tc.approveConfirm}</p>
        <p className="t-caption">{tc.claimColumns.id}: <span className="mono">{settleApproveId}</span></p>
        <Input id="rb-settle-note" label={tc.settleNote} value={settleNote} onChange={(e) => setSettleNote(e.target.value)} />
        {settleModalError ? <p className="banner" role="alert">{settleModalError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setSettleApproveId(null)}>{t.common.cancel}</Button>
          <Button loading={settleBusy} onClick={() => void confirmSettle()}>{tc.approveClaim}</Button>
        </div>
      </Modal>
    </div>
  );
}
