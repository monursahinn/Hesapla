import { useEffect } from "react";
import { Button, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, TextInput, Textarea } from "@mantine/core";
import { useForm } from "@mantine/form";
import { api } from "@/api/client";
import { CUSTOMER_TYPES, type Customer } from "@/api/types";
import { useApiMutation } from "@/lib/mutation";

/** Cari ekle / düzenle. editing: null kapalı, {} yeni, Customer düzenleme. */
export function CustomerForm({ editing, onClose, onSaved }: { editing: Partial<Customer> | null; onClose: () => void; onSaved?: (id: number) => void }) {
  const form = useForm({
    initialValues: {
      type: "MU", code: "", title: "", taxNumber: "", taxOffice: "", phone: "", email: "", address: "", city: "", district: "",
      paymentTermDays: 0 as number | string, openingBalance: 0 as number | string, notes: "", active: true,
    },
    validate: {
      title: (v) => (v.trim() ? null : "Unvan girin"),
      taxNumber: (v) => (!v.trim() || /^\d{10,11}$/.test(v.trim()) ? null : "Vergi no 10, T.C. kimlik no 11 hane olmalı"),
      email: (v) => (!v.trim() || /^\S+@\S+\.\S+$/.test(v.trim()) ? null : "Geçerli bir e-posta girin"),
      paymentTermDays: (v) => (Number(v) >= 0 && Number(v) <= 365 ? null : "0–365 gün"),
    },
  });

  useEffect(() => {
    if (!editing) return;
    form.setValues({
      type: editing.type ?? "MU", code: editing.code ?? "", title: editing.title ?? "", taxNumber: editing.taxNumber ?? "",
      taxOffice: editing.taxOffice ?? "", phone: editing.phone ?? "", email: editing.email ?? "", address: editing.address ?? "",
      city: editing.city ?? "", district: editing.district ?? "", paymentTermDays: editing.paymentTermDays ?? 0,
      openingBalance: editing.openingBalance ?? 0, notes: editing.notes ?? "", active: editing.active ?? true,
    });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);

  const isNew = !editing?.id;
  const save = useApiMutation<typeof form.values, { id: number }>({
    mutationFn: (v) => {
      const body = {
        ...v,
        code: v.code.trim() || null, title: v.title.trim(), taxNumber: v.taxNumber.trim() || null, taxOffice: v.taxOffice.trim() || null,
        phone: v.phone.trim() || null, email: v.email.trim() || null, address: v.address.trim() || null, city: v.city.trim() || null,
        district: v.district.trim() || null, notes: v.notes.trim() || null, paymentTermDays: Number(v.paymentTermDays) || 0,
        openingBalance: Number(v.openingBalance) || 0,
      };
      return isNew ? api.post<{ id: number }>("/api/customers", body) : api.put(`/api/customers/${editing!.id}`, body);
    },
    invalidate: [["customers"], ["dashboard"]],
    onSuccess: (data) => {
      onSaved?.(data?.id ?? editing!.id!);
      onClose();
    },
  });

  return (
    <Modal opened={!!editing} onClose={onClose} title={isNew ? "Yeni cari" : "Cariyi düzenle"} size="lg">
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <Select label="Tür" data={CUSTOMER_TYPES} {...form.getInputProps("type")} />
            <TextInput label="Kod" placeholder="Boş bırakılırsa otomatik (C00001)" maxLength={30} {...form.getInputProps("code")} />
          </SimpleGrid>
          <TextInput label="Unvan / ad soyad" maxLength={200} data-autofocus {...form.getInputProps("title")} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="Vergi no / T.C. kimlik no" maxLength={11} {...form.getInputProps("taxNumber")} />
            <TextInput label="Vergi dairesi" maxLength={80} {...form.getInputProps("taxOffice")} />
            <TextInput label="Telefon" maxLength={30} {...form.getInputProps("phone")} />
            <TextInput label="E-posta" maxLength={150} {...form.getInputProps("email")} />
          </SimpleGrid>
          <Textarea label="Adres" autosize minRows={1} maxLength={500} {...form.getInputProps("address")} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="İl" maxLength={60} {...form.getInputProps("city")} />
            <TextInput label="İlçe" maxLength={80} {...form.getInputProps("district")} />
            <NumberInput label="Vade (gün)" description="Faturada varsayılan vade" min={0} max={365} allowDecimal={false} {...form.getInputProps("paymentTermDays")} />
            <NumberInput
              label="Açılış bakiyesi"
              description="+ cari bize borçlu, − biz cariye borçluyuz"
              decimalScale={2}
              thousandSeparator="."
              decimalSeparator=","
              suffix=" ₺"
              hideControls
              {...form.getInputProps("openingBalance")}
            />
          </SimpleGrid>
          <Textarea label="Not" autosize minRows={1} maxLength={1000} {...form.getInputProps("notes")} />
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
