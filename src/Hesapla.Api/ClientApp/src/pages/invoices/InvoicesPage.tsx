import { useState } from "react";
import { Button, Card, Group, SegmentedControl, Select, Text, TextInput } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { IconFilePlus, IconFileSpreadsheet, IconSearch } from "@tabler/icons-react";
import { useNavigate, useSearchParams } from "react-router";
import { api } from "@/api/client";
import type { InvoiceListResult } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { downloadFile, Pager } from "@/components/Shared";
import { StatusBadge } from "@/components/StatusBadge";
import { parseTrDate } from "@/lib/dates";
import { date, money } from "@/lib/format";
import { notify } from "@/lib/notify";

const STATUSES = [
  { value: "", label: "Tüm durumlar" },
  { value: "open", label: "Açık (kalanı olan)" },
  { value: "overdue", label: "Vadesi geçmiş" },
  { value: "paid", label: "Ödenmiş" },
  { value: "cancelled", label: "İptal" },
];

export default function InvoicesPage() {
  const { canWrite } = useAuth();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const type = params.get("type") ?? "";
  const [status, setStatus] = useState<string | null>("");
  const [from, setFrom] = useState<string | null>(null);
  const [to, setTo] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);

  const q = useQuery({
    queryKey: ["invoices", "list", type, status, from, to, debounced, page],
    queryFn: () => api.get<InvoiceListResult>("/api/invoices", { type, status, from, to, q: debounced, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });
  const d = q.data;

  return (
    <>
      <PageHeader
        title="Faturalar"
        description="Satış ve alış faturaları, tahsilat durumları"
        actions={
          <>
            <Button variant="default" leftSection={<IconFileSpreadsheet size={16} />}
              onClick={() => downloadFile(`/api/export/invoices?type=${type}&from=${from ?? ""}&to=${to ?? ""}`).catch((e) => notify.error(e.message))}>
              Excel
            </Button>
            {canWrite && (
              <>
                <Button variant="default" leftSection={<IconFilePlus size={16} />} onClick={() => navigate("/invoices/new?type=AL")}>
                  Alış faturası
                </Button>
                <Button leftSection={<IconFilePlus size={16} />} onClick={() => navigate("/invoices/new?type=SA")}>
                  Satış faturası
                </Button>
              </>
            )}
          </>
        }
      />
      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}

      <Card>
        <DataTable
          toolbar={
            <Group gap="sm" wrap="wrap" w="100%">
              <SegmentedControl
                data={[{ value: "", label: "Tümü" }, { value: "SA", label: "Satış" }, { value: "AL", label: "Alış" }]}
                value={type}
                onChange={(v) => { setParams(v ? { type: v } : {}, { replace: true }); setPage(1); }}
              />
              <TextInput placeholder="Belge no, cari veya açıklama" leftSection={<IconSearch size={16} />} value={search}
                onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} style={{ flex: 1, minWidth: 200 }} />
              <Select w={190} data={STATUSES} value={status} onChange={(v) => { setStatus(v); setPage(1); }} aria-label="Durum" />
              <DateInput w={130} placeholder="Başlangıç" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} clearable value={from} onChange={(v) => { setFrom(v); setPage(1); }} />
              <DateInput w={130} placeholder="Bitiş" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} clearable value={to} onChange={(v) => { setTo(v); setPage(1); }} />
            </Group>
          }
          rows={d?.page.items}
          loading={q.isFetching}
          rowKey={(i) => i.id}
          onRowClick={(i) => navigate(`/invoices/${i.id}`)}
          emptyText="Fatura bulunamadı."
          columns={[
            { key: "date", header: "Tarih", render: (i) => date(i.date), width: 100 },
            { key: "no", header: "Belge no", render: (i) => <Text size="sm" ff="monospace">{i.documentNo}</Text>, hideOnMobile: true },
            { key: "type", header: "Tür", render: (i) => (i.type === "SA" ? "Satış" : "Alış"), hideOnMobile: true },
            { key: "customer", header: "Cari", render: (i) => i.customerTitle },
            { key: "due", header: "Vade", render: (i) => date(i.dueDate), hideOnMobile: true },
            { key: "total", header: "Toplam", align: "right", render: (i) => money(i.total) },
            { key: "remaining", header: "Kalan", align: "right", render: (i) => (i.remaining > 0 ? money(i.remaining) : "—") },
            { key: "status", header: "Durum", render: (i) => <StatusBadge code={i.status} text={i.statusLabel} /> },
          ]}
        />
        {d && (
          <Group justify="flex-end" gap="xl" mt="sm">
            <Text size="sm" c="dimmed">
              Süzülen toplam: <b>{money(d.sumTotal)}</b>
            </Text>
            <Text size="sm" c="dimmed">
              Kalan: <b>{money(d.sumRemaining)}</b>
            </Text>
          </Group>
        )}
        {d && <Pager page={page} total={d.page.total} pageSize={d.page.pageSize} onChange={setPage} />}
      </Card>
    </>
  );
}
