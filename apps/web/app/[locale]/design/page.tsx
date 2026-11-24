"use client";

import { useCallback, useRef, useState } from "react";
import { useParams } from "next/navigation";
import { getDictionary, isLocale, type Dict, type Locale } from "@/lib/i18n";
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorState,
  Input,
  LoadingSkeleton,
  Modal,
  Pagination,
  Select,
  Tabs,
  Textarea,
  ToastRegion,
  statusTone,
  type ToastItem,
  type ToastTone,
} from "@/components/uikit";

const BADGE_STATUSES = ["Draft", "PendingApproval", "Approved", "Rejected"] as const;

export default function DesignPage() {
  const params = useParams<{ locale: string }>();
  const locale: Locale = isLocale(params.locale) ? params.locale : "en";
  const t = getDictionary(locale);
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextToastId = useRef(0);

  const pushToast = useCallback((tone: ToastTone, message: string) => {
    nextToastId.current += 1;
    const id = `design-toast-${nextToastId.current}`;
    setToasts((prev) => [...prev.slice(-2), { id, tone, message }]);
  }, []);

  const dismissToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }, []);

  return (
    <div className="stack">
      <div className="page-header">
        <h1 className="t-display">{t.design.title}</h1>
        <p className="t-caption">{t.design.subtitle}</p>
      </div>
      <ButtonsSection t={t} />
      <BadgesSection t={t} />
      <FormsSection t={t} />
      <StatesSection t={t} onAction={() => pushToast("success", t.design.states.actionToast)} />
      <FeedbackSection t={t} onToast={pushToast} />
      <OverlaysSection t={t} onToast={pushToast} />
      <NavigationSection t={t} />
      <ToastRegion toasts={toasts} onDismiss={dismissToast} closeLabel={t.design.feedback.dismiss} />
    </div>
  );
}

function ButtonsSection({ t }: { t: Dict }) {
  const [loading, setLoading] = useState(false);
  function demoLoading() {
    setLoading(true);
    window.setTimeout(() => setLoading(false), 1200);
  }
  return (
    <Card title={t.design.sections.buttons}>
      <div className="btn-row">
        <Button>{t.design.buttons.primary}</Button>
        <Button variant="secondary">{t.design.buttons.secondary}</Button>
        <Button variant="danger">{t.design.buttons.danger}</Button>
        <Button variant="secondary" small>
          {t.design.buttons.small}
        </Button>
        <Button disabled>{t.design.buttons.disabled}</Button>
        <Button loading={loading} onClick={demoLoading}>
          {t.design.buttons.loading}
        </Button>
      </div>
    </Card>
  );
}

function BadgesSection({ t }: { t: Dict }) {
  return (
    <Card title={t.design.sections.badges}>
      <p className="t-caption">{t.design.badgesNote}</p>
      <div className="btn-row">
        {BADGE_STATUSES.map((status) => (
          <Badge key={status} tone={statusTone(status)}>
            {t.common.status[status]}
          </Badge>
        ))}
      </div>
    </Card>
  );
}

function FormsSection({ t }: { t: Dict }) {
  const [name, setName] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const warehouseError = warehouseId ? undefined : t.design.forms.warehouse.error;
  const warehouseOptions = [
    { value: "wh-1", label: t.design.forms.options.dhaka },
    { value: "wh-2", label: t.design.forms.options.chattogram },
  ];
  return (
    <Card title={t.design.sections.forms}>
      <div className="form-grid">
        <Input id="design-name" label={t.design.forms.name.label} hint={t.design.forms.name.hint} value={name} onChange={(e) => setName(e.target.value)} />
        <Input id="design-email" label={t.design.forms.email.label} type="email" error={t.design.forms.email.error} />
        <Input id="design-code" label={t.design.forms.code.label} defaultValue="WH-001" readOnly />
        <Input id="design-locked" label={t.design.forms.locked.label} hint={t.design.forms.locked.hint} disabled />
        <Textarea id="design-notes" label={t.design.forms.notes.label} hint={t.design.forms.notes.hint} />
        <Select
          id="design-warehouse"
          label={t.design.forms.warehouse.label}
          placeholder={t.design.forms.warehouse.placeholder}
          options={warehouseOptions}
          value={warehouseId}
          onChange={(e) => setWarehouseId(e.target.value)}
          error={warehouseError}
        />
      </div>
    </Card>
  );
}

