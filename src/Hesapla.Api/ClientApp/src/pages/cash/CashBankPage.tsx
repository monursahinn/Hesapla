import { useEffect, useState } from "react";
import { ActionIcon, Badge, Button, Card, Group, Modal, SegmentedControl, Select, SimpleGrid, Stack, Switch, Text, TextInput, Textarea, Tooltip, UnstyledButton } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useForm } from "@mantine/form";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { IconBan, IconBuildingBank, IconCash, IconEdit, IconFileSpreadsheet, IconPlus, IconSearch } from "@tabler/icons-react";
import { useSearchParams } from "react-router";
import { api } from "@/api/client";
import type { Account, Movement, Paged } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { MovementModal, type MovementPreset } from "@/components/MovementModal";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { downloadFile, MoneyInput, Pager } from "@/components/Shared";
import { parseTrDate } from "@/lib/dates";
import { date, money } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";
import { notify } from "@/lib/notify";

const INCOMING = ["TA", "GE"];
const TYPE_COLOR: Record<string, string> = { TA: "teal", GE: "teal", OD: "orange", GI: "red", VI: "blue" };

function AccountForm({ editing, onClose }: { editing: Partial<Account> | null; onClose: () => void }) {
  const form = useForm({
    initialValues: { type: "BA", name: "", bankName: "", iban: "", openingBalance: 0 as number | string, active: true },
    validate: { name: (v) => (v.trim() ? null : "Hesap adı girin") },
  });
  useEffect(() => {
    if (!editing) return;
    form.setValues({ type: editing.type ?? "BA", name: editing.name ?? "", bankName: editing.bankName ?? "", iban: editing.iban ?? "", openingBalance: editing.openingBalance ?? 0, active: editing.active ?? true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);

  const isNew = !editing?.id;
  const save = useApiMutation<typeof form.values>({
    mutationFn: (v) => {
      const body = { ...v, name: v.name.trim(), bankName: v.type === "BA" ? v.bankName.trim() || null : null, iban: v.type === "BA" ? v.iban.trim() || null : null, openingBalance: Number(v.openingBalance) || 0 };
      return isNew ? api.post("/api/accounts", body) : api.put(`/api/accounts/${editing!.id}`, body);
    },
    invalidate: [["accounts"], ["dashboard"]],
    onSuccess: onClose,
  });

  return (
    <Modal opened={!!editing} onClose={onClose} title={isNew ? "Yeni kasa / banka hesabı" : "Hesabı düzenle"}>
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          <SegmentedControl data={[{ value: "KA", label: "Kasa" }, { value: "BA", label: "Banka" }]} {...form.getInputProps("type")} />
          <TextInput label="Hesap adı" placeholder={form.values.type === "KA" ? "Merkez Kasa" : "Ziraat TL hesabı"} maxLength={120} {...form.getInputProps("name")} />
          {form.values.type === "BA" && (
            <>
              <TextInput label="Banka" maxLength={120} {...form.getInputProps("bankName")} />
              <TextInput label="IBAN" placeholder="TR00 0000 0000 0000 0000 0000 00" maxLength={40} {...form.getInputProps("iban")} />
            </>
          )}
          <MoneyInput label="Açılış bakiyesi" allowNegative={form.values.type === "BA"} {...form.getInputProps("openingBalance")} />
          {!isNew && <Switch label="Aktif" {...form.getInputProps("active", { type: "checkbox" })} />}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>Vazgeç</Button>
            <Button type="submit" loading={save.isPending}>Kaydet</Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function CancelMovement({ movement, onClose }: { movement: Movement | null; onClose: () => void }) {
  const [reason, setReason] = useState("");
  useEffect(() => setReason(""), [movement]);
  const cancel = useApiMutation<void>({
    mutationFn: () => api.post(`/api/movements/${movement!.id}/cancel`, { reason: reason.trim() }),
    invalidate: [["accounts"], ["movements"], ["invoices"], ["customers"], ["dashboard"], ["reports"]],
    onSuccess: onClose,
  });
  return (
    <Modal opened={!!movement} onClose={onClose} title="Kayıt iptal edilsin mi?">
      <Stack>
        <Text size="sm">
          {movement && `${date(movement.date)} • ${movement.typeLabel} • ${money(movement.amount)}`}. Kayıt silinmez, iptal olarak işaretlenir ve bakiyelerden düşer.
        </Text>
        <Textarea label="İptal nedeni" autosize minRows={2} maxLength={255} value={reason} onChange={(e) => setReason(e.currentTarget.value)} data-autofocus />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Vazgeç</Button>
          <Button color="red" disabled={!reason.trim()} loading={cancel.isPending} onClick={() => cancel.mutate()}>İptal et</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

export default function CashBankPage() {
  const { canWrite } = useAuth();
  const [params, setParams] = useSearchParams();
  const type = params.get("type") ?? "";
  const [accountId, setAccountId] = useState<string | null>(null);
  const [from, setFrom] = useState<string | null>(null);
  const [to, setTo] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  const [movement, setMovement] = useState<MovementPreset | null>(null);
  const [accountEditing, setAccountEditing] = useState<Partial<Account> | null>(null);
  const [cancelling, setCancelling] = useState<Movement | null>(null);

  const accounts = useQuery({ queryKey: ["accounts"], queryFn: () => api.get<Account[]>("/api/accounts") });
  const q = useQuery({
    queryKey: ["movements", type, accountId, from, to, debounced, page],
    queryFn: () => api.get<Paged<Movement>>("/api/movements", { type, accountId, from, to, q: debounced, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });

  const signed = (m: Movement) => {
    if (m.type === "VI") return accountId && Number(accountId) === m.targetAccountId ? m.amount : -m.amount;
    return INCOMING.includes(m.type) ? m.amount : -m.amount;
  };

  return (
    <>
      <PageHeader
        title="Kasa ve banka"
        description="Tahsilat, ödeme, gider, gelir ve virmanlar"
        actions={
          <>
            <Button variant="default" leftSection={<IconFileSpreadsheet size={16} />}
              onClick={() => downloadFile(`/api/export/movements?type=${type}&from=${from ?? ""}&to=${to ?? ""}`).catch((e) => notify.error(e.message))}>
              Excel
            </Button>
            {canWrite && (
              <>
                <Button variant="default" leftSection={<IconPlus size={16} />} onClick={() => setAccountEditing({})}>Hesap</Button>
                <Button leftSection={<IconPlus size={16} />} onClick={() => setMovement({ type: (type as MovementPreset["type"]) || "TA" })}>Yeni kayıt</Button>
              </>
            )}
          </>
        }
      />

      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="lg">
        {(accounts.data ?? []).map((a) => (
          <UnstyledButton key={a.id} onClick={() => { setAccountId(accountId === String(a.id) ? null : String(a.id)); setPage(1); }}>
            <Card h="100%" style={{ outline: accountId === String(a.id) ? "2px solid var(--mantine-primary-color-filled)" : undefined, opacity: a.active ? 1 : 0.6 }}>
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <div style={{ minWidth: 0 }}>
                  <Text size="xs" c="dimmed" tt="uppercase" fw={600}>{a.type === "KA" ? "Kasa" : a.bankName || "Banka"}</Text>
                  <Text fw={600} truncate>{a.name}</Text>
                  <Text fz={22} fw={700} c={a.balance < 0 ? "red" : undefined} className="num">{money(a.balance)}</Text>
                </div>
                <Stack gap={4} align="flex-end">
                  {a.type === "KA" ? <IconCash size={22} opacity={0.6} /> : <IconBuildingBank size={22} opacity={0.6} />}
                  {canWrite && (
                    <Tooltip label="Düzenle">
                      <ActionIcon size="sm" variant="subtle" color="gray" aria-label="Hesabı düzenle" onClick={(e) => { e.stopPropagation(); setAccountEditing(a); }}>
                        <IconEdit size={14} />
                      </ActionIcon>
                    </Tooltip>
                  )}
                </Stack>
              </Group>
            </Card>
          </UnstyledButton>
        ))}
      </SimpleGrid>

      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}
      <Card>
        <DataTable
          toolbar={
            <Group gap="sm" wrap="wrap" w="100%">
              <SegmentedControl
                data={[{ value: "", label: "Tümü" }, { value: "TA", label: "Tahsilat" }, { value: "OD", label: "Ödeme" }, { value: "GI", label: "Gider" }, { value: "GE", label: "Gelir" }, { value: "VI", label: "Virman" }]}
                value={type}
                onChange={(v) => { setParams(v ? { type: v } : {}, { replace: true }); setPage(1); }}
              />
              <TextInput placeholder="Açıklama, cari, kategori" leftSection={<IconSearch size={16} />} value={search}
                onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} style={{ flex: 1, minWidth: 180 }} />
              <Select w={180} placeholder="Tüm hesaplar" clearable data={(accounts.data ?? []).map((a) => ({ value: String(a.id), label: a.name }))} value={accountId} onChange={(v) => { setAccountId(v); setPage(1); }} />
              <DateInput w={130} placeholder="Başlangıç" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} clearable value={from} onChange={(v) => { setFrom(v); setPage(1); }} />
              <DateInput w={130} placeholder="Bitiş" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} clearable value={to} onChange={(v) => { setTo(v); setPage(1); }} />
            </Group>
          }
          rows={q.data?.items}
          loading={q.isFetching}
          rowKey={(m) => m.id}
          emptyText="Kayıt bulunamadı."
          columns={[
            { key: "date", header: "Tarih", render: (m) => date(m.date), width: 100 },
            { key: "type", header: "Tür", render: (m) => <Badge color={m.cancelled ? "gray" : TYPE_COLOR[m.type]}>{m.cancelled ? "İptal" : m.typeLabel}</Badge> },
            {
              key: "detail",
              header: "Açıklama",
              render: (m) => (
                <div>
                  <Text size="sm" td={m.cancelled ? "line-through" : undefined}>
                    {m.customerTitle ?? m.categoryName ?? (m.type === "VI" ? `${m.accountName} → ${m.targetAccountName}` : "")}
                    {m.invoiceNo ? ` • ${m.invoiceNo}` : ""}
                  </Text>
                  {(m.description || m.cancelReason) && (
                    <Text size="xs" c="dimmed">{m.cancelled ? `İptal nedeni: ${m.cancelReason}` : m.description}</Text>
                  )}
                </div>
              ),
            },
            { key: "account", header: "Hesap", render: (m) => m.accountName, hideOnMobile: true },
            {
              key: "amount",
              header: "Tutar",
              align: "right",
              render: (m) => {
                const v = signed(m);
                return <Text size="sm" fw={600} c={m.cancelled ? "dimmed" : v >= 0 ? "teal" : "red"} className="num">{v >= 0 ? "+" : "−"}{money(Math.abs(v))}</Text>;
              },
            },
          ]}
          actions={(m) =>
            canWrite && !m.cancelled ? (
              <Tooltip label="İptal et">
                <ActionIcon variant="subtle" color="red" aria-label="İptal et" onClick={() => setCancelling(m)}>
                  <IconBan size={16} />
                </ActionIcon>
              </Tooltip>
            ) : null
          }
        />
        {q.data && <Pager page={page} total={q.data.total} pageSize={q.data.pageSize} onChange={setPage} />}
      </Card>

      <MovementModal preset={movement} onClose={() => setMovement(null)} />
      <AccountForm editing={accountEditing} onClose={() => setAccountEditing(null)} />
      <CancelMovement movement={cancelling} onClose={() => setCancelling(null)} />
    </>
  );
}
