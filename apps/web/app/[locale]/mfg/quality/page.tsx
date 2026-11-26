"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  fetchQualityPlans, createQualityPlan, addQualityCharacteristic, approveQualityPlan,
  createInspection, addInspectionResult, fetchInspectionResults, finalizeInspection,
  fetchQualityHolds, createQualityHold, disposeQualityHold,
  fetchNonconformances, raiseNonconformance, addContainment, releaseContainment, closeNonconformance,
  fetchCapaCases, createCapaCase, addCapaAction, verifyCapa, closeCapaCase,
  fetchQualityDashboard,
  type QualityPlanRow, type InspectionRow, type InspectionResultRow,
  type QualityHoldRow, type NonconformanceRow, type CapaRow, type QualityDashboardRow,
} from "@/lib/services/mfg-deferred";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge, Button, EmptyState, Input, LoadingSkeleton, Modal, Select, Table, Tabs,
  type TabItem, type Column,
} from "@/components/uikit";
import { hasPermission, deferredActionError, mfgStatusLabel, useMfgMe } from "../_lib/helpers";
import { PageShell, useToasts } from "../_lib/screen";

const CHAR_TYPES = ["Attribute", "Variable"];
const DISPOSITIONS = ["ReleaseToStock", "Scrap", "Rework", "Return"];

