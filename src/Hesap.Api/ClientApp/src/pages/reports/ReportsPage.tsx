import { useState } from "react";
import { Alert, Anchor, Button, Card, Group, Select, SimpleGrid, Stack, Table, Tabs, Text } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useQuery } from "@tanstack/react-query";
import { IconFileSpreadsheet } from "@tabler/icons-react";
import { Link, useSearchParams } from "react-router";
import { api } from "@/api/client";
import { PageHeader } from "@/components/PageHeader";
import { PageError, PageLoader } from "@/components/PageLoader";
import { downloadFile, firstOfYearIso, todayIso } from "@/components/Shared";
import { parseTrDate } from "@/lib/dates";
import { money } from "@/lib/format";
import { notify } from "@/lib/notify";

interface ProfitLoss {
  sales: number;
  purchases: number;
  grossProfit: number;
  otherIncome: number;
  otherIncomeByCategory: { category: string; amount: number }[];
  expenses: number;
  expensesByCategory: { category: string; amount: number }[];
  netProfit: number;
}

interface VatRow { rate: number; baseAmount: number; vat: number }
interface Vat { outputVat: number; sales: VatRow[]; inputVat: number; purchases: VatRow[]; expenseVat: number; payable: number; carryForward: number }

interface Aging {
  customerId: number;
  customerTitle: string;
  notDue: number;
  d0To30: number;
  d31To60: number;
  d61To90: number;
  over90: number;
  total: number;
  balance: number;
}

function Line({ label, value, strong, indent, color }: { label: string; value: number; strong?: boolean; indent?: boolean; color?: string }) {
  return (
    <Group justify="space-between" pl={indent ? "md" : 0}>
      <Text size="sm" fw={strong ? 700 : undefined} c={indent ? "dimmed" : undefined}>{label}</Text>
      <Text size="sm" fw={strong ? 700 : 500} c={color} className="num">{money(value)}</Text>
    </Group>
  );
}

function ProfitLossTab() {
  const [from, setFrom] = useState<string | null>(firstOfYearIso());
  const [to, setTo] = useState<string | null>(todayIso());
  const q = useQuery({ queryKey: ["reports", "pl", from, to], queryFn: () => api.get<ProfitLoss>("/api/reports/profit-loss", { from, to }), placeholderData: (p) => p });

  return (
    <Stack>
      <Group gap="sm">
        <DateInput label="Başlangıç" w={150} valueFormat="DD.MM.YYYY" dateParser={parseTrDate} value={from} onChange={setFrom} />
        <DateInput label="Bitiş" w={150} valueFormat="DD.MM.YYYY" dateParser={parseTrDate} value={to} onChange={setTo} />
      </Group>
      {q.isError && <PageError error={q.error} />}
      {q.data && (
        <SimpleGrid cols={{ base: 1, md: 2 }}>
          <Card>
            <Stack gap={8}>
              <Line label="Satışlar (KDV hariç)" value={q.data.sales} />
              <Line label="Alışlar (KDV hariç)" value={-q.data.purchases} />
              <Line label="Brüt kâr" value={q.data.grossProfit} strong />
              <Line label="Diğer gelirler" value={q.data.otherIncome} />
              {q.data.otherIncomeByCategory.map((c) => <Line key={c.category} label={c.category} value={c.amount} indent />)}
              <Line label="Giderler (KDV hariç)" value={-q.data.expenses} />
              {q.data.expensesByCategory.map((c) => <Line key={c.category} label={c.category} value={-c.amount} indent />)}
              <Line label={q.data.netProfit >= 0 ? "Net kâr" : "Net zarar"} value={q.data.netProfit} strong color={q.data.netProfit >= 0 ? "teal" : "red"} />
            </Stack>
          </Card>
          <Alert variant="light" color="gray" title="Bu rapor hakkında">
            Fatura ve kasa-banka kayıtlarından hazırlanan yönetim raporudur. Alışlar dönem gideri sayılır (stok maliyeti ayrıca
            hesaplanmaz). Resmî bilanço ve gelir tablosu için mali müşavirinizin defter kayıtları esastır.
          </Alert>
        </SimpleGrid>
      )}
    </Stack>
  );
}

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

