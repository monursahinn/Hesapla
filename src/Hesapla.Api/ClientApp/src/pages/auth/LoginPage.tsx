import { useState } from "react";
import { Alert, Anchor, Button, Group, PasswordInput, Stack, Text, TextInput } from "@mantine/core";
import { useForm } from "@mantine/form";
import { IconAlertCircle, IconLock, IconMail } from "@tabler/icons-react";
import { Link, Navigate, useLocation, useNavigate } from "react-router";
import { ApiError } from "@/api/client";
import { useAuth } from "@/auth/AuthProvider";
import { AppSplash } from "@/components/AppSplash";
import { AuthLayout } from "./AuthLayout";

export function LoginPage() {
  const { me, loading, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const form = useForm({
    initialValues: { email: "", password: "" },
    validate: {
      email: (v) => (/^\S+@\S+\.\S+$/.test(v.trim()) ? null : "Geçerli bir e-posta adresi girin"),
      password: (v) => (v ? null : "Şifre girin"),
    },
  });

  const from = (location.state as { from?: string } | null)?.from ?? "/";
  if (loading) return <AppSplash />;
  if (me) return <Navigate to={from} replace />;

  const submit = form.onSubmit(async (v) => {
    setError(null);
    setBusy(true);
    try {
      await login(v.email.trim(), v.password);
      navigate(from, { replace: true });
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Giriş yapılamadı.");
      form.setFieldValue("password", "");
    } finally {
      setBusy(false);
    }
  });

  return (
    <AuthLayout title="Giriş yap" subtitle="Hesabınızla devam edin">
      <form onSubmit={submit} noValidate>
        <Stack>
          {error && (
            <Alert color="red" variant="light" icon={<IconAlertCircle size={18} />}>
              {error}
            </Alert>
          )}
          <TextInput label="E-posta" placeholder="ornek@firma.com" autoComplete="username" autoFocus leftSection={<IconMail size={16} />} {...form.getInputProps("email")} />
          <PasswordInput label="Şifre" placeholder="••••••••" autoComplete="current-password" leftSection={<IconLock size={16} />} {...form.getInputProps("password")} />
          <Group justify="flex-end">
            <Anchor component={Link} to="/forgot-password" size="sm">
              Şifremi unuttum
            </Anchor>
          </Group>
          <Button type="submit" size="md" loading={busy} fullWidth>
            Giriş yap
          </Button>
          <Text size="sm" ta="center" c="dimmed">
            Hesabınız yok mu?{" "}
            <Anchor component={Link} to="/register">
              14 gün ücretsiz deneyin
            </Anchor>
          </Text>
        </Stack>
      </form>
    </AuthLayout>
  );
}
