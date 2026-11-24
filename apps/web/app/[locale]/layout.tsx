import type { Metadata } from "next";
import type { ReactNode } from "react";
import Link from "next/link";
import { DM_Sans } from "next/font/google";
import { getDictionary, isLocale, LOCALES } from "@/lib/i18n";
import { LocaleSwitch } from "@/components/locale-switch";
import ThemeToggle from "@/components/theme-toggle";
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

// Sidebar nav groups — routes are locale-prefixed at render time below.
const NAV = [
  {
    heading: "Overview",
    items: [{ href: "dashboard", label: "Dashboard" }],
  },
  {
    heading: "Procurement",
    items: [
      { href: "procurement/suppliers", label: "Suppliers" },
      { href: "procurement/purchase-orders", label: "Purchase orders" },
    ],
  },
  {
    heading: "Inventory",
    items: [
      { href: "inventory", label: "Stock" },
      { href: "inventory/warehouses", label: "Warehouses" },
      { href: "inventory/transfers", label: "Transfers" },
      { href: "inventory/counts", label: "Counts" },
      { href: "inventory/valuation", label: "Valuation" },
    ],
  },
  {
    heading: "Finance",
    items: [{ href: "finance/journals", label: "Journals" }],
  },
  {
    heading: "System",
    items: [{ href: "design", label: "Design system" }],
  },
];

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
        <div className="app-shell">
          <aside className="sidebar">
            <Link href={`/${locale}`} className="sidebar__brand">
              TeamZlab<span>ERP</span>
            </Link>
            {NAV.map((group) => (
              <nav key={group.heading} className="sidebar__group" aria-label={group.heading}>
                <p className="sidebar__heading">{group.heading}</p>
                {group.items.map((item) => (
                  <Link key={item.href} href={`/${locale}/${item.href}`} className="sidebar__link">
                    {item.label}
                  </Link>
                ))}
              </nav>
            ))}
          </aside>
          <div className="shell__main">
            <header className="topbar">
              <Link href={`/${locale}`} className="topbar__title">
                {t.meta.appTitle}
              </Link>
              <div className="topbar__actions">
                <LocaleSwitch locale={locale} otherLabel={t.meta.otherLocale} />
                <ThemeToggle lightLabel="Switch to light theme" darkLabel="Switch to dark theme" />
              </div>
            </header>
            <main className="content">{children}</main>
          </div>
        </div>
      </body>
    </html>
  );
}
