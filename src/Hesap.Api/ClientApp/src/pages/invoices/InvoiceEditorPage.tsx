import { Fragment, useEffect, useMemo } from "react";
import { ActionIcon, Alert, Button, Card, Grid, Group, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Textarea } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useForm } from "@mantine/form";
import { useQuery } from "@tanstack/react-query";
import { IconPlus, IconTrash } from "@tabler/icons-react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { api } from "@/api/client";
import { UNITS, VAT_RATES, type Invoice, type InvoiceType } from "@/api/types";
import { PageHeader } from "@/components/PageHeader";
import { PageError, PageLoader } from "@/components/PageLoader";
import { CustomerSelect, MoneyInput, ProductSelect, todayIso } from "@/components/Shared";
import { parseTrDate } from "@/lib/dates";
import { money } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";

interface LineForm {
  key: string;
  productId: number | null;
  description: string;
  quantity: number | string;
  unit: string;
  unitPrice: number | string;
  discountRate: number | string;
  vatRate: string;
}

const newLine = (): LineForm => ({ key: crypto.randomUUID(), productId: null, description: "", quantity: 1, unit: "Adet", unitPrice: "", discountRate: 0, vatRate: "20" });

/** Sunucuyla aynı yuvarlama: kuruşa, yarım yukarı. */
const r2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100;

function calc(l: LineForm) {
  const gross = r2((Number(l.quantity) || 0) * (Number(l.unitPrice) || 0));
  const discount = r2((gross * (Number(l.discountRate) || 0)) / 100);
  const amount = r2(gross - discount);
  const vat = r2((amount * Number(l.vatRate)) / 100);
  return { gross, discount, amount, vat };
}