function StatesSection({ t, onAction }: { t: Dict; onAction: () => void }) {
  const [retrying, setRetrying] = useState(false);
  function retry() {
    setRetrying(true);
    window.setTimeout(() => setRetrying(false), 900);
  }
  return (
    <Card title={t.design.sections.states}>
      <div className="demo-grid">
        <EmptyState
          title={t.design.states.emptyTitle}
          description={t.design.states.emptyDescription}
          action={{ label: t.design.states.emptyAction, onClick: onAction }}
        />
        {retrying ? <LoadingSkeleton rows={3} label={t.common.loading} /> : <ErrorState message={t.design.states.errorTitle} retryLabel={t.common.retry} onRetry={retry} />}
      </div>
    </Card>
  );
}

function FeedbackSection({ t, onToast }: { t: Dict; onToast: (tone: ToastTone, message: string) => void }) {
  return (
    <Card title={t.design.sections.feedback}>
      <h3 className="t-body-strong">{t.design.feedback.skeleton}</h3>
      <LoadingSkeleton rows={3} label={t.common.loading} />
      <h3 className="t-body-strong">{t.design.feedback.toasts}</h3>
      <p className="t-caption">{t.design.feedback.liveRegionNote}</p>
      <div className="btn-row">
        <Button variant="secondary" onClick={() => onToast("success", t.design.feedback.toastSuccess)}>
          {t.design.feedback.triggerSuccess}
        </Button>
        <Button variant="secondary" onClick={() => onToast("info", t.design.feedback.toastInfo)}>
          {t.design.feedback.triggerInfo}
        </Button>
        <Button variant="danger" onClick={() => onToast("error", t.design.feedback.toastError)}>
          {t.design.feedback.triggerError}
        </Button>
      </div>
    </Card>
  );
}

function OverlaysSection({ t, onToast }: { t: Dict; onToast: (tone: ToastTone, message: string) => void }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  function confirmReversal() {
    setBusy(true);
    window.setTimeout(() => {
      setBusy(false);
      setOpen(false);
      onToast("success", t.design.overlays.confirmed);
    }, 900);
  }
  return (
    <Card title={t.design.sections.overlays}>
      <p className="t-caption">{t.design.overlays.hint}</p>
      <Button onClick={() => setOpen(true)}>{t.design.overlays.openModal}</Button>
      <Modal open={open} onClose={() => setOpen(false)} busy={busy} title={t.design.overlays.modalTitle} closeLabel={t.design.overlays.close}>
        <p className="t-body">{t.design.overlays.modalBody}</p>
        <div className="modal__actions">
          <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
            {t.design.overlays.cancel}
          </Button>
          <Button variant="danger" loading={busy} onClick={confirmReversal}>
            {t.design.overlays.confirm}
          </Button>
        </div>
      </Modal>
    </Card>
  );
}

function NavigationSection({ t }: { t: Dict }) {
  const [activeId, setActiveId] = useState("wh");
  const [page, setPage] = useState(2);
  const tabContent = <p className="t-body">{t.design.navigation.tabBody}</p>;
  const tabs = [
    { id: "wh", label: t.design.navigation.tabWarehouses, content: tabContent },
    { id: "loc", label: t.design.navigation.tabLocations, content: tabContent },
    { id: "val", label: t.design.navigation.tabValuation, content: tabContent },
  ];
  return (
    <Card title={t.design.sections.navigation}>
      <h3 className="t-body-strong">{t.design.navigation.tabs}</h3>
      <Tabs tabs={tabs} activeId={activeId} onChange={setActiveId} label={t.design.navigation.tabsLabel} />
      <h3 className="t-body-strong">{t.design.navigation.pagination}</h3>
      <Pagination
        page={page}
        pageCount={8}
        onChange={setPage}
        labels={{
          region: t.design.navigation.region,
          prev: t.design.navigation.previous,
          next: t.design.navigation.next,
          page: t.design.navigation.page,
        }}
      />
    </Card>
  );
}
