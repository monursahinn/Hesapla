import { useEffect, useState } from "react";
import { ActionIcon, Badge, Button, Card, ColorSwatch, FileButton, Group, Image, Modal, PasswordInput, Select, SimpleGrid, Stack, Switch, Tabs, Text, TextInput, Textarea, Tooltip } from "@mantine/core";
import { useForm } from "@mantine/form";
import { useQuery } from "@tanstack/react-query";
import { IconEdit, IconLockOpen, IconPlus, IconUpload } from "@tabler/icons-react";
import { useSearchParams } from "react-router";
import { api } from "@/api/client";
import type { Category } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { THEMES } from "@/theme";
import { useThemePreference } from "@/themePreference";
import { date, dateTime } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";

interface Firm {
  name: string; taxNumber?: string | null; taxOffice?: string | null; address?: string | null; city?: string | null; district?: string | null;
  phone?: string | null; email?: string | null; iban?: string | null; invoiceNote?: string | null; hasLogo: boolean; plan: string; subscriptionEnd: string;
}
interface User { id: number; fullName: string; email: string; phone?: string | null; role: string; roleLabel: string; active: boolean; locked: boolean; lastLogin?: string | null }

const PLANS: Record<string, string> = { DE: "Deneme", ST: "Standart", PR: "Profesyonel" };
const ROLES = [
  { value: "AD", label: "Yönetici — her şey ve kullanıcılar" },
  { value: "AC", label: "Muhasebe — kayıt girer, ayarları değiştiremez" },
  { value: "VW", label: "Görüntüleyici — yalnızca görür ve dışa aktarır" },
];

function FirmTab() {
  const { hasRole } = useAuth();
  const admin = hasRole("AD");
  const [logoVersion, setLogoVersion] = useState(0);
  const q = useQuery({ queryKey: ["firm"], queryFn: () => api.get<Firm>("/api/firm") });
  const form = useForm({
    initialValues: { name: "", taxNumber: "", taxOffice: "", address: "", city: "", district: "", phone: "", email: "", iban: "", invoiceNote: "" },
    validate: { name: (v) => (v.trim() ? null : "Unvan girin"), taxNumber: (v) => (!v.trim() || /^\d{10,11}$/.test(v.trim()) ? null : "10 veya 11 hane") },
  });
  useEffect(() => {
    const f = q.data;
    if (!f) return;
    form.setValues({ name: f.name, taxNumber: f.taxNumber ?? "", taxOffice: f.taxOffice ?? "", address: f.address ?? "", city: f.city ?? "", district: f.district ?? "", phone: f.phone ?? "", email: f.email ?? "", iban: f.iban ?? "", invoiceNote: f.invoiceNote ?? "" });
    form.resetDirty();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q.data]);

  const save = useApiMutation<typeof form.values>({
    mutationFn: (v) => api.put("/api/firm", Object.fromEntries(Object.entries(v).map(([k, x]) => [k, x.trim() || null]))),
    invalidate: [["firm"], ["auth", "me"]],
  });
  const logo = useApiMutation<File>({
    mutationFn: (f) => { const fd = new FormData(); fd.set("file", f); return api.post("/api/firm/logo", fd); },
    invalidate: [["firm"]],
    onSuccess: () => setLogoVersion((v) => v + 1),
  });

  return (
    <SimpleGrid cols={{ base: 1, lg: 3 }}>
      <Card style={{ gridColumn: "span 2" }}>
        <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
          <fieldset disabled={!admin} style={{ border: 0, padding: 0, margin: 0 }}>
            <Stack>
              <TextInput label="Firma unvanı" maxLength={200} {...form.getInputProps("name")} />
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label="Vergi no / T.C. kimlik no" maxLength={11} {...form.getInputProps("taxNumber")} />
                <TextInput label="Vergi dairesi" maxLength={80} {...form.getInputProps("taxOffice")} />
                <TextInput label="Telefon" maxLength={30} {...form.getInputProps("phone")} />
                <TextInput label="E-posta" maxLength={150} {...form.getInputProps("email")} />
                <TextInput label="İl" maxLength={60} {...form.getInputProps("city")} />
                <TextInput label="İlçe" maxLength={80} {...form.getInputProps("district")} />
              </SimpleGrid>
              <Textarea label="Adres" autosize minRows={1} maxLength={500} {...form.getInputProps("address")} />
              <TextInput label="IBAN (faturada gösterilir)" maxLength={40} {...form.getInputProps("iban")} />
              <Textarea label="Fatura notu" description="Satış faturalarının altına yazılır" autosize minRows={2} maxLength={500} {...form.getInputProps("invoiceNote")} />
              {admin && <Group justify="flex-end"><Button type="submit" loading={save.isPending}>Kaydet</Button></Group>}
            </Stack>
          </fieldset>
        </form>
      </Card>
      <Stack>
        <Card>
          <Text fw={600} mb="xs">Logo</Text>
          <Text size="xs" c="dimmed" mb="sm">Fatura PDF'inde görünür. PNG veya JPEG, en fazla 1 MB.</Text>
          {q.data?.hasLogo ? <Image src={`/api/firm/logo?v=${logoVersion}`} h={80} fit="contain" alt="Firma logosu" mb="sm" /> : <Text size="sm" c="dimmed" mb="sm">Logo yüklenmedi.</Text>}
          {admin && (
            <FileButton accept="image/png,image/jpeg" onChange={(f) => f && logo.mutate(f)}>
              {(props) => <Button {...props} variant="default" leftSection={<IconUpload size={16} />} loading={logo.isPending}>Logo yükle</Button>}
            </FileButton>
          )}
        </Card>
        <Card>
          <Text fw={600} mb="xs">Abonelik</Text>
          <Text size="sm">Paket: <b>{PLANS[q.data?.plan ?? ""] ?? "—"}</b></Text>
          <Text size="sm">Bitiş: <b>{date(q.data?.subscriptionEnd)}</b></Text>
        </Card>
      </Stack>
    </SimpleGrid>
  );
}

