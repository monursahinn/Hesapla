import { useEffect } from "react";
import { Alert, Button, Group, Modal, SegmentedControl, Select, SimpleGrid, Stack, TextInput, Textarea } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { useForm } from "@mantine/form";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { VAT_RATES, type Account, type Category, type InvoiceListResult, type Movement } from "@/api/types";
import { CustomerSelect, MoneyInput, todayIso } from "@/components/Shared";
import { parseTrDate } from "@/lib/dates";
import { date, money } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";

export type MovementType = Movement["type"];

export interface MovementPreset {
  type: MovementType;
  customerId?: number;
  invoiceId?: number;
  amount?: number;
  /** Tür değiştirilemesin (ör. faturadan açılan tahsilat). */
  lockType?: boolean;
}

const TYPES = [
  { value: "TA", label: "Tahsilat" },
  { value: "OD", label: "Ödeme" },
  { value: "GI", label: "Gider" },
  { value: "GE", label: "Gelir" },
  { value: "VI", label: "Virman" },
];

const TITLES: Record<MovementType, string> = {
  TA: "Tahsilat (cariden para girişi)",
  OD: "Ödeme (cariye para çıkışı)",
  GI: "Gider kaydı",
  GE: "Gelir kaydı",
  VI: "Virman (hesaplar arası transfer)",
};