/** Fatura oluştur / düzenle. Tutarlar burada önizleme içindir; kesin hesap sunucuda yapılır. */
export default function InvoiceEditorPage() {
  const editId = Number(useParams().id) || null;
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const existing = useQuery({ queryKey: ["invoices", editId], queryFn: () => api.get<Invoice>(`/api/invoices/${editId}`), enabled: !!editId });

  const form = useForm({
    initialValues: {
      type: (params.get("type") === "AL" ? "AL" : "SA") as InvoiceType,
      customerId: (Number(params.get("customerId")) || null) as number | null,
      date: todayIso() as string | null,
      dueDate: null as string | null,
      documentNo: "",
      description: "",
      lines: [newLine()],
    },
    validate: {
      customerId: (v) => (v ? null : "Cari seçin"),
      date: (v) => (v ? null : "Tarih seçin"),
      dueDate: (v, all) => (v && all.date && v < all.date ? "Vade faturadan önce olamaz" : null),
      lines: {
        description: (v, all, path) => {
          const i = Number(path.split(".")[1]);
          return v.trim() || all.lines[i].productId ? null : "Açıklama veya ürün";
        },
        quantity: (v) => (Number(v) > 0 ? null : "Miktar > 0"),
        unitPrice: (v) => (v !== "" && Number(v) >= 0 ? null : "Fiyat girin"),
      },
    },
  });

  useEffect(() => {
    const inv = existing.data;
    if (!inv) return;
    form.setValues({
      type: inv.type,
      customerId: inv.customerId,
      date: inv.date.slice(0, 10),
      dueDate: inv.dueDate.slice(0, 10),
      documentNo: inv.type === "AL" ? inv.documentNo : "",
      description: inv.description ?? "",
      lines: inv.lines.map((l) => ({
        key: crypto.randomUUID(), productId: l.productId ?? null, description: l.description, quantity: l.quantity, unit: l.unit,
        unitPrice: l.unitPrice, discountRate: l.discountRate, vatRate: String(l.vatRate),
      })),
    });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [existing.data]);

  const isSale = form.values.type === "SA";
  const totals = useMemo(() => {
    const rows = form.values.lines.map(calc);
    const byVat = new Map<string, number>();
    form.values.lines.forEach((l, i) => byVat.set(l.vatRate, r2((byVat.get(l.vatRate) ?? 0) + rows[i].vat)));
    const subtotal = r2(rows.reduce((s, r) => s + r.amount, 0));
    const discount = r2(rows.reduce((s, r) => s + r.discount, 0));
    const vat = r2(rows.reduce((s, r) => s + r.vat, 0));
    return { rows, subtotal, discount, vat, total: r2(subtotal + vat), byVat: [...byVat].sort((a, b) => Number(a[0]) - Number(b[0])) };
  }, [form.values.lines]);

  const save = useApiMutation<typeof form.values, Invoice>({
    mutationFn: (v) => {
      const body = {
        type: v.type,
        customerId: v.customerId,
        date: v.date,
        dueDate: v.dueDate || null,
        documentNo: v.type === "AL" ? v.documentNo.trim() || null : null,
        description: v.description.trim() || null,
        lines: v.lines.map((l) => ({
          productId: l.productId, description: l.description.trim() || null, quantity: Number(l.quantity), unit: l.unit,
          unitPrice: Number(l.unitPrice), discountRate: Number(l.discountRate) || 0, vatRate: Number(l.vatRate),
        })),
      };
      return editId ? api.put<Invoice>(`/api/invoices/${editId}`, body) : api.post<Invoice>("/api/invoices", body);
    },
    invalidate: [["invoices"], ["customers"], ["products"], ["dashboard"], ["reports"]],
    onSuccess: (inv) => navigate(`/invoices/${inv?.id ?? editId}`, { replace: true }),
  });

  if (editId && existing.isPending) return <PageLoader />;
  if (editId && existing.isError) return <PageError error={existing.error} />;

  return (
    <>
      <PageHeader
        title={editId ? `${existing.data?.documentNo} düzenle` : isSale ? "Yeni satış faturası" : "Yeni alış faturası"}
        description={isSale ? "Müşteriye kesilen fatura; numara otomatik verilir." : "Tedarikçiden gelen fatura; tedarikçinin fatura numarasını girin."}
      />
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          <Card>
            <Grid>
              <Grid.Col span={{ base: 12, md: 6 }}>
                <CustomerSelect
                  label={isSale ? "Müşteri" : "Tedarikçi"}
                  type={isSale ? "MU" : "TE"}
                  value={form.values.customerId}
                  onChange={(id, c) => {
                    form.setFieldValue("customerId", id);
                    // Vade boşsa carinin vade günüyle önerilir.
                    if (c && form.values.date && !form.values.dueDate && c.paymentTermDays > 0) {
                      const d = new Date(form.values.date + "T00:00:00");
                      d.setDate(d.getDate() + c.paymentTermDays);
                      form.setFieldValue("dueDate", `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`);
                    }
                  }}
                  error={form.errors.customerId}
                />
              </Grid.Col>
              <Grid.Col span={{ base: 6, md: 3 }}>
                <DateInput label="Fatura tarihi" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} {...form.getInputProps("date")} />
              </Grid.Col>
              <Grid.Col span={{ base: 6, md: 3 }}>
                <DateInput label="Vade tarihi" placeholder="Carinin vadesi" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} clearable {...form.getInputProps("dueDate")} />
              </Grid.Col>
              {!isSale && (
                <Grid.Col span={{ base: 12, md: 6 }}>
                  <TextInput label="Tedarikçinin fatura no" placeholder="Boş bırakılırsa otomatik verilir" maxLength={30} {...form.getInputProps("documentNo")} />
                </Grid.Col>
              )}
              <Grid.Col span={{ base: 12, md: isSale ? 12 : 6 }}>
                <Textarea label="Açıklama" autosize minRows={1} maxLength={500} {...form.getInputProps("description")} />
              </Grid.Col>
            </Grid>
          </Card>

          <Card>
            <Text fw={600} mb="sm">
              Satırlar
            </Text>
            {typeof form.errors.lines === "string" && (
              <Alert color="red" mb="sm">
                {form.errors.lines}
              </Alert>
            )}
            <Table.ScrollContainer minWidth={980}>
              <Table verticalSpacing="xs" highlightOnHover={false}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th w={230}>Ürün / hizmet</Table.Th>
                    <Table.Th>Açıklama</Table.Th>
                    <Table.Th w={90}>Miktar</Table.Th>
                    <Table.Th w={100}>Birim</Table.Th>
                    <Table.Th w={130}>Birim fiyat</Table.Th>
                    <Table.Th w={80}>İsk. %</Table.Th>
                    <Table.Th w={90}>KDV</Table.Th>
                    <Table.Th w={120} ta="right">Tutar</Table.Th>
                    <Table.Th w={40} />
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {form.values.lines.map((l, i) => (
                    <Table.Tr key={l.key}>
                      <Table.Td>
                        <ProductSelect
                          size="xs"
                          value={l.productId}
                          placeholder="Seçin (isteğe bağlı)"
                          onChange={(p) => {
                            form.setFieldValue(`lines.${i}.productId`, p?.id ?? null);
                            if (p) {
                              form.setFieldValue(`lines.${i}.unit`, p.unit);
                              form.setFieldValue(`lines.${i}.vatRate`, String(p.vatRate));
                              form.setFieldValue(`lines.${i}.unitPrice`, isSale ? p.salePrice : p.purchasePrice);
                              if (!l.description) form.setFieldValue(`lines.${i}.description`, p.name);
                            }
                          }}
                        />
                      </Table.Td>
                      <Table.Td>
                        <TextInput size="xs" maxLength={300} {...form.getInputProps(`lines.${i}.description`)} />
                      </Table.Td>
                      <Table.Td>
                        <NumberInput size="xs" min={0} decimalScale={3} decimalSeparator="," thousandSeparator="." hideControls {...form.getInputProps(`lines.${i}.quantity`)} />
                      </Table.Td>
                      <Table.Td>
                        <Select size="xs" data={UNITS.includes(l.unit) ? UNITS : [l.unit, ...UNITS]} searchable {...form.getInputProps(`lines.${i}.unit`)} />
                      </Table.Td>
                      <Table.Td>
                        <MoneyInput size="xs" {...form.getInputProps(`lines.${i}.unitPrice`)} />
                      </Table.Td>
                      <Table.Td>
                        <NumberInput size="xs" min={0} max={100} decimalScale={2} decimalSeparator="," hideControls {...form.getInputProps(`lines.${i}.discountRate`)} />
                      </Table.Td>
                      <Table.Td>
                        <Select size="xs" data={VAT_RATES} {...form.getInputProps(`lines.${i}.vatRate`)} />
                      </Table.Td>
                      <Table.Td ta="right">
                        <Text size="sm" className="num">
                          {money(totals.rows[i].amount)}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <ActionIcon variant="subtle" color="red" aria-label="Satırı sil" disabled={form.values.lines.length === 1} onClick={() => form.removeListItem("lines", i)}>
                          <IconTrash size={16} />
                        </ActionIcon>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
            <Group justify="space-between" mt="md" align="flex-start" wrap="wrap">
              <Button variant="light" leftSection={<IconPlus size={16} />} disabled={form.values.lines.length >= 200} onClick={() => form.insertListItem("lines", newLine())}>
                Satır ekle
              </Button>
              <SimpleGrid cols={2} spacing={6} verticalSpacing={4} miw={280}>
                {totals.discount > 0 && (
                  <>
                    <Text size="sm" c="dimmed">Toplam iskonto</Text>
                    <Text size="sm" ta="right" className="num">{money(totals.discount)}</Text>
                  </>
                )}
                <Text size="sm" c="dimmed">Ara toplam</Text>
                <Text size="sm" ta="right" className="num">{money(totals.subtotal)}</Text>
                {totals.byVat.map(([rate, v]) => (
                  <Fragment key={rate}>
                    <Text size="sm" c="dimmed">KDV %{rate}</Text>
                    <Text size="sm" ta="right" className="num">{money(v)}</Text>
                  </Fragment>
                ))}
                <Text fw={700}>Genel toplam</Text>
                <Text fw={700} ta="right" className="num">{money(totals.total)}</Text>
              </SimpleGrid>
            </Group>
          </Card>

          <Group justify="flex-end">
            <Button variant="default" onClick={() => navigate(-1)}>
              Vazgeç
            </Button>
            <Button type="submit" loading={save.isPending}>
              {editId ? "Değişiklikleri kaydet" : "Faturayı kaydet"}
            </Button>
          </Group>
        </Stack>
      </form>
    </>
  );
}
