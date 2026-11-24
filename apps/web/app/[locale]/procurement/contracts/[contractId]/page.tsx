"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import { fetchMe } from "@/lib/services/api";
import {
  activateContract,
  exerciseRenewal,
  fetchContractDetail,
  fetchContracts,
  procError,
  type Contract,
  type ContractAmendment,
  type ContractObligation,
  type ContractPriceLine,
} from "@/lib/services/procurement";
import { useAuth } from "@/hooks/useAuth";
import { Badge, Button, LoadingSkeleton, Modal } from "@/components/uikit";
import { CONTRACT_STATUS_TONES, hasPerm, isoDate, statusLabel, type ContractCtx } from "../../_lib/ui";
import { AmendmentsSection } from "./_sections/amendments";
import { ObligationsSection } from "./_sections/obligations";

const EMPTY_LISTS = {
  amendments: [] as ContractAmendment[],
  priceLines: [] as ContractPriceLine[],
  obligations: [] as ContractObligation[],
};

export default function ContractWorkspacePage() {
  const params = useParams<{ locale: string; contractId: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const contractId = params.contractId;
  const t = getDictionary(locale);
  const c = t.procurement.contracts.workspace;
  const { token, ready } = useAuth(locale);

  const [contract, setContract] = useState<Contract | null>(null);
  const [amendments, setAmendments] = useState<ContractAmendment[]>(EMPTY_LISTS.amendments);
  const [priceLines, setPriceLines] = useState<ContractPriceLine[]>(EMPTY_LISTS.priceLines);
  const [obligations, setObligations] = useState<ContractObligation[]>(EMPTY_LISTS.obligations);
  const [me, setMe] = useState<{ id: string; perms: string[] } | null>(null);
  const [phase, setPhase] = useState<"loading" | "notFound" | "error" | "success">("loading");
  const [sessionOnly, setSessionOnly] = useState(false);

  const [activateOpen, setActivateOpen] = useState(false);
  const [renewOpen, setRenewOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [acting, setActing] = useState(false);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchMe(token).then((res) => {
      if (!cancelled && res.ok) setMe({ id: res.data.user.id, perms: res.data.permissions });
    });
    return () => { cancelled = true; };
  }, [token]);

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    // Depth comes from GET /contracts/{id} once it lands; until then the list row
    // carries the overview and amendments/obligations stay in-session.
    const detailRes = await fetchContractDetail(token, contractId);
    if (detailRes.ok) {
      const { amendments: a, obligations: o, priceLines: pl, ...row } = detailRes.data;
      setContract(row);
      setAmendments(a);
      setPriceLines(pl);
      setObligations(o);
      setSessionOnly(false);
      setPhase("success");
      return;
    }
    const listRes = await fetchContracts(token);
    const row = listRes.ok ? listRes.data.find((x) => x.id === contractId) : undefined;
    if (row) {
      setContract(row);
      setAmendments([]);
      setPriceLines([]);
      setObligations([]);
      setSessionOnly(true);
      setPhase("success");
    } else {
      setContract(null);
      setPhase(listRes.ok ? "notFound" : "error");
    }
  }, [token, contractId]);
  useEffect(() => { void load(); }, [load]);

  const canApprove = me ? hasPerm(me.perms, "contracts:approve") : false;
  const isMaker = me !== null && contract !== null && contract.createdBy === me.id;
  // Zero price lines is knowable only from the detail read; a degraded session (no detail
  // permission) leaves the decision to the server, which answers CONTRACT_NO_PRICE_LINES.
  const canActivate = canApprove && contract?.status === "Draft" && !isMaker && (sessionOnly || priceLines.length > 0);
  const optionsLeft = contract !== null && contract.optionPeriodsTotal !== null && contract.optionsExercised < contract.optionPeriodsTotal;
  const canRenew = canApprove && contract?.status === "Active" && !isMaker && optionsLeft;

  const errMap: Record<string, string> = {
    CONTRACT_APPROVAL_ERROR: c.overview.makerChecker,
    CONTRACT_NO_PRICE_LINES: c.overview.noPriceLines,
    CONTRACT_STATE_ERROR: t.procurement.common.forbidden,
    CONTRACT_NO_OPTION: c.renewal.noOption,
    FORBIDDEN: t.procurement.common.forbidden,
  };

  async function activate() {
    if (!token || !contract) return;
    setActing(true);
    setActionError(null);
    const res = await activateContract(token, contract.id);
    setActing(false);
    if (!res.ok) {
      setActionError(procError(res.code, res.message, errMap, c.overview.activateFailed));
      return;
    }
    setContract(res.data);
    setActivateOpen(false);
  }

  async function renew() {
    if (!token || !contract) return;
    setActing(true);
    setActionError(null);
    const res = await exerciseRenewal(token, contract.id, contract.optionsExercised + 1);
    setActing(false);
    if (!res.ok) {
      setActionError(procError(res.code, res.message, errMap, c.renewal.failed));
      return;
    }
    setContract(res.data);
    setRenewOpen(false);
  }

  if (!ready || token === null || (phase === "loading" && contract === null)) return <LoadingSkeleton rows={6} />;

  if (phase === "error" || contract === null) {
    return (
      <div className="stack">
        <div className="page-header">
          <h1 className="t-display">{c.loadFailed}</h1>
          <p className="t-caption"><Link href={`/${locale}/procurement/contracts`}>{c.backToList}</Link></p>
        </div>
        <div className="state state--error" role="alert">
          <p className="state__title">{phase === "notFound" ? c.notFound : c.loadFailed}</p>
          <Button variant="secondary" onClick={() => void load()}>{t.common.retry}</Button>
        </div>
      </div>
    );
  }

  const ov = c.overview;
  const rn = c.renewal;
  const ctx: ContractCtx = {
    token,
    locale,
    t,
    contract,
    amendments,
    priceLines,
    obligations,
    meId: me?.id ?? null,
    perms: me?.perms ?? [],
    setAmendments,
    setObligations,
    onContract: setContract,
  };

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{contract.id}</h1>
        <p className="t-caption"><Link href={`/${locale}/procurement/contracts`}>{c.backToList}</Link></p>
      </div>
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      {sessionOnly ? <p className="banner" role="status">{c.sessionOnly}</p> : null}

      <section className="card">
        <h2 className="t-title">
          {ov.title} <Badge tone={CONTRACT_STATUS_TONES[contract.status] ?? "muted"}>{statusLabel(t, contract.status)}</Badge>
          {contract.noticeDue ? <Badge tone="warning">{c.overview.noticeDue}</Badge> : null}
        </h2>
        <div className="form-grid">
          <p className="t-caption">{ov.supplier}: <span className="mono">{contract.supplierId}</span></p>
          <p className="t-caption">{ov.currency}: <span className="mono">{contract.currency}</span></p>
          <p className="t-caption">{ov.validity}: <span className="mono">{isoDate(contract.effectiveFrom)} → {isoDate(contract.effectiveTo)}</span></p>
          <p className="t-caption">{ov.owner}: <span className="mono">{contract.ownerUserId}</span></p>
          <p className="t-caption">{ov.activatedBy}: {contract.activatedBy ? <span className="mono">{contract.activatedBy}</span> : "—"}{contract.activatedAt ? ` · ${isoDate(contract.activatedAt)}` : ""}</p>
          <p className="t-caption">{ov.award}: {contract.awardId ? <span className="mono">{contract.awardId}</span> : "—"}</p>
          <p className="t-caption">{ov.options}: <span className="mono">{contract.optionsExercised}/{contract.optionPeriodsTotal ?? 0}</span></p>
        </div>
        {canApprove ? (
          isMaker ? (
            <p className="banner" role="status">{ov.makerChecker}</p>
          ) : (
            <>
              {contract.status === "Draft" && !sessionOnly && priceLines.length === 0 ? (
                <p className="banner" role="status">{ov.noPriceLines}</p>
              ) : null}
              <div className="row-actions" style={{ justifyContent: "flex-start" }}>
                {contract.status === "Draft" ? (
                  <Button disabled={!canActivate} onClick={() => { setActionError(null); setActivateOpen(true); }}>{ov.activate}</Button>
                ) : null}
              </div>
            </>
          )
        ) : null}
      </section>

      <AmendmentsSection ctx={ctx} />

      <ObligationsSection ctx={ctx} />

      <section className="card">
        <h2 className="t-title">{rn.title}</h2>
        <p className="t-caption">{rn.exercised}: <span className="mono">{contract.optionsExercised}/{contract.optionPeriodsTotal ?? 0}</span></p>
        <p className="t-caption">{rn.next}: <span className="mono">{contract.optionsExercised + 1}</span></p>
        {!optionsLeft ? <p className="banner" role="status">{rn.noOption}</p> : null}
        {canApprove ? (
          isMaker ? (
            <p className="banner" role="status">{rn.makerChecker}</p>
          ) : (
            <div className="row-actions" style={{ justifyContent: "flex-start" }}>
              <Button disabled={!canRenew} onClick={() => { setActionError(null); setRenewOpen(true); }}>{rn.exercise}</Button>
            </div>
          )
        ) : null}
      </section>

      <Modal open={activateOpen} onClose={() => setActivateOpen(false)} title={ov.activateTitle} closeLabel={t.common.cancel} busy={acting}>
        <p>{ov.activateConsequence}</p>
        {/* API errors render inside the dialog — a page-level banner hides behind the scrim. */}
        {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setActivateOpen(false)}>{t.common.cancel}</Button>
          <Button loading={acting} onClick={() => void activate()}>{ov.activate}</Button>
        </div>
      </Modal>

      <Modal open={renewOpen} onClose={() => setRenewOpen(false)} title={rn.exerciseTitle} closeLabel={t.common.cancel} busy={acting}>
        <p>{rn.exerciseConsequence}</p>
        {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setRenewOpen(false)}>{t.common.cancel}</Button>
          <Button loading={acting} onClick={() => void renew()}>{rn.exercise}</Button>
        </div>
      </Modal>
    </div>
  );
}
