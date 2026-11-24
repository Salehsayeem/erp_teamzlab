"use client";

import { useState, type FormEvent } from "react";
import { addAmendment, procError, type ContractPriceLine } from "@/lib/services/procurement";
import { fmtMoney, fmtNumber } from "@/lib/format";
import { Button, EmptyState, Input, Table, Textarea } from "@/components/uikit";
import type { Column } from "@/components/uikit";
import { hasPerm, isoDate, type ContractCtx } from "../../../_lib/ui";

type PriceLineDraft = { itemSku: string; unitPrice: string; effectiveFrom: string; effectiveTo: string };
type AmendmentForm = { effectiveDate: string; summary: string };

const EMPTY_LINE: PriceLineDraft = { itemSku: "", unitPrice: "", effectiveFrom: "", effectiveTo: "" };
const EMPTY_FORM: AmendmentForm = { effectiveDate: "", summary: "" };

function overlaps(a: PriceLineDraft, b: PriceLineDraft): boolean {
  const aEnd = a.effectiveTo === "" ? null : a.effectiveTo;
  const bEnd = b.effectiveTo === "" ? null : b.effectiveTo;
  return (aEnd === null || b.effectiveFrom <= aEnd) && (bEnd === null || a.effectiveFrom <= bEnd);
}

export function AmendmentsSection({ ctx }: { ctx: ContractCtx }) {
  const { token, t, locale, contract, amendments, setAmendments, onContract } = ctx;
  const am = t.procurement.contracts.workspace.amendments;
  const canWrite = hasPerm(ctx.perms, "contracts:write");

  const [form, setForm] = useState<AmendmentForm>(EMPTY_FORM);
  const [lines, setLines] = useState<PriceLineDraft[]>([{ ...EMPTY_LINE }]);
  const [errors, setErrors] = useState<{ effectiveDate?: string; summary?: string }>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const errs: typeof errors = {};
    if (!form.effectiveDate) errs.effectiveDate = am.errors.effectiveDateRequired;
    if (!form.summary.trim()) errs.summary = am.errors.summaryRequired;
    setErrors(errs);
    setFormError(null);

    const skus: string[] = [];
    for (const line of lines) {
      if (!line.itemSku.trim()) {
        setFormError(am.errors.skuRequired);
        return;
      }
      if (line.unitPrice === "" || !(Number(line.unitPrice) >= 0)) {
        setFormError(am.errors.priceInvalid);
        return;
      }
      if (!line.effectiveFrom || (line.effectiveTo !== "" && line.effectiveTo < line.effectiveFrom)) {
        setFormError(am.errors.lineDatesInvalid);
        return;
      }
      skus.push(line.itemSku.trim().toLowerCase());
    }
    for (let i = 0; i < skus.length; i += 1) {
      for (let j = i + 1; j < skus.length; j += 1) {
        if (skus[i] === skus[j] && overlaps(lines[i] ?? EMPTY_LINE, lines[j] ?? EMPTY_LINE)) {
          setFormError(am.overlap);
          return;
        }
      }
    }
    if (errs.effectiveDate || errs.summary) return;

    setSubmitting(true);
    const res = await addAmendment(token, contract.id, {
      effectiveDate: form.effectiveDate,
      summary: form.summary.trim(),
      priceLines: lines.map((l) => ({
        itemSku: l.itemSku.trim(),
        unitPrice: Number(l.unitPrice),
        effectiveFrom: l.effectiveFrom,
        effectiveTo: l.effectiveTo === "" ? null : l.effectiveTo,
      })),
    });
    setSubmitting(false);
    if (!res.ok) {
      const mapped: Record<string, string> = {
        CONTRACT_PRICE_OVERLAP: am.overlap,
        CONTRACT_AMENDMENT_CONFLICT: am.overlap,
        CONTRACT_STATE_ERROR: am.immutable,
        FORBIDDEN: t.procurement.common.forbidden,
      };
      setFormError(procError(res.code, res.message, mapped, am.addFailed));
      return;
    }
    setAmendments((prev) => [res.data.amendment, ...prev]);
    onContract(res.data.contract);
    setForm(EMPTY_FORM);
    setLines([{ ...EMPTY_LINE }]);
  }

  const priceLineColumns: Column<ContractPriceLine>[] = [
    { key: "itemSku", label: am.priceLine.itemSku, mono: true, render: (l) => l.itemSku },
    { key: "unitPrice", label: am.priceLine.unitPrice, mono: true, render: (l) => fmtMoney(l.unitPrice, locale, contract.currency) },
    { key: "effectiveFrom", label: am.priceLine.effectiveFrom, mono: true, render: (l) => isoDate(l.effectiveFrom) },
    { key: "effectiveTo", label: am.priceLine.effectiveTo, mono: true, render: (l) => (l.effectiveTo ? isoDate(l.effectiveTo) : "—") },
  ];

  const columns: Column<(typeof amendments)[number]>[] = [
    { key: "version", label: am.columns.version, mono: true, render: (a) => `v${a.version}` },
    { key: "effectiveDate", label: am.columns.effectiveDate, mono: true, render: (a) => isoDate(a.effectiveDate) },
    { key: "summary", label: am.columns.summary, render: (a) => a.summary },
    { key: "approvedBy", label: am.columns.approvedBy, mono: true, render: (a) => a.approvedBy ?? "—" },
  ];

  return (
    <section className="card">
      <h2 className="t-title">{am.title}</h2>
      <p className="t-caption">{am.immutable}</p>
      {amendments.length === 0 ? (
        <EmptyState title={am.empty} />
      ) : (
        <>
          <Table columns={columns} rows={amendments} caption={am.title} />
          {ctx.amendments.length > 0 ? null : null}
        </>
      )}
      {canWrite ? (
        <form onSubmit={(e) => void submit(e)}>
          <h3 className="t-body-strong">{am.addTitle}</h3>
          <div className="form-grid">
            <Input id="am-date" label={am.effectiveDate} type="date" value={form.effectiveDate} onChange={(e) => setForm({ ...form, effectiveDate: e.target.value })} error={errors.effectiveDate} />
            <div style={{ gridColumn: "1 / -1" }}>
              <Textarea id="am-summary" label={am.summary} value={form.summary} onChange={(e) => setForm({ ...form, summary: e.target.value })} error={errors.summary} />
            </div>
          </div>
          <h3 className="t-body-strong">{am.priceLines}</h3>
          {lines.map((line, i) => (
            <div className="form-grid" key={i}>
              <Input
                id={`pl-sku-${i}`}
                label={`${am.priceLine.itemSku} ${i + 1}`}
                value={line.itemSku}
                onChange={(e) => setLines((prev) => prev.map((x, idx) => (idx === i ? { ...x, itemSku: e.target.value } : x)))}
              />
              <Input
                id={`pl-price-${i}`}
                label={`${am.priceLine.unitPrice} ${i + 1}`}
                type="number"
                min="0"
                step="any"
                value={line.unitPrice}
                onChange={(e) => setLines((prev) => prev.map((x, idx) => (idx === i ? { ...x, unitPrice: e.target.value } : x)))}
              />
              <Input
                id={`pl-from-${i}`}
                label={`${am.priceLine.effectiveFrom} ${i + 1}`}
                type="date"
                value={line.effectiveFrom}
                onChange={(e) => setLines((prev) => prev.map((x, idx) => (idx === i ? { ...x, effectiveFrom: e.target.value } : x)))}
              />
              <Input
                id={`pl-to-${i}`}
                label={am.priceLine.effectiveTo}
                type="date"
                value={line.effectiveTo}
                onChange={(e) => setLines((prev) => prev.map((x, idx) => (idx === i ? { ...x, effectiveTo: e.target.value } : x)))}
              />
              <div className="field">
                <Button
                  type="button"
                  variant="secondary"
                  small
                  aria-label={`${t.procurement.common.remove} ${i + 1}`}
                  disabled={lines.length === 1}
                  onClick={() => setLines((prev) => prev.filter((_, idx) => idx !== i))}
                >
                  {t.procurement.common.remove}
                </Button>
              </div>
            </div>
          ))}
          <div className="row-actions" style={{ justifyContent: "flex-start" }}>
            <Button type="button" variant="secondary" small onClick={() => setLines((prev) => [...prev, { ...EMPTY_LINE }])}>{am.addLine}</Button>
            <Button type="submit" loading={submitting}>{am.addTitle}</Button>
          </div>
          {formError ? <p className="banner" role="alert">{formError}</p> : null}
        </form>
      ) : null}
    </section>
  );
}
