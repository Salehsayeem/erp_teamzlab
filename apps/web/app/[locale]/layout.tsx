import type { Metadata } from "next";
import type { ReactNode } from "react";
import Link from "next/link";
import { getDictionary, isLocale, LOCALES } from "@/lib/i18n";
import { LocaleSwitch } from "@/components/locale-switch";
import "../globals.css";

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
  return (
    <html lang={locale} dir="ltr">
      <body>
        <header className="topbar">
          <Link href={`/${locale}`} className="topbar__title">
            {t.meta.appTitle}
          </Link>
          <LocaleSwitch locale={locale} otherLabel={t.meta.otherLocale} />
        </header>
        <main className="page">{children}</main>
      </body>
    </html>
  );
}