export default function QualityPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.mfg.quality;
  const { token, ready } = useAuth(locale);
  const { permissions, meId, meReady } = useMfgMe(token);
  const canPlan = hasPermission(permissions, "quality:plan");
  const canInspect = hasPermission(permissions, "quality:inspect");
  const canDispose = hasPermission(permissions, "quality:dispose");
  const { toasts, push, dismiss } = useToasts();

  const [tab, setTab] = useState("plans");
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [dashboard, setDashboard] = useState<QualityDashboardRow | null>(null);

  // plans
  const [plans, setPlans] = useState<QualityPlanRow[] | null>(null);
  const [planForm, setPlanForm] = useState({ itemSku: "", revision: "1", effectiveFrom: "" });
  const [planErrors, setPlanErrors] = useState<{ item?: string; rev?: string; from?: string }>({});
  const [openPlan, setOpenPlan] = useState<string>("");
  const [charForm, setCharForm] = useState({ code: "", name: "", dataType: "Variable", minValue: "", maxValue: "", mandatory: true });
  const [charErrors, setCharErrors] = useState<{ code?: string; name?: string }>({});
  const [approvePlanId, setApprovePlanId] = useState<string>("");

  // inspections (session list — the API exposes no inspection index read)
  const [inspections, setInspections] = useState<InspectionRow[]>([]);
  const [inspForm, setInspForm] = useState({ qualityPlanId: "", orderId: "", itemSku: "", lotSerial: "", populationQty: "" });
  const [inspErrors, setInspErrors] = useState<{ plan?: string; order?: string; item?: string; qty?: string }>({});
  const [openInsp, setOpenInsp] = useState<string>("");
  const [results, setResults] = useState<InspectionResultRow[] | null>(null);
  const [resultForm, setResultForm] = useState({ characteristicCode: "", rawValue: "" });
  const [resultErrors, setResultErrors] = useState<{ code?: string; value?: string }>({});
  const [finalizeId, setFinalizeId] = useState<string>("");

  // holds
  const [holds, setHolds] = useState<QualityHoldRow[] | null>(null);
  const [holdForm, setHoldForm] = useState({ itemSku: "", lotSerial: "", qty: "", orderId: "", inspectionId: "" });
  const [holdErrors, setHoldErrors] = useState<{ item?: string; qty?: string }>({});
  const [disposeTarget, setDisposeTarget] = useState<QualityHoldRow | null>(null);
  const [disposeForm, setDisposeForm] = useState({ disposition: "ReleaseToStock", qty: "", reason: "" });
  const [disposeErrors, setDisposeErrors] = useState<{ qty?: string }>({});

  // nonconformances
  const [ncs, setNcs] = useState<NonconformanceRow[] | null>(null);
  const [ncForm, setNcForm] = useState({ sourceEventId: "", title: "", itemSku: "", orderId: "", affectedQty: "", objectRef: "", objectQty: "" });
  const [ncErrors, setNcErrors] = useState<{ source?: string; title?: string; qty?: string; ref?: string }>({});
  const [ncContainment, setNcContainment] = useState<{ nc: NonconformanceRow; kind: string } | null>(null);
  const [ncRelease, setNcRelease] = useState<{ containmentId: string; reason: string } | null>(null);
  const [ncCloseId, setNcCloseId] = useState<string>("");

  // CAPA
  const [capas, setCapas] = useState<CapaRow[] | null>(null);
  const [capaForm, setCapaForm] = useState({ title: "", nonconformanceId: "", effectivenessCriteria: "" });
  const [capaErrors, setCapaErrors] = useState<{ title?: string; criteria?: string }>({});
  const [capaActionForm, setCapaActionForm] = useState<{ capaId: string; description: string }>({ capaId: "", description: "" });
  const [capaVerifyForm, setCapaVerifyForm] = useState<{ capaId: string; criteriaResult: string; satisfied: boolean }>({ capaId: "", criteriaResult: "", satisfied: true });
  const [capaCloseId, setCapaCloseId] = useState<string>("");

  const load = useCallback(async () => {
    if (!token) return;
    setPhase("loading");
    setErrorCode(null);
    const [p, h, n, c, d] = await Promise.all([
      fetchQualityPlans(token), fetchQualityHolds(token), fetchNonconformances(token),
      fetchCapaCases(token), fetchQualityDashboard(token),
    ]);
    if (!p.ok) { setErrorCode(p.code); setPhase("error"); return; }
    setPlans(p.data);
    setHolds(h.ok ? h.data : []);
    setNcs(n.ok ? n.data : []);
    setCapas(c.ok ? c.data : []);
    setDashboard(d.ok ? d.data : null);
    setPhase("success");
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  async function submitPlan(e: FormEvent) {
    e.preventDefault();
    const rev = Number(planForm.revision);
    const errs: { item?: string; rev?: string; from?: string } = {};
    if (!planForm.itemSku.trim()) errs.item = tc.errors.itemRequired;
    if (!planForm.revision.trim() || !Number.isInteger(rev) || rev < 1) errs.rev = tc.errors.revInvalid;
    if (!planForm.effectiveFrom) errs.from = tc.errors.itemRequired;
    setPlanErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await createQualityPlan(token, { itemSku: planForm.itemSku.trim(), revision: rev, effectiveFrom: planForm.effectiveFrom, effectiveTo: null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.planCreated + " " + res.data.id);
    setPlanForm({ itemSku: "", revision: "1", effectiveFrom: "" });
    await load();
  }

  async function submitCharacteristic(e: FormEvent) {
    e.preventDefault();
    if (!openPlan || !token) return;
    const errs: { code?: string; name?: string } = {};
    if (!charForm.code.trim()) errs.code = tc.errors.codeRequired;
    if (!charForm.name.trim()) errs.name = tc.errors.nameRequired;
    setCharErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await addQualityCharacteristic(token, openPlan, {
      code: charForm.code.trim(), name: charForm.name.trim(), dataType: charForm.dataType,
      minValue: charForm.minValue ? Number(charForm.minValue) : null,
      maxValue: charForm.maxValue ? Number(charForm.maxValue) : null,
      unit: null, mandatory: charForm.mandatory,
    });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.charAdded);
    setCharForm({ code: "", name: "", dataType: charForm.dataType, minValue: "", maxValue: "", mandatory: true });
    await load();
  }

  async function confirmApprovePlan() {
    if (!approvePlanId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await approveQualityPlan(token, approvePlanId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.planApprovedNote);
    setApprovePlanId("");
    await load();
  }

  async function submitInspection(e: FormEvent) {
    e.preventDefault();
    const qty = Number(inspForm.populationQty);
    const errs: { plan?: string; order?: string; item?: string; qty?: string } = {};
    if (!inspForm.qualityPlanId.trim()) errs.plan = tc.errors.planRequired;
    if (!inspForm.orderId.trim()) errs.order = tc.errors.itemRequired;
    if (!inspForm.itemSku.trim()) errs.item = tc.errors.itemRequired;
    if (!inspForm.populationQty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setInspErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await createInspection(token, { qualityPlanId: inspForm.qualityPlanId.trim(), orderId: inspForm.orderId.trim(), itemSku: inspForm.itemSku.trim(), lotSerial: inspForm.lotSerial.trim() || null, populationQty: qty });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.inspectionCreated + " " + res.data.id);
    setInspections((prev) => [res.data, ...prev]);
    setOpenInsp(res.data.id);
    setInspForm({ qualityPlanId: inspForm.qualityPlanId, orderId: "", itemSku: "", lotSerial: "", populationQty: "" });
  }

  async function openResults(inspectionId: string) {
    setOpenInsp(inspectionId);
    setResults(null);
    if (!token) return;
    const res = await fetchInspectionResults(token, inspectionId);
    setResults(res.ok ? res.data : []);
  }

  async function submitResult(e: FormEvent) {
    e.preventDefault();
    if (!openInsp || !token) return;
    const errs: { code?: string; value?: string } = {};
    if (!resultForm.characteristicCode.trim()) errs.code = tc.errors.codeRequired;
    if (!resultForm.rawValue.trim()) errs.value = tc.errors.resultRequired;
    setResultErrors(errs);
    if (Object.keys(errs).length > 0) return;
    setBusy(true);
    setBanner(null);
    const res = await addInspectionResult(token, openInsp, { characteristicCode: resultForm.characteristicCode.trim(), rawValue: resultForm.rawValue.trim() });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.resultAdded);
    setResultForm({ characteristicCode: "", rawValue: "" });
    await openResults(openInsp);
  }

  async function confirmFinalize() {
    if (!finalizeId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await finalizeInspection(token, finalizeId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.finalizedNote);
    setFinalizeId("");
    setInspections((prev) => prev.map((i) => (i.id === finalizeId ? { ...i, status: "Finalized" } : i)));
  }

  async function submitHold(e: FormEvent) {
    e.preventDefault();
    const qty = Number(holdForm.qty);
    const errs: { item?: string; qty?: string } = {};
    if (!holdForm.itemSku.trim()) errs.item = tc.errors.itemRequired;
    if (!holdForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    setHoldErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await createQualityHold(token, {
      itemSku: holdForm.itemSku.trim(), lotSerial: holdForm.lotSerial.trim() || null, qty,
      orderId: holdForm.orderId.trim() || null, inspectionId: holdForm.inspectionId.trim() || null, nonconformanceId: null,
    });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.holdCreatedNote);
    setHoldForm({ itemSku: "", lotSerial: "", qty: "", orderId: "", inspectionId: "" });
    await load();
  }

  async function confirmDispose(e: FormEvent) {
    e.preventDefault();
    if (!disposeTarget || !token) return;
    const qty = Number(disposeForm.qty);
    if (!disposeForm.qty.trim() || !Number.isFinite(qty) || qty <= 0) { setDisposeErrors({ qty: tc.errors.qtyInvalid }); return; }
    setDisposeErrors({});
    setBusy(true);
    setBanner(null);
    const res = await disposeQualityHold(token, disposeTarget.id, { disposition: disposeForm.disposition, qty, reason: disposeForm.reason.trim() || null });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.disposedNote);
    setDisposeTarget(null);
    await load();
  }

  async function submitNc(e: FormEvent) {
    e.preventDefault();
    const qty = Number(ncForm.affectedQty);
    const errs: { source?: string; title?: string; qty?: string; ref?: string } = {};
    if (!ncForm.sourceEventId.trim()) errs.source = tc.errors.sourceRequired;
    if (!ncForm.title.trim()) errs.title = tc.errors.titleRequired;
    if (!ncForm.affectedQty.trim() || !Number.isFinite(qty) || qty <= 0) errs.qty = tc.errors.qtyInvalid;
    if (!ncForm.objectRef.trim()) errs.ref = tc.errors.sourceRequired;
    setNcErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await raiseNonconformance(token, {
      sourceEventId: ncForm.sourceEventId.trim(), title: ncForm.title.trim(),
      itemSku: ncForm.itemSku.trim() || null, orderId: ncForm.orderId.trim() || null, affectedQty: qty,
      objects: [{ objectType: "Order", objectRef: ncForm.objectRef.trim(), qty }],
    });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.ncCreatedNote + " " + res.data.id);
    setNcForm({ sourceEventId: "", title: "", itemSku: "", orderId: "", affectedQty: "", objectRef: "", objectQty: "" });
    await load();
  }

  async function confirmContainment(e: FormEvent) {
    e.preventDefault();
    if (!ncContainment || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await addContainment(token, ncContainment.nc.id, ncContainment.kind);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.containmentAddedNote);
    setNcContainment(null);
    await load();
  }

  async function confirmReleaseContainment(e: FormEvent) {
    e.preventDefault();
    if (!ncRelease || !token) return;
    if (!ncRelease.reason.trim()) { setBanner(tc.errors.reasonRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await releaseContainment(token, ncRelease.containmentId, ncRelease.reason.trim());
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.containmentReleasedNote);
    setNcRelease(null);
    await load();
  }

  async function confirmCloseNc() {
    if (!ncCloseId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await closeNonconformance(token, ncCloseId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.ncClosedNote);
    setNcCloseId("");
    await load();
  }

  async function submitCapa(e: FormEvent) {
    e.preventDefault();
    const errs: { title?: string; criteria?: string } = {};
    if (!capaForm.title.trim()) errs.title = tc.errors.titleRequired;
    if (!capaForm.effectivenessCriteria.trim()) errs.criteria = tc.errors.criteriaRequired;
    setCapaErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await createCapaCase(token, { title: capaForm.title.trim(), nonconformanceId: capaForm.nonconformanceId.trim() || null, effectivenessCriteria: capaForm.effectivenessCriteria.trim() });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.capaCreatedNote + " " + res.data.id);
    setCapaForm({ title: "", nonconformanceId: capaForm.nonconformanceId, effectivenessCriteria: "" });
    await load();
  }

  async function submitCapaAction(e: FormEvent) {
    e.preventDefault();
    if (!capaActionForm.capaId || !token) return;
    if (!capaActionForm.description.trim()) { setBanner(tc.errors.titleRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await addCapaAction(token, capaActionForm.capaId, capaActionForm.description.trim());
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.actionAddedNote);
    setCapaActionForm({ capaId: "", description: "" });
    await load();
  }

  async function submitVerify(e: FormEvent) {
    e.preventDefault();
    if (!capaVerifyForm.capaId || !token) return;
    if (!capaVerifyForm.criteriaResult.trim()) { setBanner(tc.errors.resultRequired); return; }
    setBusy(true);
    setBanner(null);
    const res = await verifyCapa(token, capaVerifyForm.capaId, { criteriaResult: capaVerifyForm.criteriaResult.trim(), satisfied: capaVerifyForm.satisfied });
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.verifyNote);
    setCapaVerifyForm({ capaId: "", criteriaResult: "", satisfied: true });
    await load();
  }

  async function confirmCloseCapa() {
    if (!capaCloseId || !token) return;
    setBusy(true);
    setBanner(null);
    const res = await closeCapaCase(token, capaCloseId);
    setBusy(false);
    if (!res.ok) { setBanner(deferredActionError(t, res.code, res.message)); return; }
    push("success", tc.capaClosedNote);
    setCapaCloseId("");
    await load();
  }

  const planColumns: Column<QualityPlanRow>[] = [
    { key: "id", label: tc.plansTitle, mono: true, render: (p) => p.id },
    { key: "item", label: tc.planItem, mono: true, render: (p) => p.itemSku },
    { key: "rev", label: tc.planRevision, mono: true, render: (p) => p.revision },
    { key: "status", label: tc.colDataType, render: (p) => <Badge tone={p.status === "Approved" ? "success" : "warning"}>{mfgStatusLabel(t, p.status)}</Badge> },
    { key: "chars", label: tc.colChar, mono: true, render: (p) => p.characteristics.length },
    { key: "act", label: tc.approvePlan, render: (p) => (
      <div className="row-actions">
        <Button small variant="secondary" onClick={() => setOpenPlan(openPlan === p.id ? "" : p.id)}>{tc.colChar}</Button>
        {p.status === "Draft" && canPlan !== false && meReady ? <Button small onClick={() => setApprovePlanId(p.id)}>{tc.approvePlan}</Button> : null}
      </div>
    ) },
  ];

  const holdColumns: Column<QualityHoldRow>[] = [
    { key: "id", label: tc.holdsTitle, mono: true, render: (h) => h.id },
    { key: "item", label: tc.holdItem, mono: true, render: (h) => h.itemSku },
    { key: "qty", label: tc.holdQty, mono: true, render: (h) => fmtNumber(h.qty, locale) },
    { key: "status", label: tc.dispositionLabel, render: (h) => h.disposition
      ? <Badge tone="success">{h.disposition}</Badge>
      : <Badge tone="danger">{mfgStatusLabel(t, h.status)}</Badge> },
    { key: "holder", label: tc.colHolder, mono: true, render: (h) => h.heldBy },
    { key: "act", label: tc.dispose, render: (h) => (
      h.disposition === null && canDispose !== false && meReady && h.heldBy !== meId ? (
        <Button small onClick={() => { setDisposeTarget(h); setDisposeForm({ disposition: "ReleaseToStock", qty: String(h.qty), reason: "" }); }}>{tc.dispose}</Button>
      ) : null
    ) },
  ];

  const ncColumns: Column<NonconformanceRow>[] = [
    { key: "id", label: tc.ncTitle, mono: true, render: (n) => n.id },
    { key: "title", label: tc.ncTitleLabel, render: (n) => n.title },
    { key: "qty", label: tc.ncAffected, mono: true, render: (n) => fmtNumber(n.affectedQty, locale) },
    { key: "status", label: tc.colDataType, render: (n) => <Badge tone={n.status === "Closed" ? "muted" : "warning"}>{mfgStatusLabel(t, n.status)}</Badge> },
    { key: "contain", label: tc.containmentKind, render: (n) => (
      <div className="stack">
        {n.containments.map((c) => (
          <div className="row-actions" key={c.id}>
            <span className="t-caption mono">{c.kind}</span>
            <Badge tone={c.status === "Released" ? "success" : "warning"}>{mfgStatusLabel(t, c.status)}</Badge>
            {c.status !== "Released" && canDispose !== false && meReady && c.openedBy !== meId ? (
              <Button small variant="secondary" onClick={() => setNcRelease({ containmentId: c.id, reason: "" })}>{tc.releaseContainment}</Button>
            ) : null}
          </div>
        ))}
      </div>
    ) },
    { key: "act", label: tc.closeNc, render: (n) => (
      n.status !== "Closed" && canDispose !== false && meReady ? <Button small variant="secondary" onClick={() => setNcCloseId(n.id)}>{tc.closeNc}</Button> : null
    ) },
  ];

  const capaColumns: Column<CapaRow>[] = [
    { key: "id", label: tc.capaTitle, mono: true, render: (c) => c.id },
    { key: "title", label: tc.capaTitleLabel, render: (c) => c.title },
    { key: "criteria", label: tc.capaCriteria, mono: true, render: (c) => c.effectivenessCriteria },
    { key: "status", label: tc.colDataType, render: (c) => <Badge tone={c.status === "Closed" ? "success" : "warning"}>{mfgStatusLabel(t, c.status)}</Badge> },
    { key: "act", label: tc.closeCapa, render: (c) => (
      c.status !== "Closed" && canDispose !== false && meReady ? (
        <div className="row-actions">
          <Button small variant="secondary" onClick={() => setCapaActionForm({ capaId: c.id, description: "" })}>{tc.addAction}</Button>
          <Button small variant="secondary" onClick={() => setCapaVerifyForm({ capaId: c.id, criteriaResult: "", satisfied: true })}>{tc.verifyLabel}</Button>
          <Button small variant="secondary" onClick={() => setCapaCloseId(c.id)}>{tc.closeCapa}</Button>
        </div>
      ) : null
    ) },
  ];

  const resultColumns: Column<InspectionResultRow>[] = [
    { key: "code", label: tc.resultCode, mono: true, render: (r) => r.characteristicCode },
    { key: "value", label: tc.resultValue, mono: true, render: (r) => r.rawValue },
    { key: "pass", label: tc.colDataType, render: (r) => (r.evaluatedPass === null ? "—" : r.evaluatedPass ? <Badge tone="success">OK</Badge> : <Badge tone="danger">KO</Badge>) },
    { key: "amend", label: tc.resultValue, mono: true, render: (r) => (r.amendsId ? "#" + r.amendmentNo : "—") },
  ];

  const planOptions = (plans ?? []).map((p) => ({ value: p.id, label: p.id + " — " + p.itemSku }));
  const openPlanRow = (plans ?? []).find((p) => p.id === openPlan);
  const tabs: TabItem[] = [
    { id: "plans", label: tc.plansTitle, content: (
      <div className="stack">
        <section className="card">
          <h2 className="t-title">{tc.plansTitle}</h2>
          <form onSubmit={(e) => void submitPlan(e)}>
            <div className="form-grid">
              <Input id="q-item" label={tc.planItem} value={planForm.itemSku} onChange={(e) => setPlanForm({ ...planForm, itemSku: e.target.value })} error={planErrors.item} />
              <Input id="q-rev" label={tc.planRevision} type="number" min="1" step="1" value={planForm.revision} onChange={(e) => setPlanForm({ ...planForm, revision: e.target.value })} error={planErrors.rev} />
              <Input id="q-from" label={tc.planItem} type="date" value={planForm.effectiveFrom} onChange={(e) => setPlanForm({ ...planForm, effectiveFrom: e.target.value })} error={planErrors.from} />
            </div>
            <p><Button type="submit" loading={busy} disabled={!meReady || canPlan === false}>{tc.createPlan}</Button></p>
          </form>
          {plans === null ? null : plans.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={planColumns} rows={plans} caption={tc.plansTitle} />}
          {openPlanRow ? (
            <div className="stack">
              <h3 className="t-body-strong">{tc.addChar} — <span className="mono">{openPlanRow.id}</span></h3>
              <Table
                columns={[
                  { key: "code", label: tc.charCode, mono: true, render: (c) => c.code },
                  { key: "name", label: tc.charName, render: (c) => c.name },
                  { key: "type", label: tc.colDataType, render: (c) => c.dataType },
                  { key: "min", label: tc.charMin, mono: true, render: (c) => c.minValue ?? "—" },
                  { key: "max", label: tc.charMax, mono: true, render: (c) => c.maxValue ?? "—" },
                  { key: "mand", label: tc.charMandatory, render: (c) => (c.mandatory ? <Badge tone="warning">{tc.charMandatory}</Badge> : "—") },
                ]}
                rows={openPlanRow.characteristics}
                caption={tc.colChar}
              />
              <form onSubmit={(e) => void submitCharacteristic(e)}>
                <div className="form-grid">
                  <Input id="q-ccode" label={tc.charCode} value={charForm.code} onChange={(e) => setCharForm({ ...charForm, code: e.target.value })} error={charErrors.code} />
                  <Input id="q-cname" label={tc.charName} value={charForm.name} onChange={(e) => setCharForm({ ...charForm, name: e.target.value })} error={charErrors.name} />
                  <Select id="q-ctype" label={tc.colDataType} value={charForm.dataType} onChange={(e) => setCharForm({ ...charForm, dataType: e.target.value })} options={CHAR_TYPES.map((x) => ({ value: x, label: x }))} />
                  <Input id="q-cmin" label={tc.charMin} type="number" step="any" value={charForm.minValue} onChange={(e) => setCharForm({ ...charForm, minValue: e.target.value })} />
                  <Input id="q-cmax" label={tc.charMax} type="number" step="any" value={charForm.maxValue} onChange={(e) => setCharForm({ ...charForm, maxValue: e.target.value })} />
                </div>
                <p className="t-caption">
                  <label htmlFor="q-cmand"><input id="q-cmand" type="checkbox" checked={charForm.mandatory} onChange={(e) => setCharForm({ ...charForm, mandatory: e.target.checked })} /> {tc.charMandatory}</label>
                </p>
                <p><Button type="submit" small loading={busy} disabled={!meReady || canPlan === false}>{tc.addChar}</Button></p>
              </form>
            </div>
          ) : null}
        </section>
      </div>
    ) },
    { id: "inspections", label: tc.inspectionsTitle, content: (
      <section className="card">
        <h2 className="t-title">{tc.inspectionsTitle}</h2>
        <form onSubmit={(e) => void submitInspection(e)}>
          <div className="form-grid">
            <Input id="q-iplan" label={tc.planIdLabel} value={inspForm.qualityPlanId} onChange={(e) => setInspForm({ ...inspForm, qualityPlanId: e.target.value })} error={inspErrors.plan} />
            <Input id="q-iorder" label={tc.inspectionOrder} value={inspForm.orderId} onChange={(e) => setInspForm({ ...inspForm, orderId: e.target.value })} error={inspErrors.order} />
            <Input id="q-iitem" label={tc.inspectionItem} value={inspForm.itemSku} onChange={(e) => setInspForm({ ...inspForm, itemSku: e.target.value })} error={inspErrors.item} />
            <Input id="q-ilot" label={tc.inspectionLot} value={inspForm.lotSerial} onChange={(e) => setInspForm({ ...inspForm, lotSerial: e.target.value })} />
            <Input id="q-iqty" label={tc.populationQty} type="number" min="0" step="any" value={inspForm.populationQty} onChange={(e) => setInspForm({ ...inspForm, populationQty: e.target.value })} error={inspErrors.qty} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canInspect === false}>{tc.createInspection}</Button></p>
        </form>
        {inspections.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="stack">
            {inspections.map((i) => (
              <div className="stack" key={i.id}>
                <div className="row-actions">
                  <button type="button" className="sidebar__link" style={{ inlineSize: "auto", padding: 0 }} onClick={() => void openResults(i.id)}>
                    <span className="mono">{i.id}</span>
                  </button>
                  <span className="t-caption mono">{i.itemSku} × {fmtNumber(i.sampleSize, locale)}</span>
                  <Badge tone={i.status === "Finalized" ? "success" : "warning"}>{mfgStatusLabel(t, i.status)}</Badge>
                  {i.status !== "Finalized" && canInspect !== false && meReady ? <Button small variant="secondary" onClick={() => setFinalizeId(i.id)}>{tc.finalize}</Button> : null}
                </div>
                {openInsp === i.id ? (
                  <div className="stack">
                    {results === null ? null : results.length === 0 ? <p className="t-caption">{tc.empty}</p> : <Table columns={resultColumns} rows={results} caption={tc.resultsTitle} />}
                    {i.status !== "Finalized" ? (
                      <form onSubmit={(e) => void submitResult(e)}>
                        <div className="form-grid">
                          <Input id={"q-rcode-" + i.id} label={tc.resultCode} value={resultForm.characteristicCode} onChange={(e) => setResultForm({ ...resultForm, characteristicCode: e.target.value })} error={resultErrors.code} />
                          <Input id={"q-rvalue-" + i.id} label={tc.resultValue} value={resultForm.rawValue} onChange={(e) => setResultForm({ ...resultForm, rawValue: e.target.value })} error={resultErrors.value} />
                        </div>
                        <p><Button type="submit" small loading={busy} disabled={!meReady || canInspect === false}>{tc.addResult}</Button></p>
                      </form>
                    ) : null}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        )}
      </section>
    ) },
    { id: "holds", label: tc.holdsTitle, content: (
      <section className="card">
        <h2 className="t-title">{tc.holdsTitle}</h2>
        <form onSubmit={(e) => void submitHold(e)}>
          <div className="form-grid">
            <Input id="q-hitem" label={tc.holdItem} value={holdForm.itemSku} onChange={(e) => setHoldForm({ ...holdForm, itemSku: e.target.value })} error={holdErrors.item} />
            <Input id="q-hlot" label={tc.inspectionLot} value={holdForm.lotSerial} onChange={(e) => setHoldForm({ ...holdForm, lotSerial: e.target.value })} />
            <Input id="q-hqty" label={tc.holdQty} type="number" min="0" step="any" value={holdForm.qty} onChange={(e) => setHoldForm({ ...holdForm, qty: e.target.value })} error={holdErrors.qty} />
            <Input id="q-horder" label={tc.inspectionOrder} value={holdForm.orderId} onChange={(e) => setHoldForm({ ...holdForm, orderId: e.target.value })} />
            <Input id="q-hinsp" label={tc.planIdLabel} value={holdForm.inspectionId} onChange={(e) => setHoldForm({ ...holdForm, inspectionId: e.target.value })} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canDispose === false}>{tc.createHold}</Button></p>
        </form>
        {holds === null ? null : holds.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={holdColumns} rows={holds} caption={tc.holdsTitle} />}
      </section>
    ) },
    { id: "nc", label: tc.ncTitle, content: (
      <section className="card">
        <h2 className="t-title">{tc.ncTitle}</h2>
        <p className="t-caption">{tc.ncObjectsHint}</p>
        <form onSubmit={(e) => void submitNc(e)}>
          <div className="form-grid">
            <Input id="q-nsrc" label={tc.ncSource} value={ncForm.sourceEventId} onChange={(e) => setNcForm({ ...ncForm, sourceEventId: e.target.value })} error={ncErrors.source} />
            <Input id="q-ntitle" label={tc.ncTitleLabel} value={ncForm.title} onChange={(e) => setNcForm({ ...ncForm, title: e.target.value })} error={ncErrors.title} />
            <Input id="q-nitem" label={tc.planItem} value={ncForm.itemSku} onChange={(e) => setNcForm({ ...ncForm, itemSku: e.target.value })} />
            <Input id="q-norder" label={tc.inspectionOrder} value={ncForm.orderId} onChange={(e) => setNcForm({ ...ncForm, orderId: e.target.value })} />
            <Input id="q-nqty" label={tc.ncAffected} type="number" min="0" step="any" value={ncForm.affectedQty} onChange={(e) => setNcForm({ ...ncForm, affectedQty: e.target.value })} error={ncErrors.qty} />
            <Input id="q-nref" label={tc.ncObjectsHint} value={ncForm.objectRef} onChange={(e) => setNcForm({ ...ncForm, objectRef: e.target.value })} error={ncErrors.ref} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canDispose === false}>{tc.raiseNc}</Button></p>
        </form>
        {ncs === null ? null : ncs.length === 0 ? <EmptyState title={tc.empty} /> : (
          <div className="stack">
            <Table columns={ncColumns} rows={ncs} caption={tc.ncTitle} />
            {ncs.map((n) => (
              n.containments.length === 0 && n.status !== "Closed" && canDispose !== false && meReady ? (
                <div className="row-actions" key={"cont-" + n.id}>
                  <Button small variant="secondary" onClick={() => setNcContainment({ nc: n, kind: "Quarantine" })}>{tc.addContainment}</Button>
                </div>
              ) : null
            ))}
          </div>
        )}
      </section>
    ) },
    { id: "capa", label: tc.capaTitle, content: (
      <section className="card">
        <h2 className="t-title">{tc.capaTitle}</h2>
        <form onSubmit={(e) => void submitCapa(e)}>
          <div className="form-grid">
            <Input id="q-catitle" label={tc.capaTitleLabel} value={capaForm.title} onChange={(e) => setCapaForm({ ...capaForm, title: e.target.value })} error={capaErrors.title} />
            <Input id="q-canc" label={tc.ncTitleLabel} value={capaForm.nonconformanceId} onChange={(e) => setCapaForm({ ...capaForm, nonconformanceId: e.target.value })} />
            <Input id="q-cacrit" label={tc.capaCriteria} value={capaForm.effectivenessCriteria} onChange={(e) => setCapaForm({ ...capaForm, effectivenessCriteria: e.target.value })} error={capaErrors.criteria} />
          </div>
          <p><Button type="submit" small loading={busy} disabled={!meReady || canDispose === false}>{tc.createCapa}</Button></p>
        </form>
        {capas === null ? null : capas.length === 0 ? <EmptyState title={tc.empty} /> : <Table columns={capaColumns} rows={capas} caption={tc.capaTitle} />}
      </section>
    ) },
  ];

  if (!ready || (phase === "loading" && plans === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    return (
      <PageShell
        title={tc.title} subtitle={tc.subtitle} loading={false}
        error errorCode={errorCode} onRetry={() => void load()}
        forbidden={errorCode === "FORBIDDEN" || errorCode === "HTTP_403" ? tc.forbidden : tc.loadFailed}
        toasts={toasts} onDismiss={dismiss} banner={null}
        loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
      />
    );
  }
  return (
    <PageShell
      title={tc.title} subtitle={tc.subtitle} loading={false} error={false} errorCode={null}
      onRetry={() => void load()} forbidden={tc.forbidden}
      toasts={toasts} onDismiss={dismiss} banner={banner}
      loadFailed={tc.loadFailed} retryLabel={t.common.retry} cancelLabel={t.common.cancel}
    >
      {dashboard ? (
        <section className="tiles">
          {[
            { label: tc.openHolds, value: String(dashboard.openHolds) },
            { label: tc.heldQty, value: fmtNumber(dashboard.heldQty, locale) },
            { label: tc.ncOpen, value: String(dashboard.nonconformancesOpen) },
            { label: tc.capaOpen, value: String(dashboard.capaOpen) },
            { label: tc.inspectionsOpen, value: String(dashboard.inspectionsInProgress) },
          ].map((tile) => (
            <div className="kpi" key={tile.label}>
              <p className="kpi__label">{tile.label}</p>
              <p className="kpi__value">{tile.value}</p>
            </div>
          ))}
        </section>
      ) : null}
      <Tabs tabs={tabs} activeId={tab} onChange={setTab} label={tc.title} />

      <Modal open={approvePlanId !== ""} onClose={() => setApprovePlanId("")} title={tc.planApprovedNote} closeLabel={t.common.cancel} busy={busy}>
        <p className="t-caption mono">{approvePlanId}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setApprovePlanId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmApprovePlan()}>{tc.approvePlan}</Button>
        </div>
      </Modal>
      <Modal open={finalizeId !== ""} onClose={() => setFinalizeId("")} title={tc.finalizeConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.finalizeConfirm}</p>
        <p className="t-caption mono">{finalizeId}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setFinalizeId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmFinalize()}>{tc.finalize}</Button>
        </div>
      </Modal>
      <Modal open={disposeTarget !== null} onClose={() => setDisposeTarget(null)} title={tc.disposeConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmDispose(e)}>
          <p>{tc.disposeConfirm}</p>
          {disposeTarget ? <p className="t-caption mono">{disposeTarget.id} — {disposeTarget.itemSku}</p> : null}
          <div className="form-grid">
            <Select id="q-disp-kind" label={tc.dispositionLabel} value={disposeForm.disposition} onChange={(e) => setDisposeForm({ ...disposeForm, disposition: e.target.value })} options={DISPOSITIONS.map((d) => ({ value: d, label: d === "ReleaseToStock" ? tc.dispRelease : d === "Scrap" ? tc.dispScrap : d === "Rework" ? tc.dispRework : tc.dispReturn }))} />
            <Input id="q-disp-qty" label={tc.holdQty} type="number" min="0" step="any" value={disposeForm.qty} onChange={(e) => setDisposeForm({ ...disposeForm, qty: e.target.value })} error={disposeErrors.qty} />
            <Input id="q-disp-reason" label={tc.releaseReason} value={disposeForm.reason} onChange={(e) => setDisposeForm({ ...disposeForm, reason: e.target.value })} />
          </div>
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setDisposeTarget(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.dispose}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={ncContainment !== null} onClose={() => setNcContainment(null)} title={tc.containmentKind} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmContainment(e)}>
          {ncContainment ? <p className="t-caption mono">{ncContainment.nc.id}</p> : null}
          <Input id="q-cont-kind" label={tc.containmentKind} value={ncContainment?.kind ?? ""} onChange={(e) => setNcContainment((prev) => (prev ? { ...prev, kind: e.target.value } : prev))} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setNcContainment(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.addContainment}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={ncRelease !== null} onClose={() => setNcRelease(null)} title={tc.releaseContainment} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void confirmReleaseContainment(e)}>
          <Input id="q-cont-rel" label={tc.releaseReason} value={ncRelease?.reason ?? ""} onChange={(e) => setNcRelease((prev) => (prev ? { ...prev, reason: e.target.value } : prev))} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setNcRelease(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.releaseContainment}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={ncCloseId !== ""} onClose={() => setNcCloseId("")} title={tc.ncCloseConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.ncCloseConfirm}</p>
        <p className="t-caption mono">{ncCloseId}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setNcCloseId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmCloseNc()}>{tc.closeNc}</Button>
        </div>
      </Modal>
      <Modal open={capaCloseId !== ""} onClose={() => setCapaCloseId("")} title={tc.capaCloseConfirmTitle} closeLabel={t.common.cancel} busy={busy}>
        <p>{tc.capaCloseConfirm}</p>
        <p className="t-caption mono">{capaCloseId}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setCapaCloseId("")}>{t.common.cancel}</Button>
          <Button loading={busy} onClick={() => void confirmCloseCapa()}>{tc.closeCapa}</Button>
        </div>
      </Modal>
      <Modal open={capaActionForm.capaId !== ""} onClose={() => setCapaActionForm({ capaId: "", description: "" })} title={tc.addAction} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void submitCapaAction(e)}>
          <Input id="q-capa-desc" label={tc.capaActionLabel} value={capaActionForm.description} onChange={(e) => setCapaActionForm({ ...capaActionForm, description: e.target.value })} />
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setCapaActionForm({ capaId: "", description: "" })}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.addAction}</Button>
          </div>
        </form>
      </Modal>
      <Modal open={capaVerifyForm.capaId !== ""} onClose={() => setCapaVerifyForm({ capaId: "", criteriaResult: "", satisfied: true })} title={tc.verifyLabel} closeLabel={t.common.cancel} busy={busy}>
        <form onSubmit={(e) => void submitVerify(e)}>
          <Input id="q-capa-ver" label={tc.verifyLabel} value={capaVerifyForm.criteriaResult} onChange={(e) => setCapaVerifyForm({ ...capaVerifyForm, criteriaResult: e.target.value })} />
          <p className="t-caption">
            <label htmlFor="q-capa-sat"><input id="q-capa-sat" type="checkbox" checked={capaVerifyForm.satisfied} onChange={(e) => setCapaVerifyForm({ ...capaVerifyForm, satisfied: e.target.checked })} /> {tc.verifySatisfied}</label>
          </p>
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setCapaVerifyForm({ capaId: "", criteriaResult: "", satisfied: true })}>{t.common.cancel}</Button>
            <Button type="submit" loading={busy}>{tc.verifyLabel}</Button>
          </div>
        </form>
      </Modal>
    </PageShell>
  );
}