export function MovementModal({ preset, onClose }: { preset: MovementPreset | null; onClose: () => void }) {
  const accounts = useQuery({ queryKey: ["accounts"], queryFn: () => api.get<Account[]>("/api/accounts"), enabled: !!preset });
  const categories = useQuery({ queryKey: ["categories"], queryFn: () => api.get<Category[]>("/api/categories"), enabled: !!preset });

  const form = useForm({
    initialValues: {
      type: "TA" as MovementType,
      date: todayIso() as string | null,
      accountId: null as string | null,
      targetAccountId: null as string | null,
      customerId: null as number | null,
      invoiceId: null as string | null,
      categoryId: null as string | null,
      vatRate: "20",
      amount: "" as number | string,
      documentNo: "",
      description: "",
    },
    validate: {
      date: (v) => (v ? null : "Tarih seçin"),
      accountId: (v) => (v ? null : "Hesap seçin"),
      amount: (v) => (Number(v) > 0 ? null : "Tutar girin"),
      customerId: (v, all) => ((all.type === "TA" || all.type === "OD") && !v ? "Cari seçin" : null),
      categoryId: (v, all) => ((all.type === "GI" || all.type === "GE") && !v ? "Kategori seçin" : null),
      targetAccountId: (v, all) => (all.type !== "VI" ? null : !v ? "Karşı hesap seçin" : v === all.accountId ? "Farklı bir hesap seçin" : null),
    },
  });

  // Her açılışta ön ayarla başlar; ilk aktif hesap (kasa) seçili gelir.
  useEffect(() => {
    if (!preset) return;
    form.setValues({
      type: preset.type,
      date: todayIso(),
      accountId: null,
      targetAccountId: null,
      customerId: preset.customerId ?? null,
      invoiceId: preset.invoiceId ? String(preset.invoiceId) : null,
      categoryId: null,
      vatRate: "20",
      amount: preset.amount ?? "",
      documentNo: "",
      description: "",
    });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preset]);

  const activeAccounts = (accounts.data ?? []).filter((a) => a.active);
  useEffect(() => {
    if (preset && !form.values.accountId && activeAccounts.length) form.setFieldValue("accountId", String(activeAccounts[0].id));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preset, accounts.data]);

  const t = form.values.type;
  const isCari = t === "TA" || t === "OD";
  const isCat = t === "GI" || t === "GE";

  // Carinin bu türe uygun açık faturaları (tahsilat → satış, ödeme → alış).
  const openInvoices = useQuery({
    queryKey: ["invoices", "open", form.values.customerId, t],
    queryFn: () => api.get<InvoiceListResult>("/api/invoices", { customerId: form.values.customerId, type: t === "TA" ? "SA" : "AL", status: "open", pageSize: 50 }),
    enabled: !!preset && isCari && !!form.values.customerId,
  });

  const save = useApiMutation<typeof form.values>({
    mutationFn: (v) =>
      api.post("/api/movements", {
        type: v.type,
        date: v.date,
        accountId: Number(v.accountId),
        targetAccountId: v.type === "VI" ? Number(v.targetAccountId) : null,
        customerId: isCari ? v.customerId : null,
        invoiceId: isCari && v.invoiceId ? Number(v.invoiceId) : null,
        categoryId: isCat ? Number(v.categoryId) : null,
        amount: Number(v.amount),
        vatRate: isCat ? Number(v.vatRate) : 0,
        documentNo: v.documentNo.trim() || null,
        description: v.description.trim() || null,
      }),
    invalidate: [["accounts"], ["movements"], ["invoices"], ["customers"], ["dashboard"], ["reports"]],
    onSuccess: onClose,
  });

  const accountData = activeAccounts.map((a) => ({ value: String(a.id), label: `${a.name} — ${money(a.balance)}` }));

  return (
    <Modal opened={!!preset} onClose={onClose} title={TITLES[t]} size="lg">
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          {!preset?.lockType && (
            <SegmentedControl
              fullWidth
              data={TYPES}
              value={t}
              onChange={(v) => {
                form.setFieldValue("type", v as MovementType);
                form.setFieldValue("invoiceId", null);
                form.setFieldValue("customerId", null);
                form.setFieldValue("categoryId", null);
              }}
            />
          )}
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <DateInput label="Tarih" valueFormat="DD.MM.YYYY" dateParser={parseTrDate} maxDate={new Date()} {...form.getInputProps("date")} />
            <MoneyInput label="Tutar" {...form.getInputProps("amount")} />
          </SimpleGrid>
          <SimpleGrid cols={{ base: 1, sm: t === "VI" ? 2 : 1 }}>
            <Select label={t === "VI" ? "Çıkış hesabı" : "Kasa / banka"} data={accountData} {...form.getInputProps("accountId")} />
            {t === "VI" && <Select label="Giriş hesabı" data={accountData} {...form.getInputProps("targetAccountId")} />}
          </SimpleGrid>

          {isCari && (
            <>
              <CustomerSelect
                label="Cari"
                type={t === "TA" ? "MU" : "TE"}
                value={form.values.customerId}
                onChange={(id) => {
                  form.setFieldValue("customerId", id);
                  form.setFieldValue("invoiceId", null);
                }}
                error={form.errors.customerId}
              />
              {form.values.customerId && (
                <Select
                  label="Faturaya bağla (isteğe bağlı)"
                  description="Bağlanırsa fatura ödendi olarak işaretlenir; bağlanmazsa cari hesaba mahsup edilir."
                  clearable
                  placeholder={openInvoices.data?.page.items.length ? "Fatura seçin" : "Açık fatura yok"}
                  data={(openInvoices.data?.page.items ?? []).map((i) => ({
                    value: String(i.id),
                    label: `${i.documentNo} • ${date(i.date)} • kalan ${money(i.remaining)}`,
                  }))}
                  {...form.getInputProps("invoiceId")}
                  onChange={(v) => {
                    form.setFieldValue("invoiceId", v);
                    const inv = openInvoices.data?.page.items.find((i) => String(i.id) === v);
                    if (inv && !form.values.amount) form.setFieldValue("amount", inv.remaining);
                  }}
                />
              )}
            </>
          )}

          {isCat && (
            <SimpleGrid cols={{ base: 1, sm: 2 }}>
              <Select
                label="Kategori"
                data={(categories.data ?? []).filter((c) => c.active && c.type === t).map((c) => ({ value: String(c.id), label: c.name }))}
                {...form.getInputProps("categoryId")}
              />
              <Select label="KDV oranı (tutar KDV dahil)" data={VAT_RATES} {...form.getInputProps("vatRate")} />
            </SimpleGrid>
          )}

          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="Belge no" placeholder="Dekont / makbuz no" maxLength={40} {...form.getInputProps("documentNo")} />
            <Textarea label="Açıklama" autosize minRows={1} maxLength={500} {...form.getInputProps("description")} />
          </SimpleGrid>

          {t === "OD" || t === "GI" ? (
            <Alert variant="light" color="gray">
              Kasa hesapları eksiye düşemez; banka hesabı eksi bakiyeye (kredili mevduat) izin verir.
            </Alert>
          ) : null}

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
