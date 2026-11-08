import { useState } from "react";
import { Group, NumberInput, Pagination, Select, Text, type NumberInputProps, type SelectProps } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { Customer, Paged, Product } from "@/api/types";
import { money } from "@/lib/format";

/** Türk biçimiyle para girişi: 1.234,56 ₺ (binlik nokta, ondalık virgül; kuruştan küçük girilemez). */
export function MoneyInput(props: NumberInputProps) {
  return <NumberInput decimalScale={2} thousandSeparator="." decimalSeparator="," suffix=" ₺" hideControls allowNegative={false} max={999_999_999_999} {...props} />;
}

/** Sayfalama: toplam kayıt ve sayfa düğmeleri. */
export function Pager({ page, total, pageSize, onChange }: { page: number; total: number; pageSize: number; onChange: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <Group justify="space-between" mt="md" wrap="wrap" gap="xs">
      <Text size="sm" c="dimmed">
        Toplam {total.toLocaleString("tr-TR")} kayıt
      </Text>
      {pages > 1 && <Pagination size="sm" value={page} total={pages} onChange={onChange} />}
    </Group>
  );
}

/** Sunucuda arayan cari seçici (yazdıkça süzülür). type: MU satış için, TE alış için. */
export function CustomerSelect({ type, value, onChange, ...rest }: { type?: "MU" | "TE"; value: number | null; onChange: (id: number | null, c?: Customer) => void } & Omit<SelectProps, "value" | "onChange" | "data">) {
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search, 250);
  const q = useQuery({
    queryKey: ["customers", "pick", type, debounced],
    queryFn: () => api.get<Paged<Customer>>("/api/customers", { q: debounced, type, active: true, pageSize: 30 }),
  });
  // Seçili cari aramadan bağımsız listede kalsın.
  const selected = useQuery({
    queryKey: ["customers", value],
    queryFn: () => api.get<Customer>(`/api/customers/${value}`),
    enabled: !!value,
  });
  const items = [...(q.data?.items ?? [])];
  if (selected.data && !items.some((c) => c.id === selected.data!.id)) items.unshift(selected.data);

  return (
    <Select
      searchable
      searchValue={search}
      onSearchChange={setSearch}
      filter={({ options }) => options}
      nothingFoundMessage={q.isFetching ? "Aranıyor…" : "Cari bulunamadı"}
      data={items.map((c) => ({ value: String(c.id), label: `${c.title} (${c.code})` }))}
      value={value ? String(value) : null}
      onChange={(v) => onChange(v ? Number(v) : null, items.find((c) => String(c.id) === v))}
      {...rest}
    />
  );
}

/** Sunucuda arayan ürün/hizmet seçici. */
export function ProductSelect({ value, onChange, ...rest }: { value: number | null; onChange: (p: Product | null) => void } & Omit<SelectProps, "value" | "onChange" | "data">) {
  const [search, setSearch] = useState("");
  const [debounced] = useDebouncedValue(search, 250);
  const q = useQuery({
    queryKey: ["products", "pick", debounced],
    queryFn: () => api.get<Paged<Product>>("/api/products", { q: debounced, active: true, pageSize: 30 }),
  });
  const items = q.data?.items ?? [];

  return (
    <Select
      searchable
      clearable
      searchValue={search}
      onSearchChange={setSearch}
      filter={({ options }) => options}
      nothingFoundMessage="Ürün bulunamadı"
      data={items.map((p) => ({ value: String(p.id), label: `${p.name} — ${money(p.salePrice)}` }))}
      value={value ? String(value) : null}
      onChange={(v) => onChange(items.find((p) => String(p.id) === v) ?? null)}
      {...rest}
    />
  );
}

/** Bakiye metni: (+) alacak, (−) borç; renkli. */
export function BalanceText({ value, size = "sm" }: { value: number; size?: string }) {
  const color = value > 0 ? "teal" : value < 0 ? "red" : "dimmed";
  return (
    <Text size={size} fw={600} c={color} className="num" span>
      {money(Math.abs(value))} {value > 0 ? "(A)" : value < 0 ? "(B)" : ""}
    </Text>
  );
}

/** Bugün (yerel) YYYY-AA-GG. */
export const todayIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};

export const firstOfYearIso = () => `${new Date().getFullYear()}-01-01`;

/** Sunucunun döndürdüğü dosyayı (PDF/Excel) indirir; oturum çereziyle. */
export async function downloadFile(url: string) {
  const res = await fetch(url, { credentials: "same-origin" });
  if (!res.ok) throw new Error("Dosya indirilemedi.");
  const blob = await res.blob();
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(res.headers.get("content-disposition") ?? "")?.[1];
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = name ? decodeURIComponent(name) : "dosya";
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 2000);
}
