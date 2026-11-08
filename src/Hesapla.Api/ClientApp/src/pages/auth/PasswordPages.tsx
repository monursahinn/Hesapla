import { useState } from "react";
import { Alert, Anchor, Button, PasswordInput, Stack, Text, TextInput } from "@mantine/core";
import { useForm } from "@mantine/form";
import { IconCircleCheck, IconMail } from "@tabler/icons-react";
import { Link, useSearchParams } from "react-router";
import { api, ApiError } from "@/api/client";
import { AuthLayout } from "./AuthLayout";

export function ForgotPasswordPage() {
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const form = useForm({
    initialValues: { email: "" },
    validate: { email: (v) => (/^\S+@\S+\.\S+$/.test(v.trim()) ? null : "Geçerli bir e-posta adresi girin") },
  });

  return (
    <AuthLayout title="Şifremi Unuttum" subtitle="Kayıtlı e-posta adresinize sıfırlama bağlantısı gönderelim">
      {done ? (
        <Stack>
          <Alert color="teal" variant="light" icon={<IconCircleCheck size={18} />}>
            {done}
          </Alert>
          <Button component={Link} to="/login" variant="light">
            Girişe dön
          </Button>
        </Stack>
      ) : (
        <form
          noValidate
          onSubmit={form.onSubmit(async (v) => {
            setBusy(true);
            setError(null);
            try {
              const r = await api.raw("/api/auth/forgot-password", { method: "POST", body: { email: v.email.trim() }, skipAuthRedirect: true });
              setDone(r.message);
            } catch (e) {
              setError(e instanceof ApiError ? e.message : "İstek gönderilemedi.");
            } finally {
              setBusy(false);
            }
          })}
        >
          <Stack>
            {error && <Alert color="red">{error}</Alert>}
            <TextInput label="E-posta" placeholder="ornek@mail.com" leftSection={<IconMail size={16} />} autoFocus {...form.getInputProps("email")} />
            <Button type="submit" loading={busy} fullWidth>
              Bağlantı Gönder
            </Button>
            <Text size="sm" ta="center">
              <Anchor component={Link} to="/login">
                Girişe dön
              </Anchor>
            </Text>
          </Stack>
        </form>
      )}
    </AuthLayout>
  );
}

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [done, setDone] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const form = useForm({
    initialValues: { newPassword: "", newPasswordConfirm: "" },
    validate: {
      newPassword: (v) => (v.length >= 8 && /\d/.test(v) && /\p{L}/u.test(v) ? null : "En az 8 karakter; harf ve rakam içermeli"),
      newPasswordConfirm: (v, all) => (v === all.newPassword ? null : "Şifreler uyuşmuyor"),
    },
  });

  return (
    <AuthLayout title="Yeni Şifre Belirle" subtitle="En az 8 karakter; harf ve rakam içermeli">
      {!token ? (
        <Alert color="red">Bağlantı geçersiz veya eksik. Lütfen e-postadaki bağlantıyı yeniden açın.</Alert>
      ) : done ? (
        <Stack>
          <Alert color="teal" icon={<IconCircleCheck size={18} />}>
            {done}
          </Alert>
          <Button component={Link} to="/login">
            Giriş Yap
          </Button>
        </Stack>
      ) : (
        <form
          noValidate
          onSubmit={form.onSubmit(async (v) => {
            setBusy(true);
            setError(null);
            try {
              const r = await api.raw("/api/auth/reset-password", {
                method: "POST",
                body: { token, newPassword: v.newPassword },
                skipAuthRedirect: true,
              });
              setDone(r.message);
            } catch (e) {
              setError(e instanceof ApiError ? e.message : "Şifre değiştirilemedi.");
            } finally {
              setBusy(false);
            }
          })}
        >
          <Stack>
            {error && <Alert color="red">{error}</Alert>}
            <PasswordInput label="Yeni şifre" autoComplete="new-password" autoFocus {...form.getInputProps("newPassword")} />
            <PasswordInput label="Yeni şifre (tekrar)" autoComplete="new-password" {...form.getInputProps("newPasswordConfirm")} />
            <Button type="submit" loading={busy} fullWidth>
              Şifreyi Değiştir
            </Button>
          </Stack>
        </form>
      )}
    </AuthLayout>
  );
}
