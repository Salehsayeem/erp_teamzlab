"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Locale } from "@/lib/i18n";
import {
  addBookEntry,
  createContractPrice,
  createPriceBook,
  listPriceBooks,
  fetchCustomers,
  type CustomerRow,
  type PriceBookRow,
} from "@/lib/services/sales";
import { useAuth } from "@/hooks/useAuth";
import { fmtDay, hasPermission, salesActionError, SalesMeError, useSalesMe } from "../_lib/helpers";
import { fmtMoney } from "@/lib/format";
import {
  Button,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Select,
  Tabs,
  ToastRegion,
  type ToastItem,
} from "@/components/uikit";

export default function PriceBooksPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const tc = t.sales.pricebooks;
  const { token, ready } = useAuth(locale);
  const { meId, permissions, meReady, meFailed, retryMe } = useSalesMe(token);

  const [books, setBooks] = useState<PriceBookRow[] | null>(null);
  const [phase, setPhase] = useState<"loading" | "error" | "success">("loading");
  const [customers, setCustomers] = useState<CustomerRow[]>([]);

  const [name, setName] = useState("");
  const [currency, setCurrency] = useState("BDT");
  const [precedence, setPrecedence] = useState("0");
  const [bookErrors, setBookErrors] = useState<{ name?: string; currency?: string; precedence?: string }>({});
  const [creatingBook, setCreatingBook] = useState(false);
  const [bookError, setBookError] = useState<string | null>(null);

  const [entryBookId, setEntryBookId] = useState("");
  const [entrySku, setEntrySku] = useState("");
  const [entryMinQty, setEntryMinQty] = useState("0");
  const [entryPrice, setEntryPrice] = useState("");
  const [entryFrom, setEntryFrom] = useState("");
  const [entryTo, setEntryTo] = useState("");
  const [entryRule, setEntryRule] = useState("");
  const [entryErrors, setEntryErrors] = useState<{ book?: string; sku?: string; price?: string; from?: string }>({});
  const [addingEntry, setAddingEntry] = useState(false);
  const [entryError, setEntryError] = useState<string | null>(null);

  const [contractCustomer, setContractCustomer] = useState("");
  const [contractSku, setContractSku] = useState("");
  const [contractMinQty, setContractMinQty] = useState("0");
  const [contractPrice, setContractPrice] = useState("");
  const [contractFrom, setContractFrom] = useState("");
  const [contractTo, setContractTo] = useState("");
  const [contractErrors, setContractErrors] = useState<{ customer?: string; sku?: string; price?: string; from?: string }>({});
  const [addingContract, setAddingContract] = useState(false);
  const [contractError, setContractError] = useState<string | null>(null);
  const [tab, setTab] = useState("books");
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
    const res = await listPriceBooks(token);
    if (res.ok) {
      setBooks(res.data);
      setPhase("success");
    } else {
      setBooks(null);
      setPhase("error");
    }
  }, [token]);
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void fetchCustomers(token).then((res) => { if (!cancelled && res.ok) setCustomers(res.data); });
    return () => { cancelled = true; };
  }, [token]);

  async function submitBook(e: FormEvent) {
    e.preventDefault();
    const errs: { name?: string; currency?: string; precedence?: string } = {};
    if (!name.trim()) errs.name = tc.errors.nameRequired;
    if (!currency.trim()) errs.currency = tc.errors.currencyRequired;
    if (!Number.isFinite(Number(precedence)) || Number(precedence) < 0) errs.precedence = tc.errors.precedenceInvalid;
    setBookErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setCreatingBook(true);
    setBookError(null);
    const res = await createPriceBook(token, { name: name.trim(), currency: currency.trim(), precedence: Number(precedence) });
    setCreatingBook(false);
    if (!res.ok) {
      setBookError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.created + " " + res.data.id);
    setName("");
    await load();
  }

  async function submitEntry(e: FormEvent) {
    e.preventDefault();
    const errs: { book?: string; sku?: string; price?: string; from?: string } = {};
    if (!entryBookId) errs.book = tc.loadFailed;
    if (!entrySku.trim()) errs.sku = tc.errors.skuRequired;
    if (!Number.isFinite(Number(entryMinQty)) || Number(entryMinQty) < 0) errs.sku = tc.errors.qtyInvalid;
    if (!entryPrice.trim() || !Number.isFinite(Number(entryPrice)) || Number(entryPrice) < 0) errs.price = tc.errors.priceInvalid;
    if (!entryFrom) errs.from = tc.errors.fromRequired;
    setEntryErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setAddingEntry(true);
    setEntryError(null);
    const res = await addBookEntry(token, entryBookId, {
      itemSku: entrySku.trim(),
      minQty: Number(entryMinQty),
      unitPrice: Number(entryPrice),
      effectiveFrom: entryFrom,
      ...(entryTo ? { effectiveTo: entryTo } : {}),
      ...(entryRule.trim() ? { rule: entryRule.trim() } : {}),
    });
    setAddingEntry(false);
    if (!res.ok) {
      setEntryError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.entryAdded);
    setEntrySku("");
    setEntryPrice("");
    setEntryFrom("");
    setEntryTo("");
    setEntryRule("");
    await load();
  }

  async function submitContract(e: FormEvent) {
    e.preventDefault();
    const errs: { customer?: string; sku?: string; price?: string; from?: string } = {};
    if (!contractCustomer) errs.customer = tc.errors.customerRequired;
    if (!contractSku.trim()) errs.sku = tc.errors.skuRequired;
    if (!contractPrice.trim() || !Number.isFinite(Number(contractPrice)) || Number(contractPrice) < 0) errs.price = tc.errors.priceInvalid;
    if (!contractFrom) errs.from = tc.errors.fromRequired;
    setContractErrors(errs);
    if (Object.keys(errs).length > 0 || !token) return;
    setAddingContract(true);
    setContractError(null);
    const res = await createContractPrice(token, {
      customerId: contractCustomer,
      itemSku: contractSku.trim(),
      minQty: Number(contractMinQty || 0),
      unitPrice: Number(contractPrice),
      effectiveFrom: contractFrom,
      ...(contractTo ? { effectiveTo: contractTo } : {}),
    });
    setAddingContract(false);
    if (!res.ok) {
      setContractError(salesActionError(t, res.code, res.message));
      return;
    }
    pushToast("success", tc.contractAdded);
    setContractSku("");
    setContractPrice("");
    setContractFrom("");
    setContractTo("");
  }

  const writeGate = hasPermission(permissions, "pricebooks:write");

  if (!ready || (phase === "loading" && books === null)) return <LoadingSkeleton rows={7} />;
  if (phase === "error") return <ErrorState message={tc.loadFailed} retryLabel={t.common.retry} onRetry={() => void load()} />;

  const list = books ?? [];

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{tc.title}</h1>
        <p className="t-caption">{tc.subtitle}</p>
      </div>
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.common.cancel} />
      {meFailed ? <SalesMeError t={t} onRetry={retryMe} /> : null}

      <Tabs
        label={tc.title}
        activeId={tab}
        onChange={setTab}
        tabs={[
          { id: "books", label: tc.tabBooks, content: (
            <div className="stack">
              {list.length === 0 ? <EmptyState title={tc.empty} /> : list.map((book) => (
                <section className="card" key={book.id}>
                  <h2 className="t-title">
                    <span className="mono">{book.name}</span> · {tc.bookColumns.currency}: <span className="mono">{book.currency}</span> · {tc.bookColumns.precedence}: <span className="mono">{book.precedence}</span>
                  </h2>
                  <h3 className="t-body-strong">{tc.entriesTitle}</h3>
                  {book.entries.length === 0 ? (
                    <p className="t-caption">{tc.noEntries}</p>
                  ) : (
                    <div className="table-scroll">
                      <table className="table table--lines">
                        <thead>
                          <tr>
                            <th scope="col">{tc.entryColumns.sku}</th>
                            <th scope="col">{tc.entryColumns.minQty}</th>
                            <th scope="col">{tc.entryColumns.unitPrice}</th>
                            <th scope="col">{tc.entryColumns.from}</th>
                            <th scope="col">{tc.entryColumns.to}</th>
                            <th scope="col">{tc.entryColumns.rule}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {book.entries.map((entry, i) => (
                            <tr key={book.id + "-" + i}>
                              <td data-label={tc.entryColumns.sku} className="mono">{entry.itemSku}</td>
                              <td data-label={tc.entryColumns.minQty} className="mono">{entry.minQty}</td>
                              <td data-label={tc.entryColumns.unitPrice} className="mono">{fmtMoney(entry.unitPrice, locale, book.currency)}</td>
                              <td data-label={tc.entryColumns.from}><span className="t-caption">{fmtDay(entry.effectiveFrom, locale)}</span></td>
                              <td data-label={tc.entryColumns.to}><span className="t-caption">{entry.effectiveTo ? fmtDay(entry.effectiveTo, locale) : tc.openEnded}</span></td>
                              <td data-label={tc.entryColumns.rule} className="mono">{entry.rule ?? "—"}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </section>
              ))}

              <section className="card">
                <h2 className="t-title">{tc.createTitle}</h2>
                <form onSubmit={(e) => void submitBook(e)}>
                  <div className="form-grid">
                    <Input id="pb-name" label={tc.name} value={name} onChange={(e) => setName(e.target.value)} error={bookErrors.name} />
                    <Input id="pb-currency" label={tc.currency} value={currency} onChange={(e) => setCurrency(e.target.value)} error={bookErrors.currency} />
                    <Input id="pb-precedence" label={tc.precedence} hint={tc.precedenceHint} type="number" min="0" step="1" value={precedence} onChange={(e) => setPrecedence(e.target.value)} error={bookErrors.precedence} />
                  </div>
                  {bookError ? <p className="banner" role="alert">{bookError}</p> : null}
                  <p><Button type="submit" loading={creatingBook} disabled={writeGate === false}>{tc.create}</Button></p>
                </form>
              </section>

              <section className="card">
                <h2 className="t-title">{tc.addEntryTitle}</h2>
                <form onSubmit={(e) => void submitEntry(e)}>
                  <div className="form-grid">
                    <Select
                      id="pb-entry-book"
                      label={tc.bookColumns.book}
                      value={entryBookId}
                      onChange={(e) => setEntryBookId(e.target.value)}
                      options={list.map((b) => ({ value: b.id, label: b.name + " (" + b.currency + ")" }))}
                      placeholder={tc.bookColumns.book}
                      error={entryErrors.book}
                    />
                    <Input id="pb-entry-sku" label={tc.entrySku} value={entrySku} onChange={(e) => setEntrySku(e.target.value)} error={entryErrors.sku} />
                    <Input id="pb-entry-minqty" label={tc.entryMinQty} type="number" min="0" step="any" value={entryMinQty} onChange={(e) => setEntryMinQty(e.target.value)} />
                    <Input id="pb-entry-price" label={tc.entryPrice} type="number" min="0" step="any" value={entryPrice} onChange={(e) => setEntryPrice(e.target.value)} error={entryErrors.price} />
                    <Input id="pb-entry-from" label={tc.entryFrom} type="date" value={entryFrom} onChange={(e) => setEntryFrom(e.target.value)} error={entryErrors.from} />
                    <Input id="pb-entry-to" label={tc.entryTo} type="date" value={entryTo} onChange={(e) => setEntryTo(e.target.value)} />
                    <Input id="pb-entry-rule" label={tc.entryRule} value={entryRule} onChange={(e) => setEntryRule(e.target.value)} />
                  </div>
                  {entryError ? <p className="banner" role="alert">{entryError}</p> : null}
                  <p><Button type="submit" loading={addingEntry} disabled={writeGate === false}>{tc.addEntry}</Button></p>
                </form>
              </section>
            </div>
          ),
          },

          { id: "contracts", label: tc.tabContracts, content: (
            <div className="stack" style={{ marginTop: 0 }}>
              <section className="card">
                <h2 className="t-title">{tc.contractTitle}</h2>
                <p className="t-caption">{tc.contractHint}</p>
                <form onSubmit={(e) => void submitContract(e)}>
                  <div className="form-grid">
                    <Select
                      id="pb-contract-customer"
                      label={tc.contractCustomer}
                      value={contractCustomer}
                      onChange={(e) => setContractCustomer(e.target.value)}
                      options={customers.map((c) => ({ value: c.id, label: c.name + " (" + c.id + ")" }))}
                      placeholder={tc.contractCustomer}
                      error={contractErrors.customer}
                    />
                    <Input id="pb-contract-sku" label={tc.contractSku} value={contractSku} onChange={(e) => setContractSku(e.target.value)} error={contractErrors.sku} />
                    <Input id="pb-contract-minqty" label={tc.contractMinQty} type="number" min="0" step="any" value={contractMinQty} onChange={(e) => setContractMinQty(e.target.value)} />
                    <Input id="pb-contract-price" label={tc.contractPrice} type="number" min="0" step="any" value={contractPrice} onChange={(e) => setContractPrice(e.target.value)} error={contractErrors.price} />
                    <Input id="pb-contract-from" label={tc.contractFrom} type="date" value={contractFrom} onChange={(e) => setContractFrom(e.target.value)} error={contractErrors.from} />
                    <Input id="pb-contract-to" label={tc.contractTo} type="date" value={contractTo} onChange={(e) => setContractTo(e.target.value)} />
                  </div>
                  {contractError ? <p className="banner" role="alert">{contractError}</p> : null}
                  <p><Button type="submit" disabled={writeGate === false}>{tc.addContract}</Button></p>
                </form>
              </section>
            </div>
          ),
          },
        ]}
      />
    </div>
  );
}
