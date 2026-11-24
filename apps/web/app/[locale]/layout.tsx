import type { Metadata } from "next";
import type { ReactNode } from "react";
import Link from "next/link";
import { DM_Sans } from "next/font/google";
import { getDictionary, isLocale, LOCALES } from "@/lib/i18n";
import AppShell from "@/components/app-shell";
import "../globals.css";

// Reference theme typography: DM Sans variable (loaded via next/font, no runtime fetch).
const dmSans = DM_Sans({ subsets: ["latin"], display: "swap" });

export function generateStaticParams() {
  return LOCALES.map((locale) => ({ locale }));
}

// Unknown locales hard-404 at build time instead of rendering a broken locale.
export const dynamicParams = false;

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale: raw } = await params;
  const t = getDictionary(isLocale(raw) ? raw : "en");
  return { title: t.meta.appTitle };
}

export default async function LocaleLayout({ children, params }: { children: ReactNode; params: Promise<{ locale: string }> }) {
  const { locale: raw } = await params;
  const locale = isLocale(raw) ? raw : "en";
  const t = getDictionary(locale);
  // apply the persisted theme before first paint. Dark is the default (reference theme
  // convention: html.dark / html.light classes); "light" is the explicit stored choice.
  // SECURITY: the string is a compile-time constant with no interpolated/untrusted
  // content — nothing to sanitize, so dangerouslySetInnerHTML is safe here.
  const themeInit = `try{var t=localStorage.getItem("theme");if(t!=="light")document.documentElement.classList.add("dark");else document.documentElement.classList.remove("dark");}catch(e){document.documentElement.classList.add("dark");}`;
  return (
    <html lang={locale} dir="ltr">
      <body className={dmSans.className}>
        <script dangerouslySetInnerHTML={{ __html: themeInit }} />
        <AppShell locale={locale}>{children}</AppShell>
      </body>
    </html>
  );
}
