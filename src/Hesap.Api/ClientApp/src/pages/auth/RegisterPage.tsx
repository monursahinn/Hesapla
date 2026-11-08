import { useState } from "react";
import { Alert, Anchor, Button, PasswordInput, SimpleGrid, Stack, Text, TextInput } from "@mantine/core";
import { useForm } from "@mantine/form";
import { IconAlertCircle } from "@tabler/icons-react";
import { Link, Navigate, useNavigate } from "react-router";
import { api, ApiError } from "@/api/client";
import { useAuth, type Me } from "@/auth/AuthProvider";
import { AppSplash } from "@/components/AppSplash";
import { notify } from "@/lib/notify";
import { AuthLayout } from "./AuthLayout";

/** Yeni firma kaydı: firma ve yönetici hesabı birlikte açılır, 14 günlük deneme başlar. */
export function RegisterPage() {
  const { me, loading, setSession } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const form = useForm({
    initialValues: { companyName: "", taxNumber: "", fullName: "", email: "", phone: "", password: "", password2: "" },
    validate: {
      companyName: (v) => (v.trim().length >= 2 ? null : "Firma unvanını girin"),
      taxNumber: (v) => (!v.trim() || /^\d{10,11}$/.test(v.trim()) ? null : "Vergi no 10, T.C. kimlik no 11 hane olmalı"),
      fullName: (v) => (v.trim().length >= 3 ? null : "Adınızı ve soyadınızı girin"),
      email: (v) => (/^\S+@\S+\.\S+$/.test(v.trim()) ? null : "Geçerli bir e-posta adresi girin"),
      password: (v) => (v.length >= 8 && /\d/.test(v) && /\p{L}/u.test(v) ? null : "En az 8 karakter; harf ve rakam içermeli"),
      password2: (v, all) => (v === all.password ? null : "Şifreler uyuşmuyor"),
    },
  });

  if (loading) return <AppSplash />;
  if (me) return <Navigate to="/" replace />;

  const submit = form.onSubmit(async (v) => {
    setError(null);
    setBusy(true);
    try {
      const r = await api.raw<Me>("/api/auth/register", {
        method: "POST",
        skipAuthRedirect: true,
        body: { companyName: v.companyName.trim(), taxNumber: v.taxNumber.trim() || null, fullName: v.fullName.trim(), email: v.email.trim(), phone: v.phone.trim() || null, password: v.password },
      });
      setSession(r.data!);
      notify.success(r.message);
      navigate("/", { replace: true });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Kayıt yapılamadı.");
    } finally {
      setBusy(false);
    }
  });

  return (
    <AuthLayout title="Ücretsiz deneyin" subtitle="14 gün boyunca tüm özellikler açık; kredi kartı gerekmez." wide>
      <form onSubmit={submit} noValidate>
        <Stack>
          {error && (
            <Alert color="red" variant="light" icon={<IconAlertCircle size={18} />}>
              {error}
            </Alert>
          )}
          <TextInput label="Firma unvanı" placeholder="Örnek Ticaret Ltd. Şti." maxLength={200} autoFocus {...form.getInputProps("companyName")} />
          <TextInput label="Vergi no / T.C. kimlik no" description="İsteğe bağlı; faturalarınızda görünür" maxLength={11} {...form.getInputProps("taxNumber")} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="Adınız soyadınız" maxLength={120} autoComplete="name" {...form.getInputProps("fullName")} />
            <TextInput label="Telefon" placeholder="05xx xxx xx xx" maxLength={30} autoComplete="tel" {...form.getInputProps("phone")} />
          </SimpleGrid>
          <TextInput label="E-posta" placeholder="ornek@firma.com" maxLength={150} autoComplete="email" {...form.getInputProps("email")} />
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <PasswordInput label="Şifre" autoComplete="new-password" maxLength={128} {...form.getInputProps("password")} />
            <PasswordInput label="Şifre (tekrar)" autoComplete="new-password" maxLength={128} {...form.getInputProps("password2")} />
          </SimpleGrid>
          <Button type="submit" size="md" loading={busy} fullWidth>
            Hesabımı oluştur
          </Button>
          <Text size="sm" ta="center" c="dimmed">
            Zaten hesabınız var mı?{" "}
            <Anchor component={Link} to="/login">
              Giriş yapın
            </Anchor>
          </Text>
        </Stack>
      </form>
    </AuthLayout>
  );
}
