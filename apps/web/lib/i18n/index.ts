import { bn } from "./bn";
import { en, type Dict } from "./en";

export type Locale = "en" | "bn";
export const LOCALES: readonly Locale[] = ["en", "bn"];

export function isLocale(value: string): value is Locale {
  return value === "en" || value === "bn";
}

export function getDictionary(locale: Locale): Dict {
  return locale === "bn" ? bn : en;
}

export function localeTag(locale: Locale): string {
  return locale === "bn" ? "bn-BD" : "en-US";
}

export type { Dict };
