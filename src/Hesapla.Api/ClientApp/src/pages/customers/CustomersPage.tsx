import { useState } from "react";
import { Badge, Button, Card, Group, SegmentedControl, TextInput } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { IconFileSpreadsheet, IconPlus, IconSearch } from "@tabler/icons-react";
import { useNavigate, useSearchParams } from "react-router";
import { api } from "@/api/client";
import { CUSTOMER_TYPES, type Customer, type Paged } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { BalanceText, downloadFile, Pager } from "@/components/Shared";
import { notify } from "@/lib/notify";
import { CustomerForm } from "./CustomerForm";

export default function CustomersPage() {
  const { canWrite } = useAuth();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Partial<Customer> | null>(null);
  const type = params.get("type") ?? "";

  const q = useQuery({
    queryKey: ["customers", "list", debounced, type, page],
    queryFn: () => api.get<Paged<Customer>>("/api/customers", { q: debounced, type, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });

  return (
    <>
      <PageHeader
        title="Cariler"
        description="Müşteri ve tedarikçileriniz, bakiyeleri ve ekstreleri"
        actions={
          <>
            <Button variant="default" leftSection={<IconFileSpreadsheet size={16} />} onClick={() => downloadFile("/api/export/customers").catch((e) => notify.error(e.message))}>
              Excel
            </Button>
            {canWrite && (
              <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing({})}>
                Yeni cari
              </Button>
            )}
          </>
        }
      />
      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}

      <Card>
        <DataTable
          toolbar={
            <Group gap="sm" wrap="wrap" w="100%">
              <TextInput
                placeholder="Unvan, kod, vergi no veya telefon"
                leftSection={<IconSearch size={16} />}
                value={search}
                onChange={(e) => {
                  setSearch(e.currentTarget.value);
                  setPage(1);
                }}
                style={{ flex: 1, minWidth: 220 }}
              />
              <SegmentedControl
                data={[{ value: "", label: "Tümü" }, { value: "MU", label: "Müşteriler" }, { value: "TE", label: "Tedarikçiler" }]}
                value={type}
                onChange={(v) => {
                  setParams(v ? { type: v } : {}, { replace: true });
                  setPage(1);
                }}
              />
            </Group>
          }
          rows={q.data?.items}
          loading={q.isFetching}
          rowKey={(c) => c.id}
          onRowClick={(c) => navigate(`/customers/${c.id}`)}
          emptyText={debounced ? "Aramaya uyan cari yok." : "Henüz cari eklenmedi."}
          columns={[
            { key: "code", header: "Kod", render: (c) => c.code, width: 90, hideOnMobile: true },
            {
              key: "title",
              header: "Unvan",
              render: (c) => (
                <Group gap={6} wrap="nowrap">
                  <span>{c.title}</span>
                  {!c.active && (
                    <Badge color="gray" size="sm">
                      Pasif
                    </Badge>
                  )}
                </Group>
              ),
            },
            { key: "type", header: "Tür", render: (c) => CUSTOMER_TYPES.find((t) => t.value === c.type)?.label, hideOnMobile: true },
            { key: "phone", header: "Telefon", render: (c) => c.phone ?? "—", hideOnMobile: true },
            { key: "city", header: "İl", render: (c) => c.city ?? "—", hideOnMobile: true },
            { key: "balance", header: "Bakiye", align: "right", render: (c) => <BalanceText value={c.balance} /> },
          ]}
        />
        {q.data && <Pager page={page} total={q.data.total} pageSize={q.data.pageSize} onChange={setPage} />}
      </Card>

      <CustomerForm editing={editing} onClose={() => setEditing(null)} onSaved={(id) => navigate(`/customers/${id}`)} />
    </>
  );
}
