import { localeTag, type Locale } from "./i18n";

// Amounts/IDs always mono + locale-formatted (03_UI_Spec: never toFixed by hand).

export function fmtNumber(value: number, locale: Locale): string {
  return new Intl.NumberFormat(localeTag(locale)).format(value);
}

export function fmtMoney(value: number, locale: Locale, currency: string): string {
  return new Intl.NumberFormat(localeTag(locale), { style: "currency", currency }).format(value);
}

export function fmtDate(iso: string, locale: Locale): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeTag(locale), { dateStyle: "medium", timeStyle: "short" }).format(date);
}
