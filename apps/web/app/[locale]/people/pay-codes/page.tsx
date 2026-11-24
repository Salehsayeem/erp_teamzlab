"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  createPayCode,
  deactivatePayCode,
  fetchPayCodes,
  updatePayCode,
  type PayCodeRow,
} from "@/lib/services/people";
import { useAuth } from "@/hooks/useAuth";
import { fmtNumber } from "@/lib/format";
import {
  Badge,
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Select,
  Table,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPermission, peopleActionError, peopleStatusLabel, peopleTone, usePeopleMe } from "../_lib/helpers";

const KINDS = ["Earning", "Deduction"];
const BASES = ["Hours", "Amount", "Percent"];

type CodeForm = { code: string; kind: string; name: string; calcBasis: string; ratePct: string; debitAccount: string; creditAccount: string };
const EMPTY_CODE: CodeForm = { code: "", kind: "Earning", name: "", calcBasis: "Amount", ratePct: "", debitAccount: "", creditAccount: "" };
type EditForm = { name: string; ratePct: string; calcBasis: string };

export default function PayCodesPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.people.payCodes;
  const { token, ready } = useAuth(locale);
  const { permissions, meReady } = usePeopleMe(token);
  const canWrite = hasPermission(permissions, "paycodes:write");

  const [codes, setCodes] = useState<PayCodeRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [kindFilter, setKindFilter] = useState("");

  const [form, setForm] = useState<CodeForm>(EMPTY_CODE);
  const [errors, setErrors] = useState<Partial<Record<keyof CodeForm, string>>>({});
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const [editCode, setEditCode] = useState<PayCodeRow | null>(null);
  const [editForm, setEditForm] = useState<EditForm>({ name: "", ratePct: "", calcBasis: "" });
  const [editErrors, setEditErrors] = useState<{ rate?: string }>({});
  const [saving, setSaving] = useState(false);
  const [deactivateCode, setDeactivateCode] = useState<PayCodeRow | null>(null);
  const [deactivating, setDeactivating] = useState(false);

  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
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
    setErrorCode(null);
    const res = await fetchPayCodes(token, kindFilter || undefined);
    if (res.ok) {
      setCodes(res.data);
      setPhase("success");
    } else {
      setCodes(null);
      setErrorCode(res.code);
      setPhase("error");
    }
  }, [token, kindFilter]);
  useEffect(() => { void load(); }, [load]);

  function rateInvalid(basis: string, raw: string): boolean {
    if (basis === "Hours") return false;
    const rate = Number(raw);
    if (!raw.trim() || !Number.isFinite(rate) || rate < 0) return true;
    return basis === "Percent" && rate > 100;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: Partial<Record<keyof CodeForm, string>> = {};
    if (!form.code.trim()) errs.code = tc.errors.codeRequired;
    if (!form.name.trim()) errs.name = tc.errors.nameRequired;
    if (!form.debitAccount.trim()) errs.debitAccount = tc.errors.debitRequired;
    if (!form.creditAccount.trim()) errs.creditAccount = tc.errors.creditRequired;
    if (form.kind === "Deduction" && form.calcBasis === "Hours") errs.calcBasis = tc.deductionHoursNote;
    if (rateInvalid(form.calcBasis, form.ratePct)) errs.ratePct = tc.errors.rateInvalid;
    setErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreating(true);
    setCreateError(null);
    const rate = form.calcBasis === "Hours" ? null : Number(form.ratePct);
    const res = await createPayCode(token, {
      code: form.code.trim(), kind: form.kind, name: form.name.trim(), calcBasis: form.calcBasis,
      ratePct: rate, debitAccount: form.debitAccount.trim(), creditAccount: form.creditAccount.trim(),
    });
    setCreating(false);
    if (!res.ok) {
      setCreateError(peopleActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.code);
    setCodes((prev) => (prev ? [res.data, ...prev] : prev));
    setForm(EMPTY_CODE);
  }

  function openEdit(code: PayCodeRow) {
    setEditCode(code);
    setEditErrors({});
    setEditForm({
      name: code.name,
      ratePct: code.ratePct === null ? "" : String(code.ratePct),
      calcBasis: code.calcBasis,
    });
  }

  async function submitEdit(e: FormEvent) {
    e.preventDefault();
    if (!editCode || !token) return;
    if (editCode.calcBasis !== "Hours" && rateInvalid(editForm.calcBasis || editCode.calcBasis, editForm.ratePct)) {
      setEditErrors({ rate: tc.errors.rateInvalid });
      return;
    }
    setEditErrors({});
    setSaving(true);
    setActionError(null);
    const res = await updatePayCode(token, editCode.code, {
      name: editForm.name.trim() === editCode.name ? null : editForm.name.trim(),
      ratePct: editForm.ratePct.trim() === "" || Number(editForm.ratePct) === editCode.ratePct ? null : Number(editForm.ratePct),
      calcBasis: editForm.calcBasis === editCode.calcBasis ? null : editForm.calcBasis,
    });
    setSaving(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    const updated = res.data;
    setCodes((prev) => (prev ? prev.map((c) => (c.code === updated.code ? updated : c)) : prev));
    pushToast("success", tc.updated + " v" + updated.version);
    setEditCode(null);
  }

  async function confirmDeactivate() {
    if (!deactivateCode || !token) return;
    setDeactivating(true);
    setActionError(null);
    const res = await deactivatePayCode(token, deactivateCode.code);
    setDeactivating(false);
    if (!res.ok) {
      setActionError(peopleActionError(t, res.code, res.message));
      return;
    }
    const updated = res.data;
    setCodes((prev) => (prev ? prev.map((c) => (c.code === updated.code ? updated : c)) : prev));
    pushToast("success", tc.deactivated);
    setDeactivateCode(null);
  }

  const columns: Column<PayCodeRow>[] = useMemo(() => [
    { key: "code", label: tc.columns.code, mono: true, render: (c) => c.code },
    { key: "name", label: tc.columns.name, render: (c) => c.name },
    { key: "kind", label: tc.columns.kind, render: (c) => <Badge tone={peopleTone(c.kind)}>{peopleStatusLabel(t, c.kind)}</Badge> },
    { key: "basis", label: tc.columns.basis, render: (c) => peopleStatusLabel(t, c.calcBasis) + (c.ratePct === null ? "" : " · " + fmtNumber(c.ratePct, locale)) },
    { key: "debit", label: tc.columns.debit, mono: true, render: (c) => c.debitAccount },
    { key: "credit", label: tc.columns.credit, mono: true, render: (c) => c.creditAccount },
    { key: "version", label: tc.columns.version, mono: true, render: (c) => "v" + fmtNumber(c.version, locale) },
    { key: "status", label: tc.columns.status, render: (c) => <Badge tone={c.active ? "success" : "muted"}>{c.active ? tc.activeBadge : tc.inactiveBadge}</Badge> },
    {
      key: "actions", label: "",
      render: (c) => c.active && canWrite !== false ? (
        <div className="row-actions">
          <Button small variant="secondary" disabled={!meReady || busy} onClick={() => openEdit(c)}>{tc.editAction}</Button>
          <Button small variant="danger" disabled={!meReady || busy} onClick={() => setDeactivateCode(c)}>{tc.deactivate}</Button>
        </div>
      ) : null,
    },
  ], [t, tc, locale, canWrite, meReady, busy]);

  if (!ready || (phase === "loading" && codes === null)) return <LoadingSkeleton rows={6} />;
  if (phase === "error") {
    if (errorCode === "FORBIDDEN" || errorCode === "HTTP_403") {
      return (
        <div className="stack">
          <div className="page-header">
            <h1 className="t-display">{tc.title}</h1>
            <p className="t-caption">{tc.subtitle}</p>
          </div>
          <div className="state state--error" role="alert"><p className="state__title">{tc.forbidden}</p></div>
        </div>
      );
    }
    return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;
  }

  const list = codes ?? [];
  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {actionError ? <p className="banner" role="alert">{actionError}</p> : null}
      {createError ? <p className="banner" role="alert">{createError}</p> : null}

      <section className="card">
        <h2 className="t-title">{tc.formTitle}</h2>
        <p className="t-caption">{tc.mappingHint}</p>
        {canWrite === false ? <p className="t-caption">{tc.forbidden}</p> : (
          <form onSubmit={(e) => void submit(e)}>
            <div className="form-grid">
              <Input id="pc-code" label={tc.code} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} error={errors.code} />
              <Select id="pc-kind" label={tc.kind} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} options={KINDS.map((k) => ({ value: k, label: peopleStatusLabel(t, k) }))} />
              <Input id="pc-name" label={tc.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} error={errors.name} />
              <Select id="pc-basis" label={tc.calcBasis} value={form.calcBasis} onChange={(e) => setForm({ ...form, calcBasis: e.target.value })} options={BASES.map((b) => ({ value: b, label: peopleStatusLabel(t, b) }))} error={errors.calcBasis} />
              <Input id="pc-rate" label={tc.ratePct} type="number" min="0" step="any" value={form.ratePct} onChange={(e) => setForm({ ...form, ratePct: e.target.value })} error={errors.ratePct} hint={tc.rateHint} />
              <Input id="pc-debit" label={tc.debitAccount} value={form.debitAccount} onChange={(e) => setForm({ ...form, debitAccount: e.target.value })} error={errors.debitAccount} />
              <Input id="pc-credit" label={tc.creditAccount} value={form.creditAccount} onChange={(e) => setForm({ ...form, creditAccount: e.target.value })} error={errors.creditAccount} />
            </div>
            <p><Button type="submit" loading={creating}>{tc.create}</Button></p>
          </form>
        )}
      </section>

      <section className="card">
        <div className="form-grid">
          <Select id="pc-kind-filter" label={tc.kindFilter} value={kindFilter} onChange={(e) => setKindFilter(e.target.value)} options={KINDS.map((k) => ({ value: k, label: peopleStatusLabel(t, k) }))} placeholder={t.people.statusFilterAll} />
        </div>
        {list.length === 0 ? <EmptyState title={kindFilter ? tc.noMatch : tc.empty} /> : (
          <Table columns={columns} rows={list} caption={tc.title} />
        )}
      </section>

      <Modal open={editCode !== null} onClose={() => setEditCode(null)} title={tc.editTitle + " — " + (editCode?.code ?? "")} closeLabel={t.common.cancel} busy={saving}>
        <p className="t-caption">{tc.editHint}</p>
        <form onSubmit={(e) => void submitEdit(e)}>
          <div className="form-grid">
            <Input id="pc-edit-name" label={tc.editName} value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} />
            <Input id="pc-edit-rate" label={tc.editRate} type="number" min="0" step="any" value={editForm.ratePct} onChange={(e) => setEditForm({ ...editForm, ratePct: e.target.value })} error={editErrors.rate} />
            <Select id="pc-edit-basis" label={tc.editBasis} value={editForm.calcBasis} onChange={(e) => setEditForm({ ...editForm, calcBasis: e.target.value })} options={BASES.map((b) => ({ value: b, label: peopleStatusLabel(t, b) }))} />
          </div>
          <div className="modal__actions">
            <Button type="button" variant="secondary" onClick={() => setEditCode(null)}>{t.common.cancel}</Button>
            <Button type="submit" loading={saving}>{tc.save}</Button>
          </div>
        </form>
      </Modal>

      <Modal open={deactivateCode !== null} onClose={() => setDeactivateCode(null)} title={tc.deactivateConfirmTitle} closeLabel={t.common.cancel} busy={deactivating}>
        <p>{tc.deactivateConfirm}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setDeactivateCode(null)}>{t.common.cancel}</Button>
          <Button variant="danger" loading={deactivating} onClick={() => void confirmDeactivate()}>{tc.deactivate}</Button>
        </div>
      </Modal>
    </div>
  );
}
