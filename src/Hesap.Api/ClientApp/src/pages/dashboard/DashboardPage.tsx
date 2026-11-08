import { useState } from "react";
import { Anchor, Badge, Button, Card, Grid, Group, SimpleGrid, Stack, Table, Text } from "@mantine/core";
import { AreaChart, BarChart } from "@mantine/charts";
import { useQuery } from "@tanstack/react-query";
import { IconArrowDownLeft, IconArrowUpRight, IconBuildingBank, IconCash, IconFilePlus, IconReceipt, IconScale, IconWallet } from "@tabler/icons-react";
import { Link, useNavigate } from "react-router";
import { api } from "@/api/client";
import type { Account, InvoiceListItem, Product } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { MovementModal, type MovementPreset } from "@/components/MovementModal";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { StatCard } from "@/components/StatCard";
import { date, money } from "@/lib/format";

interface Dashboard {
  cashTotal: number;
  bankTotal: number;
  receivables: number;
  payables: number;
  monthSales: number;
  monthPurchases: number;
  monthExpenses: number;
  monthCollections: number;
  overdueReceivables: number;
  overdueInvoices: InvoiceListItem[];
  upcomingPayables: InvoiceListItem[];
  last12Months: { month: string; income: number; expense: number }[];
  cashFlow30: { day: string; in: number; out: number }[];
  lowStock: Product[];
  accounts: Account[];
}

const MONTHS = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

