"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

// Grouped app navigation. Hidden on the pre-auth routes (login, select-entity):
// an unauthenticated visitor gets the auth card, not a map of the app.
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
    heading: "Sales",
    items: [
      { href: "sales/leads", label: "Leads" },
      { href: "sales/crm", label: "CRM" },
      { href: "sales/opportunities", label: "Opportunities" },
      { href: "sales/orders", label: "Sales orders" },
      { href: "sales/invoices", label: "Invoices" },
      { href: "sales/collections", label: "Collections" },
      { href: "sales/customers", label: "Customers" },
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
    heading: "People",
    items: [
      { href: "people/workers", label: "Workers" },
      { href: "people/time", label: "Time" },
      { href: "people/pay-codes", label: "Pay codes" },
      { href: "people/pay-groups", label: "Pay groups" },
      { href: "people/pay-runs", label: "Pay runs" },
    ],
  },
  {
    heading: "Manufacturing",
    items: [
      { href: "mfg/boms", label: "BOM revisions" },
      { href: "mfg/production-orders", label: "Production orders" },
    ],
  },
  {
    heading: "System",
    items: [{ href: "design", label: "Design system" }],
  },
];

export default function AppSidebar({ locale }: { locale: string }) {
  const pathname = usePathname();
  if (pathname === `/${locale}/login` || pathname === `/${locale}/select-entity`) return null;

  const isActive = (href: string) =>
    pathname === `/${locale}/${href}` || pathname.startsWith(`/${locale}/${href}/`);

  return (
    <aside className="sidebar">
      <Link href={`/${locale}`} className="sidebar__brand">
        TeamZlab<span>ERP</span>
      </Link>
      {NAV.map((group) => (
        <nav key={group.heading} className="sidebar__group" aria-label={group.heading}>
          <p className="sidebar__heading">{group.heading}</p>
          {group.items.map((item) => (
            <Link
              key={item.href}
              href={`/${locale}/${item.href}`}
              className="sidebar__link"
              aria-current={isActive(item.href) ? "page" : undefined}
            >
              {item.label}
            </Link>
          ))}
        </nav>
      ))}
    </aside>
  );
}
