import { useState } from "react";
import { Anchor, Button, Card, Grid, Group, SimpleGrid, Stack, Text } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useQuery } from "@tanstack/react-query";
import { IconArrowDownLeft, IconArrowUpRight, IconEdit, IconFilePlus, IconFileSpreadsheet } from "@tabler/icons-react";
import { Link, useNavigate, useParams } from "react-router";
import { api } from "@/api/client";
import { CUSTOMER_TYPES, type Statement } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { MovementModal, type MovementPreset } from "@/components/MovementModal";
import { PageHeader } from "@/components/PageHeader";
import { PageError, PageLoader } from "@/components/PageLoader";
import { BalanceText, downloadFile, firstOfYearIso, todayIso } from "@/components/Shared";
import { parseTrDate } from "@/lib/dates";
import { date, money } from "@/lib/format";
import { notify } from "@/lib/notify";
import { CustomerForm } from "./CustomerForm";

function Info({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm">{value || "—"}</Text>
    </div>
  );
}

/** Cari kartı ve ekstre: dönem başı devir, faturalar ve tahsilat/ödemeler yürüyen bakiyeyle. */
export default function CustomerDetailPage() {
  const id = Number(useParams().id);
  const { canWrite } = useAuth();
  const navigate = useNavigate();
  const [from, setFrom] = useState<string | null>(firstOfYearIso());
  const [to, setTo] = useState<string | null>(todayIso());
  const [editing, setEditing] = useState(false);
  const [movement, setMovement] = useState<MovementPreset | null>(null);

  const q = useQuery({
    queryKey: ["customers", id, "statement", from, to],
    queryFn: () => api.get<Statement>(`/api/customers/${id}/statement`, { from, to }),
    placeholderData: (p) => p,
  });

  if (q.isPending) return <PageLoader />;
  if (q.isError) return <PageError error={q.error} onRetry={() => q.refetch()} />;

  const s = q.data!;
  const c = s.customer;
  const customer = c.type !== "TE";
  const supplier = c.type !== "MU";

  return (
    <>
      <PageHeader
        title={c.title}
        description={`${c.code} • ${CUSTOMER_TYPES.find((t) => t.value === c.type)?.label}${c.active ? "" : " • Pasif"}`}
        actions={
          canWrite && (
            <>
              {customer && (
                <Button leftSection={<IconFilePlus size={16} />} onClick={() => navigate(`/invoices/new?type=SA&customerId=${id}`)}>
                  Satış faturası
                </Button>
              )}
              {supplier && (
                <Button variant="default" leftSection={<IconFilePlus size={16} />} onClick={() => navigate(`/invoices/new?type=AL&customerId=${id}`)}>
                  Alış faturası
                </Button>
              )}
              {customer && (
                <Button variant="default" leftSection={<IconArrowDownLeft size={16} />} onClick={() => setMovement({ type: "TA", customerId: id, lockType: true })}>
                  Tahsilat
                </Button>
              )}
              {supplier && (
                <Button variant="default" leftSection={<IconArrowUpRight size={16} />} onClick={() => setMovement({ type: "OD", customerId: id, lockType: true })}>
                  Ödeme
                </Button>
              )}
              <Button variant="subtle" leftSection={<IconEdit size={16} />} onClick={() => setEditing(true)}>
                Düzenle
              </Button>
            </>
          )
        }
      />

      <Grid gap="md" mb="md">
        <Grid.Col span={{ base: 12, md: 8 }}>
          <Card h="100%">
            <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="md">
              <Info label="Vergi no / T.C." value={c.taxNumber} />
              <Info label="Vergi dairesi" value={c.taxOffice} />
              <Info label="Vade" value={`${c.paymentTermDays} gün`} />
              <Info label="Telefon" value={c.phone} />
              <Info label="E-posta" value={c.email} />
              <Info label="Adres" value={[c.address, c.district, c.city].filter(Boolean).join(", ")} />
            </SimpleGrid>
            {c.notes && (
              <Text size="sm" c="dimmed" mt="md">
                {c.notes}
              </Text>
            )}
          </Card>
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Card h="100%">
            <Text size="xs" tt="uppercase" fw={600} c="dimmed">
              Güncel bakiye
            </Text>
            <BalanceText value={c.balance} size="xl" />
            <Text size="xs" c="dimmed" mt={6}>
              {c.balance > 0 ? "Cari size borçlu (alacağınız)." : c.balance < 0 ? "Siz cariye borçlusunuz." : "Hesap kapalı."}
            </Text>
          </Card>
        </Grid.Col>
      </Grid>

      <Card>
        <DataTable
          title="Ekstre"
          toolbar={
            <Group gap="sm" wrap="wrap">
              <DateInput size="xs" w={130} valueFormat="DD.MM.YYYY" dateParser={parseTrDate} value={from} onChange={setFrom} aria-label="Başlangıç" />
              <DateInput size="xs" w={130} valueFormat="DD.MM.YYYY" dateParser={parseTrDate} value={to} onChange={setTo} aria-label="Bitiş" />
              <Button size="xs" variant="default" leftSection={<IconFileSpreadsheet size={14} />}
                onClick={() => downloadFile(`/api/export/statement/${id}?from=${from ?? ""}&to=${to ?? ""}`).catch((e) => notify.error(e.message))}>
                Excel
              </Button>
            </Group>
          }
          rows={s.rows}
          rowKey={(r) => `${r.kind}-${r.refId}`}
          emptyText="Bu dönemde hareket yok."
          columns={[
            { key: "date", header: "Tarih", render: (r) => date(r.date), width: 100 },
            {
              key: "kind",
              header: "İşlem",
              render: (r) =>
                r.kind === "SA" || r.kind === "AL" ? (
                  <Anchor component={Link} to={`/invoices/${r.refId}`} size="sm">
                    {r.kindLabel}
                  </Anchor>
                ) : (
                  r.kindLabel
                ),
            },
            { key: "doc", header: "Belge no", render: (r) => r.documentNo ?? "—", hideOnMobile: true },
            { key: "desc", header: "Açıklama", render: (r) => r.description ?? "", hideOnMobile: true },
            { key: "debit", header: "Borç", align: "right", render: (r) => (r.debit ? money(r.debit) : "") },
            { key: "credit", header: "Alacak", align: "right", render: (r) => (r.credit ? money(r.credit) : "") },
            { key: "balance", header: "Bakiye", align: "right", render: (r) => <BalanceText value={r.balance} /> },
          ]}
        />
        <Group justify="space-between" mt="md">
          <Stack gap={0}>
            <Text size="xs" c="dimmed">
              Dönem başı devir ({date(s.from)})
            </Text>
            <BalanceText value={s.openingBalance} />
          </Stack>
          <Stack gap={0} align="flex-end">
            <Text size="xs" c="dimmed">
              Dönem sonu ({date(s.to)})
            </Text>
            <BalanceText value={s.closingBalance} />
          </Stack>
        </Group>
      </Card>

      <CustomerForm editing={editing ? c : null} onClose={() => setEditing(false)} />
      <MovementModal preset={movement} onClose={() => setMovement(null)} />
    </>
  );
}