function UserForm({ editing, onClose }: { editing: Partial<User> | null; onClose: () => void }) {
  const isNew = !editing?.id;
  const form = useForm({
    initialValues: { fullName: "", email: "", phone: "", role: "AC", password: "", active: true },
    validate: {
      fullName: (v) => (v.trim() ? null : "Ad soyad girin"),
      email: (v) => (!isNew || /^\S+@\S+\.\S+$/.test(v.trim()) ? null : "Geçerli e-posta girin"),
      password: (v) => (!isNew || (v.length >= 8 && /\d/.test(v) && /\p{L}/u.test(v)) ? null : "En az 8 karakter, harf ve rakam"),
    },
  });
  useEffect(() => {
    if (!editing) return;
    form.setValues({ fullName: editing.fullName ?? "", email: editing.email ?? "", phone: editing.phone ?? "", role: editing.role ?? "AC", password: "", active: editing.active ?? true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing]);
  const save = useApiMutation<typeof form.values>({
    mutationFn: (v) =>
      isNew
        ? api.post("/api/users", { fullName: v.fullName.trim(), email: v.email.trim(), phone: v.phone.trim() || null, role: v.role, password: v.password })
        : api.put(`/api/users/${editing!.id}`, { fullName: v.fullName.trim(), phone: v.phone.trim() || null, role: v.role, active: v.active }),
    invalidate: [["users"]],
    onSuccess: onClose,
  });
  return (
    <Modal opened={!!editing} onClose={onClose} title={isNew ? "Yeni kullanıcı" : "Kullanıcıyı düzenle"}>
      <form onSubmit={form.onSubmit((v) => save.mutate(v))} noValidate>
        <Stack>
          <TextInput label="Ad soyad" maxLength={120} {...form.getInputProps("fullName")} />
          <TextInput label="E-posta" maxLength={150} disabled={!isNew} {...form.getInputProps("email")} />
          <TextInput label="Telefon" maxLength={30} {...form.getInputProps("phone")} />
          <Select label="Rol" data={ROLES} {...form.getInputProps("role")} />
          {isNew ? (
            <PasswordInput label="Geçici şifre" description="Kullanıcıya iletin; giriş yaptıktan sonra değiştirebilir." maxLength={128} {...form.getInputProps("password")} />
          ) : (
            <Switch label="Aktif" {...form.getInputProps("active", { type: "checkbox" })} />
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={onClose}>Vazgeç</Button>
            <Button type="submit" loading={save.isPending}>Kaydet</Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function UsersTab() {
  const [editing, setEditing] = useState<Partial<User> | null>(null);
  const q = useQuery({ queryKey: ["users"], queryFn: () => api.get<User[]>("/api/users") });
  const unlock = useApiMutation<number>({ mutationFn: (id) => api.post(`/api/users/${id}/unlock`), invalidate: [["users"]] });
  return (
    <Card>
      <DataTable
        toolbar={<Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing({})}>Kullanıcı ekle</Button>}
        rows={q.data}
        loading={q.isFetching}
        rowKey={(u) => u.id}
        columns={[
          { key: "name", header: "Ad soyad", render: (u) => <Group gap={6}><span>{u.fullName}</span>{!u.active && <Badge color="gray" size="sm">Pasif</Badge>}{u.locked && <Badge color="red" size="sm">Kilitli</Badge>}</Group> },
          { key: "email", header: "E-posta", render: (u) => u.email },
          { key: "role", header: "Rol", render: (u) => u.roleLabel },
          { key: "last", header: "Son giriş", render: (u) => (u.lastLogin ? dateTime(u.lastLogin) : "—"), hideOnMobile: true },
        ]}
        actions={(u) => (
          <Group gap={4} wrap="nowrap">
            {u.locked && <Tooltip label="Kilidi aç"><ActionIcon variant="subtle" aria-label="Kilidi aç" onClick={() => unlock.mutate(u.id)}><IconLockOpen size={16} /></ActionIcon></Tooltip>}
            <Tooltip label="Düzenle"><ActionIcon variant="subtle" color="gray" aria-label="Düzenle" onClick={() => setEditing(u)}><IconEdit size={16} /></ActionIcon></Tooltip>
          </Group>
        )}
      />
      <UserForm editing={editing} onClose={() => setEditing(null)} />
    </Card>
  );
}

function CategoriesTab() {
  const q = useQuery({ queryKey: ["categories"], queryFn: () => api.get<Category[]>("/api/categories") });
  const [editing, setEditing] = useState<Partial<Category> | null>(null);
  const [name, setName] = useState("");
  const [type, setType] = useState<string>("GI");
  const [active, setActive] = useState(true);
  useEffect(() => { setName(editing?.name ?? ""); setType(editing?.type ?? "GI"); setActive(editing?.active ?? true); }, [editing]);
  const save = useApiMutation<void>({
    mutationFn: () => (editing?.id ? api.put(`/api/categories/${editing.id}`, { type, name: name.trim(), active }) : api.post("/api/categories", { type, name: name.trim(), active: true })),
    invalidate: [["categories"]],
    onSuccess: () => setEditing(null),
  });
  return (
    <Card>
      <DataTable
        toolbar={<Button size="xs" leftSection={<IconPlus size={14} />} onClick={() => setEditing({})}>Kategori ekle</Button>}
        rows={q.data}
        loading={q.isFetching}
        rowKey={(c) => c.id}
        columns={[
          { key: "name", header: "Ad", render: (c) => <Group gap={6}><span>{c.name}</span>{!c.active && <Badge color="gray" size="sm">Pasif</Badge>}</Group> },
          { key: "type", header: "Tür", render: (c) => (c.type === "GI" ? "Gider" : "Gelir") },
        ]}
        actions={(c) => <ActionIcon variant="subtle" color="gray" aria-label="Düzenle" onClick={() => setEditing(c)}><IconEdit size={16} /></ActionIcon>}
      />
      <Modal opened={!!editing} onClose={() => setEditing(null)} title={editing?.id ? "Kategoriyi düzenle" : "Yeni kategori"}>
        <Stack>
          <Select label="Tür" data={[{ value: "GI", label: "Gider" }, { value: "GE", label: "Gelir" }]} value={type} onChange={(v) => v && setType(v)} />
          <TextInput label="Ad" maxLength={80} value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
          {editing?.id && <Switch label="Aktif" checked={active} onChange={(e) => setActive(e.currentTarget.checked)} />}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setEditing(null)}>Vazgeç</Button>
            <Button disabled={!name.trim()} loading={save.isPending} onClick={() => save.mutate()}>Kaydet</Button>
          </Group>
        </Stack>
      </Modal>
    </Card>
  );
}

function AccountTab() {
  const { me } = useAuth();
  const { pref, apply } = useThemePreference();
  const form = useForm({
    initialValues: { currentPassword: "", newPassword: "", newPassword2: "" },
    validate: {
      currentPassword: (v) => (v ? null : "Mevcut şifrenizi girin"),
      newPassword: (v) => (v.length >= 8 && /\d/.test(v) && /\p{L}/u.test(v) ? null : "En az 8 karakter, harf ve rakam"),
      newPassword2: (v, all) => (v === all.newPassword ? null : "Şifreler uyuşmuyor"),
    },
  });
  const change = useApiMutation<typeof form.values>({
    mutationFn: (v) => api.post("/api/auth/change-password", { currentPassword: v.currentPassword, newPassword: v.newPassword }),
    onSuccess: () => form.reset(),
  });
  return (
    <SimpleGrid cols={{ base: 1, md: 2 }}>
      <Card>
        <Text fw={600}>{me?.fullName}</Text>
        <Text size="sm" c="dimmed" mb="md">{me?.email} • {me?.roleLabel}</Text>
        <form onSubmit={form.onSubmit((v) => change.mutate(v))} noValidate>
          <Stack>
            <PasswordInput label="Mevcut şifre" autoComplete="current-password" {...form.getInputProps("currentPassword")} />
            <PasswordInput label="Yeni şifre" autoComplete="new-password" maxLength={128} {...form.getInputProps("newPassword")} />
            <PasswordInput label="Yeni şifre (tekrar)" autoComplete="new-password" maxLength={128} {...form.getInputProps("newPassword2")} />
            <Group justify="flex-end"><Button type="submit" loading={change.isPending}>Şifreyi değiştir</Button></Group>
          </Stack>
        </form>
      </Card>
      <Card>
        <Text fw={600} mb="sm">Görünüm</Text>
        <SimpleGrid cols={{ base: 2, sm: 3 }}>
          {THEMES.map((t) => (
            <Button key={t.value} variant={pref === t.value ? "light" : "default"} onClick={() => apply(t.value)} leftSection={<ColorSwatch color={t.swatch[1]} size={16} />}>
              {t.label}
            </Button>
          ))}
        </SimpleGrid>
      </Card>
    </SimpleGrid>
  );
}

export default function SettingsPage() {
  const { hasRole } = useAuth();
  const [params, setParams] = useSearchParams();
  const firmUser = hasRole("AD", "AC", "VW");
  const tab = params.get("tab") ?? (firmUser ? "firm" : "account");

  return (
    <>
      <PageHeader title="Ayarlar" description={firmUser ? "Firma bilgileri, kullanıcılar ve hesabınız" : "Hesabınız"} />
      <Tabs value={tab} onChange={(v) => v && setParams({ tab: v }, { replace: true })} keepMounted={false}>
        <Tabs.List mb="md">
          {firmUser && <Tabs.Tab value="firm">Firma</Tabs.Tab>}
          {hasRole("AD") && <Tabs.Tab value="users">Kullanıcılar</Tabs.Tab>}
          {hasRole("AD", "AC") && <Tabs.Tab value="categories">Gelir/gider kategorileri</Tabs.Tab>}
          <Tabs.Tab value="account">Hesabım</Tabs.Tab>
        </Tabs.List>
        {firmUser && <Tabs.Panel value="firm"><FirmTab /></Tabs.Panel>}
        {hasRole("AD") && <Tabs.Panel value="users"><UsersTab /></Tabs.Panel>}
        {hasRole("AD", "AC") && <Tabs.Panel value="categories"><CategoriesTab /></Tabs.Panel>}
        <Tabs.Panel value="account"><AccountTab /></Tabs.Panel>
      </Tabs>
    </>
  );
}