function VatTab() {
  const now = new Date();
  const [year, setYear] = useState(String(now.getFullYear()));
  const [month, setMonth] = useState(String(now.getMonth() + 1));
  const q = useQuery({ queryKey: ["reports", "vat", year, month], queryFn: () => api.get<Vat>("/api/reports/vat", { year, month }), placeholderData: (p) => p });
  const years = Array.from({ length: 6 }, (_, i) => String(now.getFullYear() - i));

  const RateTable = ({ rows }: { rows: VatRow[] }) => (
    <Table>
      <Table.Thead>
        <Table.Tr><Table.Th>Oran</Table.Th><Table.Th ta="right">Matrah</Table.Th><Table.Th ta="right">KDV</Table.Th></Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {rows.length === 0 ? (
          <Table.Tr><Table.Td colSpan={3}><Text size="sm" c="dimmed">Kayıt yok</Text></Table.Td></Table.Tr>
        ) : rows.map((r) => (
          <Table.Tr key={r.rate}><Table.Td>%{r.rate}</Table.Td><Table.Td ta="right">{money(r.baseAmount)}</Table.Td><Table.Td ta="right">{money(r.vat)}</Table.Td></Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );

  return (
    <Stack>
      <Group gap="sm">
        <Select label="Yıl" w={110} data={years} value={year} onChange={(v) => v && setYear(v)} />
        <Select label="Ay" w={150} data={MONTHS.map((m, i) => ({ value: String(i + 1), label: m }))} value={month} onChange={(v) => v && setMonth(v)} />
      </Group>
      {q.data && (
        <SimpleGrid cols={{ base: 1, md: 3 }}>
          <Card><Text fw={600} mb="xs">Hesaplanan KDV (satışlar)</Text><RateTable rows={q.data.sales} /></Card>
          <Card><Text fw={600} mb="xs">İndirilecek KDV (alışlar)</Text><RateTable rows={q.data.purchases} /></Card>
          <Card>
            <Stack gap={8}>
              <Line label="Hesaplanan KDV" value={q.data.outputVat} />
              <Line label="İndirilecek KDV (alış)" value={-q.data.inputVat} />
              <Line label="İndirilecek KDV (gider)" value={-q.data.expenseVat} />
              {q.data.payable > 0 ? <Line label="Ödenecek KDV" value={q.data.payable} strong color="red" /> : <Line label="Devreden KDV" value={q.data.carryForward} strong color="teal" />}
              <Text size="xs" c="dimmed">Önceki aydan devreden KDV dahil değildir; beyanname için mali müşavirinize danışın.</Text>
            </Stack>
          </Card>
        </SimpleGrid>
      )}
    </Stack>
  );
}

function AgingTab() {
  const q = useQuery({ queryKey: ["reports", "aging"], queryFn: () => api.get<Aging[]>("/api/reports/aging") });
  if (q.isPending) return <PageLoader stats={0} />;
  if (q.isError) return <PageError error={q.error} />;
  const rows = q.data!;
  const sum = (k: keyof Aging) => rows.reduce((s, r) => s + (r[k] as number), 0);

  return (
    <Card>
      <Group justify="space-between" mb="sm">
        <Text fw={600}>Açık satış faturalarının vadeye göre dağılımı</Text>
        <Button size="xs" variant="default" leftSection={<IconFileSpreadsheet size={14} />} onClick={() => downloadFile("/api/export/aging").catch((e) => notify.error(e.message))}>Excel</Button>
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Cari</Table.Th><Table.Th ta="right">Vadesi gelmemiş</Table.Th><Table.Th ta="right">0–30</Table.Th><Table.Th ta="right">31–60</Table.Th>
              <Table.Th ta="right">61–90</Table.Th><Table.Th ta="right">90+</Table.Th><Table.Th ta="right">Toplam</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.length === 0 ? (
              <Table.Tr><Table.Td colSpan={7}><Text size="sm" c="dimmed">Açık alacak yok.</Text></Table.Td></Table.Tr>
            ) : rows.map((r) => (
              <Table.Tr key={r.customerId}>
                <Table.Td><Anchor component={Link} to={`/customers/${r.customerId}`} size="sm">{r.customerTitle}</Anchor></Table.Td>
                <Table.Td ta="right">{r.notDue ? money(r.notDue) : "—"}</Table.Td>
                <Table.Td ta="right">{r.d0To30 ? money(r.d0To30) : "—"}</Table.Td>
                <Table.Td ta="right">{r.d31To60 ? money(r.d31To60) : "—"}</Table.Td>
                <Table.Td ta="right">{r.d61To90 ? money(r.d61To90) : "—"}</Table.Td>
                <Table.Td ta="right" c={r.over90 ? "red" : undefined}>{r.over90 ? money(r.over90) : "—"}</Table.Td>
                <Table.Td ta="right" fw={600}>{money(r.total)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
          {rows.length > 0 && (
            <Table.Tfoot>
              <Table.Tr>
                <Table.Th>Toplam</Table.Th><Table.Th ta="right">{money(sum("notDue"))}</Table.Th><Table.Th ta="right">{money(sum("d0To30"))}</Table.Th>
                <Table.Th ta="right">{money(sum("d31To60"))}</Table.Th><Table.Th ta="right">{money(sum("d61To90"))}</Table.Th><Table.Th ta="right">{money(sum("over90"))}</Table.Th>
                <Table.Th ta="right">{money(sum("total"))}</Table.Th>
              </Table.Tr>
            </Table.Tfoot>
          )}
        </Table>
      </Table.ScrollContainer>
    </Card>
  );
}

export default function ReportsPage() {
  const [params, setParams] = useSearchParams();
  const tab = params.get("tab") ?? "pl";
  return (
    <>
      <PageHeader title="Raporlar" description="Kâr/zarar, KDV özeti ve alacak yaşlandırma" />
      <Tabs value={tab} onChange={(v) => setParams(v && v !== "pl" ? { tab: v } : {}, { replace: true })} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="pl">Kâr / zarar</Tabs.Tab>
          <Tabs.Tab value="vat">KDV özeti</Tabs.Tab>
          <Tabs.Tab value="aging">Alacak yaşlandırma</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="pl"><ProfitLossTab /></Tabs.Panel>
        <Tabs.Panel value="vat"><VatTab /></Tabs.Panel>
        <Tabs.Panel value="aging"><AgingTab /></Tabs.Panel>
      </Tabs>
    </>
  );
}
