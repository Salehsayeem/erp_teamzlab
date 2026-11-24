"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { getDictionary, isLocale } from "@/lib/i18n";

// Grouped app navigation, fully dict-driven (nav.* + nav.groups in en/bn).
// Hidden on the pre-auth routes (login, select-entity): an unauthenticated
// visitor gets the auth card, not a map of the app. On mobile it renders as an
// overlay drawer (mobileOpen) that closes on navigation (onClose).
export default function AppSidebar({
  locale,
  mobileOpen = false,
  onClose,
}: {
  locale: string;
  mobileOpen?: boolean;
  onClose?: () => void;
}) {
  const pathname = usePathname();
  const dict = getDictionary(isLocale(locale) ? locale : "en");
  if (pathname === `/${locale}/login` || pathname === `/${locale}/select-entity`) return null;

  const isActive = (href: string) =>
    pathname === `/${locale}/${href}` || pathname.startsWith(`/${locale}/${href}/`);

  const groups = [
    {
      heading: dict.nav.groups.overview,
      items: [{ href: "dashboard", label: dict.nav.dashboard }],
    },
    {
      heading: dict.nav.groups.procurement,
      items: [
        { href: "procurement/suppliers", label: dict.nav.suppliers },
        { href: "procurement/purchase-orders", label: dict.nav.purchaseOrders },
        { href: "procurement/sourcing", label: dict.nav.sourcing },
        { href: "procurement/contracts", label: dict.nav.contracts },
      ],
    },
    {
      heading: dict.nav.groups.sales,
      items: [
        { href: "sales/leads", label: dict.nav.leads },
        { href: "sales/crm", label: dict.nav.crm },
        { href: "sales/opportunities", label: dict.nav.opportunities },
        { href: "sales/orders", label: dict.nav.salesOrders },
        { href: "sales/invoices", label: dict.nav.invoices },
        { href: "sales/collections", label: dict.nav.collections },
        { href: "sales/customers", label: dict.nav.customers },
      ],
    },
    {
      heading: dict.nav.groups.inventory,
      items: [
        { href: "inventory", label: dict.nav.inventory },
        { href: "inventory/warehouses", label: dict.nav.warehouses },
        { href: "inventory/transfers", label: dict.nav.transfers },
        { href: "inventory/counts", label: dict.nav.counts },
        { href: "inventory/valuation", label: dict.nav.valuation },
      ],
    },
    {
      heading: dict.nav.groups.finance,
      items: [{ href: "finance/journals", label: dict.nav.journals }],
    },
    {
      heading: dict.nav.groups.people,
      items: [
        { href: "people/workers", label: dict.nav.workers },
        { href: "people/time", label: dict.nav.time },
        { href: "people/pay-codes", label: dict.nav.payCodes },
        { href: "people/pay-groups", label: dict.nav.payGroups },
        { href: "people/pay-runs", label: dict.nav.payRuns },
      ],
    },
    {
      heading: dict.nav.groups.mfg,
      items: [
        { href: "mfg/boms", label: dict.nav.boms },
        { href: "mfg/production-orders", label: dict.nav.productionOrders },
      ],
    },
    {
      heading: dict.nav.groups.system,
      items: [{ href: "design", label: dict.nav.groups.designSystem }],
    },
  ];

  return (
    <>
      {mobileOpen ? (
        <div
          className="drawer-scrim"
          onClick={onClose}
          aria-hidden="true"
        />
      ) : null}
      <aside className={mobileOpen ? "sidebar sidebar--open" : "sidebar"} aria-label={dict.nav.groups.overview}>
        <Link href={`/${locale}`} className="sidebar__brand" onClick={onClose}>
          TeamZlab<span>ERP</span>
        </Link>
        {groups.map((group) => (
          <nav key={group.heading} className="sidebar__group" aria-label={group.heading}>
            <p className="sidebar__heading">{group.heading}</p>
            {group.items.map((item) => (
              <Link
                key={item.href}
                href={`/${locale}/${item.href}`}
                className="sidebar__link"
                aria-current={isActive(item.href) ? "page" : undefined}
                onClick={onClose}
              >
                {item.label}
              </Link>
            ))}
          </nav>
        ))}
      </aside>
    </>
  );
}