function InvoiceMiniList({ title, rows, empty, kind }: { title: string; rows: InvoiceListItem[]; empty: string; kind: "overdue" | "upcoming" }) {
  return (
    <Card h="100%">
      <Text fw={600} mb="sm">
        {title}
      </Text>
      {rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {empty}
        </Text>
      ) : (
        <Table verticalSpacing={6} horizontalSpacing={0}>
          <Table.Tbody>
            {rows.map((r) => (
              <Table.Tr key={r.id}>
                <Table.Td>
                  <Anchor component={Link} to={`/invoices/${r.id}`} size="sm" fw={500}>
                    {r.customerTitle}
                  </Anchor>
                  <Text size="xs" c="dimmed">
                    {r.documentNo} • vade {date(r.dueDate)}
                  </Text>
                </Table.Td>
                <Table.Td ta="right">
                  <Text size="sm" fw={600} c={kind === "overdue" ? "red" : undefined} className="num">
                    {money(r.remaining)}
                  </Text>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Card>
  );
}

export default function DashboardPage() {
  const { me, canWrite } = useAuth();
  const navigate = useNavigate();
  const [movement, setMovement] = useState<MovementPreset | null>(null);
  const q = useQuery({ queryKey: ["dashboard"], queryFn: () => api.get<Dashboard>("/api/reports/dashboard"), refetchInterval: 120_000 });
  const d = q.data;
  const loading = q.isPending;

  const months = (d?.last12Months ?? []).map((m) => ({ ay: MONTHS[Number(m.month.slice(5)) - 1], Gelir: m.income, Gider: m.expense }));
  const flow = (d?.cashFlow30 ?? []).map((x) => ({ gün: date(x.day, "DD.MM"), Giriş: x.in, Çıkış: x.out }));

  return (
    <>
      <PageHeader
        title={`Merhaba, ${me?.fullName.split(" ")[0] ?? ""}`}
        description={me?.firm?.name}
        actions={
          canWrite && (
            <>
              <Button leftSection={<IconFilePlus size={16} />} onClick={() => navigate("/invoices/new?type=SA")}>
                Satış faturası
              </Button>
              <Button variant="default" leftSection={<IconArrowDownLeft size={16} />} onClick={() => setMovement({ type: "TA" })}>
                Tahsilat
              </Button>
              <Button variant="default" leftSection={<IconReceipt size={16} />} onClick={() => setMovement({ type: "GI" })}>
                Gider
              </Button>
            </>
          )
        }
      />

      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}

      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="md">
        <StatCard label="Kasa" value={money(d?.cashTotal)} icon={<IconCash size={22} />} loading={loading} to="/cash" />
        <StatCard label="Banka" value={money(d?.bankTotal)} icon={<IconBuildingBank size={22} />} loading={loading} to="/cash" />
        <StatCard
          label="Alacaklar"
          value={money(d?.receivables)}
          hint={d && d.overdueReceivables > 0 ? `Vadesi geçen: ${money(d.overdueReceivables)}` : "Vadesi geçen alacak yok"}
          icon={<IconArrowDownLeft size={22} />}
          color={d && d.overdueReceivables > 0 ? "red" : "teal"}
          loading={loading}
          to="/reports?tab=aging"
        />
        <StatCard label="Borçlar" value={money(d?.payables)} icon={<IconArrowUpRight size={22} />} color="orange" loading={loading} to="/customers?type=TE" />
      </SimpleGrid>

      <SimpleGrid cols={{ base: 1, xs: 2, lg: 4 }} mb="lg">
        <StatCard label="Bu ay satış" value={money(d?.monthSales)} icon={<IconWallet size={22} />} loading={loading} to="/invoices?type=SA" />
        <StatCard label="Bu ay tahsilat" value={money(d?.monthCollections)} icon={<IconArrowDownLeft size={22} />} color="teal" loading={loading} to="/cash?type=TA" />
        <StatCard label="Bu ay alış" value={money(d?.monthPurchases)} icon={<IconScale size={22} />} color="gray" loading={loading} to="/invoices?type=AL" />
        <StatCard label="Bu ay gider" value={money(d?.monthExpenses)} icon={<IconReceipt size={22} />} color="orange" loading={loading} to="/cash?type=GI" />
      </SimpleGrid>

      {d && (
        <>
          <Grid gap="md" mb="md">
            <Grid.Col span={{ base: 12, lg: 7 }}>
              <Card h="100%">
                <Text fw={600}>Gelir ve gider</Text>
                <Text size="xs" c="dimmed" mb="md">
                  Son 12 ay (faturalar ve kasa-banka gelir/giderleri, KDV dahil)
                </Text>
                <BarChart h={240} data={months} dataKey="ay" valueFormatter={(v) => money(v)} series={[{ name: "Gelir", color: "teal.6" }, { name: "Gider", color: "red.5" }]} withLegend gridAxis="y" />
              </Card>
            </Grid.Col>
            <Grid.Col span={{ base: 12, lg: 5 }}>
              <Card h="100%">
                <Text fw={600}>Nakit akışı</Text>
                <Text size="xs" c="dimmed" mb="md">
                  Son 30 gün, kasa ve banka (virmanlar hariç)
                </Text>
                <AreaChart h={240} data={flow} dataKey="gün" valueFormatter={(v) => money(v)} series={[{ name: "Giriş", color: "teal.6" }, { name: "Çıkış", color: "red.5" }]} curveType="monotone" withDots={false} withLegend gridAxis="y" />
              </Card>
            </Grid.Col>
          </Grid>

          <SimpleGrid cols={{ base: 1, md: 2, xl: 4 }} mb="md">
            <InvoiceMiniList title="Vadesi geçen alacaklar" rows={d.overdueInvoices} empty="Vadesi geçen alacak yok." kind="overdue" />
            <InvoiceMiniList title="Yaklaşan ödemeler (7 gün)" rows={d.upcomingPayables} empty="Yaklaşan ödeme yok." kind="upcoming" />
            <Card h="100%">
              <Text fw={600} mb="sm">
                Kasa ve banka hesapları
              </Text>
              <Stack gap={8}>
                {d.accounts.map((a) => (
                  <Group key={a.id} justify="space-between" wrap="nowrap">
                    <Text size="sm" truncate>
                      {a.name}
                    </Text>
                    <Text size="sm" fw={600} c={a.balance < 0 ? "red" : undefined} className="num">
                      {money(a.balance)}
                    </Text>
                  </Group>
                ))}
              </Stack>
            </Card>
            <Card h="100%">
              <Text fw={600} mb="sm">
                Kritik stok
              </Text>
              {d.lowStock.length === 0 ? (
                <Text size="sm" c="dimmed">
                  Kritik seviyede ürün yok.
                </Text>
              ) : (
                <Stack gap={8}>
                  {d.lowStock.map((p) => (
                    <Group key={p.id} justify="space-between" wrap="nowrap">
                      <Text size="sm" truncate>
                        {p.name}
                      </Text>
                      <Badge color="red" variant="light">
                        {p.stock?.toLocaleString("tr-TR")} {p.unit}
                      </Badge>
                    </Group>
                  ))}
                </Stack>
              )}
            </Card>
          </SimpleGrid>
        </>
      )}

      <MovementModal preset={movement} onClose={() => setMovement(null)} />
    </>
  );
}
