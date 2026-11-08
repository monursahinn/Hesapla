import { useEffect, useState } from "react";
import { Badge, Button, Card, Group, Modal, SegmentedControl, Select, SimpleGrid, Stack, Text, TextInput } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { IconBuildingStore, IconClock, IconCreditCard, IconSearch, IconUserOff } from "@tabler/icons-react";
import { api } from "@/api/client";
import type { Paged } from "@/api/types";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { Pager } from "@/components/Shared";
import { StatCard } from "@/components/StatCard";
import { parseTrDate } from "@/lib/dates";
import { date, dateTime } from "@/lib/format";
import { confirmAction, useApiMutation } from "@/lib/mutation";

interface Firm {
  id: number; name: string; taxNumber?: string | null; email?: string | null; phone?: string | null; plan: string; subscriptionEnd: string;
  status: string; users: number; invoices: number; createdAt: string; lastLogin?: string | null; expired: boolean;
}

const PLANS = [{ value: "DE", label: "Deneme" }, { value: "ST", label: "Standart" }, { value: "PR", label: "Profesyonel" }];

function SubscriptionModal({ firm, onClose }: { firm: Firm | null; onClose: () => void }) {
  const [plan, setPlan] = useState("ST");
  const [end, setEnd] = useState<string | null>(null);
  useEffect(() => {
    if (!firm) return;
    setPlan(firm.plan === "DE" ? "ST" : firm.plan);
    const base = new Date(Math.max(Date.now(), new Date(firm.subscriptionEnd).getTime()));
    base.setFullYear(base.getFullYear() + 1);
    setEnd(base.toISOString().slice(0, 10));
  }, [firm]);
  const save = useApiMutation<void>({
    mutationFn: () => api.put(`/api/platform/firms/${firm!.id}/subscription`, { plan, end }),
    invalidate: [["platform"]],
    onSuccess: onClose,
  });
  return (
    <Modal opened={!!firm} onClose={onClose} title={`Abonelik — ${firm?.name ?? ""}`}>
      <Stack>
        <Text size="sm" c="dimmed">Şu an: {PLANS.find((p) => p.value === firm?.plan)?.label}, bitiş {date(firm?.subscriptionEnd)}</Text>
        <Select label="Paket" data={PLANS} value={plan} onChange={(v) => v && setPlan(v)} />
        <DateInput label="Bitiş tarihi" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} value={end} onChange={setEnd} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Vazgeç</Button>
          <Button disabled={!end} loading={save.isPending} onClick={() => save.mutate()}>Kaydet</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Platform yöneticisi: müşteri firmalar, abonelikleri ve askıya alma. Firma verilerine erişemez. */
export default function PlatformPage() {
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 300);
  const [filter, setFilter] = useState("");
  const [page, setPage] = useState(1);
  const [subscription, setSubscription] = useState<Firm | null>(null);

  const stats = useQuery({ queryKey: ["platform", "stats"], queryFn: () => api.get<{ total: number; trial: number; paid: number; expired: number; suspended: number }>("/api/platform/stats") });
  const q = useQuery({
    queryKey: ["platform", "firms", debounced, filter, page],
    queryFn: () => api.get<Paged<Firm>>("/api/platform/firms", { q: debounced, filter, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });
  const status = useApiMutation<{ id: number; active: boolean }>({
    mutationFn: ({ id, active }) => api.put(`/api/platform/firms/${id}/status`, { active }),
    invalidate: [["platform"]],
  });

  const s = stats.data;
  return (
    <>
      <PageHeader title="Firmalar" description="Müşteri firmalar ve abonelikleri" />
      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="lg">
        <StatCard label="Toplam firma" value={s?.total ?? 0} icon={<IconBuildingStore size={22} />} loading={stats.isPending} />
        <StatCard label="Ücretli abone" value={s?.paid ?? 0} icon={<IconCreditCard size={22} />} color="teal" loading={stats.isPending} />
        <StatCard label="Denemede" value={s?.trial ?? 0} icon={<IconClock size={22} />} color="yellow" loading={stats.isPending} />
        <StatCard label="Süresi dolan / askıda" value={`${s?.expired ?? 0} / ${s?.suspended ?? 0}`} icon={<IconUserOff size={22} />} color="red" loading={stats.isPending} />
      </SimpleGrid>
      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}
      <Card>
        <DataTable
          toolbar={
            <Group gap="sm" wrap="wrap" w="100%">
              <TextInput placeholder="Unvan, e-posta, vergi no" leftSection={<IconSearch size={16} />} value={search}
                onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} style={{ flex: 1, minWidth: 200 }} />
              <SegmentedControl
                data={[{ value: "", label: "Tümü" }, { value: "trial", label: "Deneme" }, { value: "paid", label: "Ücretli" }, { value: "expired", label: "Süresi dolan" }, { value: "suspended", label: "Askıda" }]}
                value={filter}
                onChange={(v) => { setFilter(v); setPage(1); }}
              />
            </Group>
          }
          rows={q.data?.items}
          loading={q.isFetching}
          rowKey={(f) => f.id}
          columns={[
            {
              key: "name",
              header: "Firma",
              render: (f) => (
                <div>
                  <Text size="sm" fw={500}>{f.name}</Text>
                  <Text size="xs" c="dimmed">{f.email} {f.phone ? `• ${f.phone}` : ""}</Text>
                </div>
              ),
            },
            {
              key: "plan",
              header: "Paket",
              render: (f) => (
                <Group gap={4}>
                  <Badge color={f.plan === "DE" ? "yellow" : "teal"}>{PLANS.find((p) => p.value === f.plan)?.label}</Badge>
                  {f.status === "AS" && <Badge color="red">Askıda</Badge>}
                </Group>
              ),
            },
            { key: "end", header: "Bitiş", render: (f) => <Text size="sm" c={f.expired ? "red" : undefined}>{date(f.subscriptionEnd)}</Text> },
            { key: "usage", header: "Kullanım", render: (f) => `${f.users} kullanıcı • ${f.invoices} fatura`, hideOnMobile: true },
            { key: "last", header: "Son giriş", render: (f) => (f.lastLogin ? dateTime(f.lastLogin) : "—"), hideOnMobile: true },
          ]}
          actions={(f) => (
            <Group gap={4} wrap="nowrap">
              <Button size="xs" variant="light" onClick={() => setSubscription(f)}>Abonelik</Button>
              <Button
                size="xs"
                variant="subtle"
                color={f.status === "AS" ? "teal" : "red"}
                onClick={() =>
                  confirmAction({
                    title: f.status === "AS" ? "Firma aktifleştirilsin mi?" : "Firma askıya alınsın mı?",
                    message: f.status === "AS" ? `${f.name} yeniden giriş yapabilecek.` : `${f.name} kullanıcıları giriş yapamaz ve kayıt giremez.`,
                    danger: f.status !== "AS",
                    onConfirm: () => status.mutate({ id: f.id, active: f.status === "AS" }),
                  })
                }
              >
                {f.status === "AS" ? "Aktifleştir" : "Askıya al"}
              </Button>
            </Group>
          )}
        />
        {q.data && <Pager page={page} total={q.data.total} pageSize={q.data.pageSize} onChange={setPage} />}
      </Card>
      <SubscriptionModal firm={subscription} onClose={() => setSubscription(null)} />
    </>
  );
}
