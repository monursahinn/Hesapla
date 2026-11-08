import { useEffect, useState } from "react";
import { Badge, Button, Card, Group, Modal, NumberInput, SegmentedControl, Select, SimpleGrid, Stack, Switch, TextInput } from "@mantine/core";
import { useForm } from "@mantine/form";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { IconPlus, IconSearch } from "@tabler/icons-react";
import { api } from "@/api/client";
import { UNITS, VAT_RATES, type Paged, type Product } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { MoneyInput, Pager } from "@/components/Shared";
import { money } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";

function ProductForm({ editing, onClose }: { editing: Partial<Product> | null; onClose: () => void }) {
  const form = useForm({
    initialValues: {
      type: "UR", code: "", name: "", unit: "Adet", vatRate: "20", salePrice: 0 as number | string, purchasePrice: 0 as number | string,
      trackStock: false, openingStock: 0 as number | string, criticalStock: "" as number | string, active: true,
    },
    validate: { name: (v) => (v.trim() ? null : "Ad girin"), unit: (v) => (v ? null : "Birim seçin") },
  });

  useEffect(() => {
    if (!editing) return;
    form.setValues({
      type: editing.type ?? "UR", code: editing.code ?? "", name: editing.name ?? "", unit: editing.unit ?? "Adet",
      vatRate: String(editing.vatRate ?? 20), salePrice: editing.salePrice ?? 0, purchasePrice: editing.purchasePrice ?? 0,
      trackStock: editing.trackStock ?? false, openingStock: editing.openingStock ?? 0, criticalStock: editing.criticalStock ?? "", active: editing.active ?? true,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);

  const isNew = !editing?.id;
  const service = form.values.type === "HZ";
  const save = useApiMutation<typeof form.values>({
    mutationFn: (v) => {
      const body = {
        ...v, code: v.code.trim() || null, name: v.name.trim(), vatRate: Number(v.vatRate), salePrice: Number(v.salePrice) || 0,
        purchasePrice: Number(v.purchasePrice) || 0, trackStock: !service && v.trackStock, openingStock: Number(v.openingStock) || 0,
        criticalStock: v.criticalStock === "" ? null : Number(v.criticalStock),
      };
      return isNew ? api.post("/api/products", body) : api.put(`/api/products/${editing!.id}`, body);
    },
    invalidate: [["products"], ["dashboard"]],
    onSuccess: onClose,
  });

  return (
    <Modal opened={!!editing} onClose={onClose} title={isNew ? "Yeni ürün / hizmet" : "Ürün / hizmeti düzenle"} size="lg">
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          <SegmentedControl data={[{ value: "UR", label: "Ürün" }, { value: "HZ", label: "Hizmet" }]} {...form.getInputProps("type")} />
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <TextInput label="Kod" placeholder="Otomatik (U00001)" maxLength={40} {...form.getInputProps("code")} />
            <TextInput label="Ad" maxLength={200} style={{ gridColumn: "span 2" }} {...form.getInputProps("name")} />
            <Select label="Birim" data={UNITS} searchable {...form.getInputProps("unit")} />
            <Select label="KDV" data={VAT_RATES} {...form.getInputProps("vatRate")} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <MoneyInput label="Satış fiyatı (KDV hariç)" {...form.getInputProps("salePrice")} />
            <MoneyInput label="Alış fiyatı (KDV hariç)" {...form.getInputProps("purchasePrice")} />
          </SimpleGrid>
          {!service && (
            <>
              <Switch label="Stok takibi yap" description="Alış faturaları stoğu artırır, satış faturaları azaltır" {...form.getInputProps("trackStock", { type: "checkbox" })} />
              {form.values.trackStock && (
                <SimpleGrid cols={{ base: 1, sm: 2 }}>
                  <NumberInput label="Açılış stoğu" decimalScale={3} decimalSeparator="," thousandSeparator="." {...form.getInputProps("openingStock")} />
                  <NumberInput label="Kritik stok seviyesi" description="Bu seviyenin altında panoda uyarı" min={0} decimalScale={3} decimalSeparator="," thousandSeparator="." {...form.getInputProps("criticalStock")} />
                </SimpleGrid>
              )}
            </>
          )}
          {!isNew && <Switch label="Aktif" {...form.getInputProps("active", { type: "checkbox" })} />}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>
              Vazgeç
            </Button>
            <Button type="submit" loading={save.isPending}>
              Kaydet
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

export default function ProductsPage() {
  const { canWrite } = useAuth();
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search.trim(), 300);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Partial<Product> | null>(null);

  const q = useQuery({
    queryKey: ["products", "list", debounced, page],
    queryFn: () => api.get<Paged<Product>>("/api/products", { q: debounced, page, pageSize: 25 }),
    placeholderData: (p) => p,
  });

  return (
    <>
      <PageHeader
        title="Ürün ve hizmetler"
        description="Fatura satırlarında kullanılan kartlar, fiyatlar ve stok"
        actions={canWrite && <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing({})}>Yeni ürün / hizmet</Button>}
      />
      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}
      <Card>
        <DataTable
          toolbar={
            <TextInput placeholder="Ad veya kod" leftSection={<IconSearch size={16} />} value={search}
              onChange={(e) => { setSearch(e.currentTarget.value); setPage(1); }} style={{ flex: 1, minWidth: 220 }} />
          }
          rows={q.data?.items}
          loading={q.isFetching}
          rowKey={(p) => p.id}
          onRowClick={canWrite ? (p) => setEditing(p) : undefined}
          emptyText={debounced ? "Aramaya uyan kayıt yok." : "Henüz ürün veya hizmet eklenmedi."}
          columns={[
            { key: "code", header: "Kod", render: (p) => p.code, width: 90, hideOnMobile: true },
            {
              key: "name",
              header: "Ad",
              render: (p) => (
                <Group gap={6} wrap="nowrap">
                  <span>{p.name}</span>
                  {p.type === "HZ" && <Badge size="sm" color="grape">Hizmet</Badge>}
                  {!p.active && <Badge size="sm" color="gray">Pasif</Badge>}
                </Group>
              ),
            },
            { key: "vat", header: "KDV", render: (p) => `%${p.vatRate}`, hideOnMobile: true },
            { key: "sale", header: "Satış fiyatı", align: "right", render: (p) => money(p.salePrice) },
            { key: "purchase", header: "Alış fiyatı", align: "right", render: (p) => money(p.purchasePrice), hideOnMobile: true },
            {
              key: "stock",
              header: "Stok",
              align: "right",
              render: (p) =>
                p.trackStock ? (
                  <Badge color={p.lowStock ? "red" : "gray"} variant={p.lowStock ? "filled" : "light"}>
                    {(p.stock ?? 0).toLocaleString("tr-TR")} {p.unit}
                  </Badge>
                ) : (
                  "—"
                ),
            },
          ]}
        />
        {q.data && <Pager page={page} total={q.data.total} pageSize={q.data.pageSize} onChange={setPage} />}
      </Card>
      <ProductForm editing={editing} onClose={() => setEditing(null)} />
    </>
  );
}
