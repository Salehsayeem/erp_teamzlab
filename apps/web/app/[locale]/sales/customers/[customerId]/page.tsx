"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  addOnboardingCheck,
  fetchCustomers,
  getCustomer360,
  markOnboardingCheckMet,
  mergeExecute,
  mergePreview,
  setCustomerHierarchy,
  type CustomerRow,
  type Customer360,
  type MergePreviewRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { hasPermission, salesActionError, salesStatusLabel, SalesMeError, useSalesMe } from "../../_lib/helpers";
import { localStatus, localTone } from "../../_lib/deferred";
import { fmtDate, fmtMoney, fmtNumber } from "@/lib/format";
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
  Met: "success",
  Unmet: "warning",
  Previewed: "warning",
  Executed: "success",
};

function backLink(locale: Locale): string {
  return "/" + locale + "/sales/customers";
}

export default function Customer360Page() {
  const params = useParams<{ locale: string; customerId: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const customerId = params.customerId;
  const t = getDictionary(locale);
  const tc = t.sales.customer360;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [customer, setCustomer] = useState<Customer360 | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "notFound" | "success">("loading");
  const [customers, setCustomers] = useState<CustomerRow[]>([]);

  const [parentId, setParentId] = useState("");
  const [parentBusy, setParentBusy] = useState(false);
  const [parentError, setParentError] = useState<string | null>(null);

  const [checkCode, setCheckCode] = useState("");
  const [mandatory, setMandatory] = useState(true);
  const [checkError, setCheckError] = useState<string | null>(null);
  const [addingCheck, setAddingCheck] = useState(false);
  const [metBusyId, setMetBusyId] = useState<string | null>(null);

  const [mergeOpen, setMergeOpen] = useState(false);
  const [survivorId, setSurvivorId] = useState("");
  const [mergeReason, setMergeReason] = useState("");
  const [mergeErrors, setMergeErrors] = useState<{ survivor?: string; reason?: string }>({});
  const [preview, setPreview] = useState<MergePreviewRow | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [execBusy, setExecBusy] = useState(false);
  const [mergeModalError, setMergeModalError] = useState<string | null>(null);

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
    const res = await getCustomer360(token, customerId);
    if (res.ok) {
      setCustomer(res.data);
      setParentId(res.data.hierarchy.parentId ?? "");
      setPhase("success");
    } else if (res.code === "CUSTOMER_NOT_FOUND" || res.code === "HTTP_404") {
      setCustomer(null);
      setPhase("notFound");
    } else {
      setCustomer(null);
      setPhase("error");
    }
  }, [token, customerId]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  async function runCardAction(run: () => Promise<{ ok: true; data: unknown } | { ok: false; code: string; message: string }>): Promise<string | null> {
    const res = await run();
    if (!res.ok) return salesActionError(t, res.code, res.message);
    await load();
    return null;
  }

  async function submitParent(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    setParentBusy(true);
    setParentError(null);
    const err = await runCardAction(() => setCustomerHierarchy(token, customerId, parentId ? parentId : null));
    setParentBusy(false);
    if (err) setParentError(err);
    else pushToast("success", tc.parentSaved);
  }

  async function submitCheck(e: FormEvent) {
    e.preventDefault();
    if (!token || !checkCode.trim()) return;
    setAddingCheck(true);
    setCheckError(null);
    const err = await runCardAction(() => addOnboardingCheck(token, customerId, { checkCode: checkCode.trim(), mandatory }));
    setAddingCheck(false);
    if (err) setCheckError(err);
    else {
      setCheckCode("");
      pushToast("success", tc.checkAdded);
    }
  }

  async function markMet(checkCodeToMet: string) {
    if (!token) return;
    setMetBusyId(checkCodeToMet);
    const err = await runCardAction(() => markOnboardingCheckMet(token, customerId, checkCodeToMet));
    setMetBusyId(null);
    if (err) setCheckError(err);
    else pushToast("success", tc.checkMetNote);
  }

  async function submitPreview(e: FormEvent) {
    e.preventDefault();
    if (!token) return;
    const errs: { survivor?: string; reason?: string } = {};
    if (!survivorId) errs.survivor = tc.survivorRequired;
    if (!mergeReason.trim()) errs.reason = tc.mergeReasonRequired;
    if (survivorId === customerId) errs.survivor = tc.errors.sameCustomer;
    setMergeErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setPreviewBusy(true);
    setMergeModalError(null);
    const res = await mergePreview(token, { survivorId, mergedId: customerId, reason: mergeReason.trim() });
    setPreviewBusy(false);
    if (!res.ok) {
      setMergeModalError(salesActionError(t, res.code, res.message));
      return;
    }
    setPreview(res.data);
  }

  async function confirmExecute() {
    if (!token || preview === null) return;
    setExecBusy(true);
    setMergeModalError(null);
    const res = await mergeExecute(token, preview.id);
    setExecBusy(false);
    if (!res.ok) {
      setMergeModalError(salesActionError(t, res.code, res.message));
      return;
    }
    setPreview(null);
    setMergeOpen(false);
    setMergeReason("");
    pushToast("success", tc.mergedNote);
  }

  const writeGate = hasPermission(permissions, "customers:write");
  const mergeGate = hasPermission(permissions, "customers:merge");

  if (!ready || (phase === "loading" && customer === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "notFound") return <EmptyState title={tc.notFound} />;
  if (phase === "error" || customer === null) return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const otherCustomers = customers.filter((c) => c.id !== customerId);
  return (
    <div className="stack">
      <div className="page-header">
        <p className="t-caption"><Link href={backLink(locale)}>{tc.backToList}</Link></p>
        <h1 className="t-display">{tc.title}: <span className="mono">{customer.name}</span></h1>
        <p className="t-caption">{t.sales.customers.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      {customer.mergedIntoId ? (
        <p className="banner" role="status">{tc.mergedBanner} <span className="mono">{customer.mergedIntoId}</span></p>
      ) : null}

      <section className="card">
        <h2 className="t-title">{tc.profileTitle}</h2>
        <div className="form-grid">
          <p><span className="t-caption">{tc.name}: </span>{customer.name}</p>
          <p><span className="t-caption">{tc.taxId}: </span><span className="mono">{customer.taxId}</span></p>
          <p><span className="t-caption">{tc.email}: </span><span className="mono">{customer.email}</span></p>
          <p><span className="t-caption">{tc.creditLimit}: </span><span className="mono">{customer.creditLimit === null ? "—" : fmtMoney(customer.creditLimit, locale, "BDT")}</span></p>
          <p><span className="t-caption">{tc.termDays}: </span><span className="mono">{customer.paymentTermNetDays}</span></p>
          {customer.mergedIntoId ? <p><span className="t-caption">{tc.mergedInto}: </span><span className="mono">{customer.mergedIntoId}</span></p> : null}
        </div>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.hierarchyTitle}</h2>
        {customer.mergedIntoId ? (
          <p className="t-caption" role="status">{tc.mergedBanner}</p>
        ) : (
          <>
            <div className="form-grid">
              <p><span className="t-caption">{tc.parent}: </span><span className="mono">{customer.hierarchy.parentId ?? tc.noParent}</span></p>
              <p><span className="t-caption">{tc.children}: </span>{customer.hierarchy.childrenIds.length === 0 ? tc.noChildren : <span className="mono">{customer.hierarchy.childrenIds.join(", ")}</span>}</p>
              <p><span className="t-caption">{tc.includedEntities}: </span><span className="mono">{customer.hierarchy.includedEntityIds.join(", ")}</span></p>
              <p><span className="t-caption">{tc.rollupTotal}: </span><span className="mono">{fmtMoney(customer.hierarchy.rollupOpenOrderTotal, locale, "BDT")}</span></p>
            </div>
            <form onSubmit={(e) => void submitParent(e)}>
              <div className="form-grid">
                <Select
                  id="c360-parent"
                  label={tc.parentPicker}
                  value={parentId}
                  onChange={(e) => setParentId(e.target.value)}
                  options={[{ value: "", label: tc.parentNone }, ...otherCustomers.map((c) => ({ value: c.id, label: c.name + " (" + c.id + ")" }))]}
                />
              </div>
              {parentError ? <p className="banner" role="alert">{parentError}</p> : null}
              <p><Button type="submit" loading={parentBusy} disabled={writeGate === false}>{tc.setParent}</Button></p>
            </form>
          </>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.onboardingTitle}</h2>
        {customer.onboardingChecks.length === 0 ? (
          <p className="t-caption">{tc.noChecks}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.checkColumns.code}</th>
                  <th scope="col">{tc.checkColumns.mandatory}</th>
                  <th scope="col">{tc.checkColumns.status}</th>
                  <th scope="col">{tc.checkColumns.metAt}</th>
                  <th scope="col">{t.journals.actions}</th>
                </tr>
              </thead>
              <tbody>
                {customer.onboardingChecks.map((chk) => (
                  <tr key={chk.checkCode}>
                    <td data-label={tc.checkColumns.code} className="mono">{chk.checkCode}</td>
                    <td data-label={tc.checkColumns.mandatory}>{chk.mandatory ? t.common.yes : t.common.no}</td>
                    <td data-label={tc.checkColumns.status}><Badge tone={localTone(TONE_MAP, chk.status)}>{localStatus(t, tc.statusLabels, chk.status)}</Badge></td>
                    <td data-label={tc.checkColumns.metAt}><span className="t-caption">{chk.metAt ? fmtDate(chk.metAt, locale) : "—"}</span></td>
                    <td data-label={t.journals.actions}>
                      {chk.status !== "Met" ? (
                        <Button small disabled={!meReady || metBusyId !== null} loading={metBusyId === chk.checkCode} onClick={() => void markMet(chk.checkCode)}>{tc.markMet}</Button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <form onSubmit={(e) => void submitCheck(e)}>
          <h3 className="t-body-strong">{tc.addCheckTitle}</h3>
          <div className="form-grid">
            <Input id="c360-check-code" label={tc.checkCode} value={checkCode} onChange={(e) => setCheckCode(e.target.value)} />
            <Select
              id="c360-check-mandatory"
              label={tc.mandatory}
              value={mandatory ? "yes" : "no"}
              onChange={(e) => setMandatory(e.target.value === "yes")}
              options={[{ value: "yes", label: t.common.yes }, { value: "no", label: t.common.no }]}
            />
          </div>
          {checkError ? <p className="banner" role="alert">{checkError}</p> : null}
          <p><Button type="submit" loading={addingCheck} disabled={!checkCode.trim() || writeGate === false}>{tc.addCheck}</Button></p>
        </form>
      </section>

      <section className="card">
        <h2 className="t-title">{tc.summaryTitle}</h2>
        <div className="tiles">
          <div className="kpi">
            <p className="kpi__label">{tc.ordersTitle}</p>
            <p className="kpi__value">{fmtNumber(customer.openOrders.length, locale)}</p>
          </div>
          <div className="kpi">
            <p className="kpi__label">{tc.invoicesCount}</p>
            <p className="kpi__value">{fmtNumber(customer.openInvoiceCount, locale)}</p>
          </div>
          <div className="kpi">
            <p className="kpi__label">{tc.invoicesTotal}</p>
            <p className="kpi__value">{fmtMoney(customer.openInvoiceTotal, locale, "BDT")}</p>
          </div>
        </div>
        {customer.openOrders.length === 0 ? (
          <p className="t-caption">{tc.noOpenOrders}</p>
        ) : (
          <div className="table-scroll">
            <table className="table table--lines">
              <thead>
                <tr>
                  <th scope="col">{tc.ordersColumns.order}</th>
                  <th scope="col">{tc.ordersColumns.status}</th>
                  <th scope="col">{tc.ordersColumns.total}</th>
                </tr>
              </thead>
              <tbody>
                {customer.openOrders.map((o) => (
                  <tr key={o.id}>
                    <td data-label={tc.ordersColumns.order} className="mono"><Link href={"/" + locale + "/sales/orders/" + o.id}>{o.id}</Link></td>
                    <td data-label={tc.ordersColumns.status}><Badge tone="muted">{salesStatusLabel(t, o.status)}</Badge></td>
                    <td data-label={tc.ordersColumns.total} className="mono">{fmtMoney(o.total, locale, "BDT")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2 className="t-title">{tc.exposureTitle}</h2>
        {customer.exposure === null || !customer.exposure.hasData ? (
          <p className="t-caption">{tc.noExposure}</p>
        ) : (
          <div className="tiles">
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.limit}</p>
              <p className="kpi__value">{customer.exposure.creditLimit === null ? "—" : fmtMoney(customer.exposure.creditLimit, locale, "BDT")}</p>
            </div>
            <div className="kpi">
              <p className="kpi__label">{tc.exposureTiles.total}</p>
              <p className="kpi__value">{fmtMoney(customer.exposure.total, locale, "BDT")}</p>
            </div>
          </div>
        )}
      </section>

      {customer.mergedIntoId ? null : (
        <section className="card">
          <h2 className="t-title">{tc.mergeTitle}</h2>
          <p className="t-caption">{tc.mergeHint}</p>
          <p><Button variant="danger" disabled={mergeGate === false} title={mergeGate === false ? tc.mergeForbidden : undefined} onClick={() => { setMergeOpen(true); setPreview(null); setMergeModalError(null); }}>{tc.mergeBtn}</Button></p>
          {mergeGate === false ? <p className="t-caption" role="status">{tc.mergeForbidden}</p> : null}
        </section>
      )}

      <Modal open={mergeOpen} onClose={() => setMergeOpen(false)} title={tc.mergeTitle} closeLabel={t.common.cancel}>
        {preview === null ? (
          <form onSubmit={(e) => void submitPreview(e)}>
            <p className="t-caption">{tc.mergeHint}</p>
            <div className="form-grid">
              <Select
                id="c360-survivor"
                label={tc.survivor}
                value={survivorId}
                onChange={(e) => setSurvivorId(e.target.value)}
                options={otherCustomers.map((c) => ({ value: c.id, label: c.name + " (" + c.id + ")" }))}
                placeholder={tc.survivor}
                error={mergeErrors.survivor}
              />
              <Input id="c360-merge-reason" label={tc.mergeReason} value={mergeReason} onChange={(e) => setMergeReason(e.target.value)} error={mergeErrors.reason} />
            </div>
            {mergeModalError ? <p className="banner" role="alert">{mergeModalError}</p> : null}
            <div className="modal__actions">
              <Button type="button" variant="secondary" onClick={() => setMergeOpen(false)}>{t.common.cancel}</Button>
              <Button type="submit" loading={previewBusy}>{tc.previewBtn}</Button>
            </div>
          </form>
        ) : (
          <>
            <h3 className="t-body-strong">{tc.previewTitle}</h3>
            <p className="t-caption">{tc.previewHint}</p>
            <p><span className="t-caption">{tc.survivor}: </span><span className="mono">{preview.survivorId}</span></p>
            <h3 className="t-body-strong">{tc.reassignments}</h3>
            <ul>
              <li>{tc.reassignOrders}: {preview.reassignments["OpenOrders"] ?? preview.reassignments["openOrders"] ?? 0}</li>
              <li>{tc.reassignOpportunities}: {preview.reassignments["OpenOpportunities"] ?? preview.reassignments["openOpportunities"] ?? 0}</li>
              <li>{tc.reassignCases}: {preview.reassignments["OpenCases"] ?? preview.reassignments["openCases"] ?? 0}</li>
              <li>{tc.reassignChecks}: {preview.reassignments["OnboardingChecks"] ?? preview.reassignments["onboardingChecks"] ?? 0}</li>
            </ul>
            {mergeModalError ? <p className="banner" role="alert">{mergeModalError}</p> : null}
            <div className="modal__actions">
              <Button variant="secondary" onClick={() => setPreview(null)}>{t.common.cancel}</Button>
              <Button variant="danger" loading={execBusy} onClick={() => void confirmExecute()}>{tc.executeBtn}</Button>
            </div>
          </>
        )}
      </Modal>
    </div>
  );
}
