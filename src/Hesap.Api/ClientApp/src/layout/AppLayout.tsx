import { ActionIcon, Alert, AppShell, Avatar, Box, Burger, Group, Image, NavLink, ScrollArea, Stack, Text, Tooltip, UnstyledButton } from "@mantine/core";
import { useDisclosure, useLocalStorage } from "@mantine/hooks";
import { IconAlertTriangle, IconLogout, IconMoon, IconSun } from "@tabler/icons-react";
import { Suspense } from "react";
import { NavLink as RouterLink, Outlet, useLocation, useNavigate } from "react-router";
import { useAuth } from "@/auth/AuthProvider";
import { PageLoader } from "@/components/PageLoader";
import { TopProgress } from "@/components/TopProgress";
import { useThemePreference } from "@/themePreference";
import { themeDef } from "@/theme";
import { date, initials } from "@/lib/format";
import { GROUP_LABELS, NAV_ITEMS, type NavItem } from "./nav";

function ColorSchemeToggle() {
  const { pref, apply } = useThemePreference();
  const dark = themeDef(pref).scheme === "dark";
  return (
    <Tooltip label={dark ? "Açık tema" : "Koyu tema"}>
      <ActionIcon variant="subtle" color="gray" size="lg" aria-label="Tema" onClick={() => apply(dark ? "Light" : "Dark")}>
        {dark ? <IconSun size={18} /> : <IconMoon size={18} />}
      </ActionIcon>
    </Tooltip>
  );
}

/** Abonelik uyarısı: süre dolduysa salt okunur, deneme bitmek üzereyse hatırlatma. */
function SubscriptionBanner() {
  const { me } = useAuth();
  const f = me?.firm;
  if (!f) return null;

  if (f.suspended)
    return (
      <Alert color="red" variant="light" icon={<IconAlertTriangle size={18} />} mb="lg" title="Firma hesabı askıda">
        Yeni kayıt girilemez. Lütfen bizimle iletişime geçin.
      </Alert>
    );
  if (f.expired)
    return (
      <Alert color="red" variant="light" icon={<IconAlertTriangle size={18} />} mb="lg" title="Abonelik süreniz doldu">
        Verilerinizi görebilir ve Excel'e aktarabilirsiniz; yeni kayıt için aboneliğinizi yenileyin.
      </Alert>
    );
  if (f.plan === "DE" && f.daysLeft <= 7)
    return (
      <Alert color="yellow" variant="light" icon={<IconAlertTriangle size={18} />} mb="lg">
        Deneme sürenizin bitmesine {f.daysLeft} gün kaldı ({date(f.subscriptionEnd)}). Kesintisiz kullanım için aboneliğe geçin.
      </Alert>
    );
  return null;
}

/**
 * Uygulama iskeleti: masaüstünde gizlenip açılabilen kenar menü, tablet/telefonda açılır menü.
 * İçerik alanı her ekran genişliğinde taşmadan sığar (Mantine AppShell).
 */
export function AppLayout() {
  const { me, hasRole, logout } = useAuth();
  const navigate = useNavigate();
  const [opened, { toggle, close }] = useDisclosure();
  const [desktopOpened, setDesktopOpened] = useLocalStorage({ key: "hesap-sidebar-open", defaultValue: true });
  const location = useLocation();

  const items = NAV_ITEMS.filter((i) => hasRole(...i.roles));
  const groups = (Object.keys(GROUP_LABELS) as NavItem["group"][])
    .map((g) => ({ g, items: items.filter((i) => i.group === g) }))
    .filter((x) => x.items.length > 0);

  const isActive = (path: string) => (path === "/" ? location.pathname === "/" : location.pathname.startsWith(path));

  return (
    <AppShell
      header={{ height: { base: 56, sm: 64 } }}
      navbar={{ width: { base: 264, xl: 280 }, breakpoint: "md", collapsed: { mobile: !opened, desktop: !desktopOpened } }}
      padding={{ base: "md", lg: "xl" }}
      styles={{
        header: { background: "var(--asy-nav-bg)" },
        navbar: { background: "var(--asy-nav-bg)" },
        main: { background: "var(--asy-page-bg)" },
      }}
    >
      <AppShell.Header>
        <TopProgress />
        <Group h="100%" px={{ base: "sm", sm: "lg" }} justify="space-between" wrap="nowrap" gap="sm">
          <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
            <Burger opened={opened} onClick={toggle} hiddenFrom="md" size="sm" aria-label="Menü" />
            <Tooltip label={desktopOpened ? "Menüyü gizle" : "Menüyü göster"}>
              <Burger opened={desktopOpened} onClick={() => setDesktopOpened((o) => !o)} visibleFrom="md" size="sm" aria-label="Menü" />
            </Tooltip>
            <RouterLink to="/" style={{ textDecoration: "none", color: "inherit", minWidth: 0 }}>
              <Group gap={10} wrap="nowrap">
                <Image src="/app/brand/logo.svg" w={32} h={32} alt="" />
                <Stack gap={0} style={{ minWidth: 0 }}>
                  <Text fw={700} fz="md" lh={1.15}>
                    {me?.appName ?? "Hesap"}
                  </Text>
                  <Text fz={11} c="dimmed" lh={1.2} truncate maw={{ base: 150, sm: 320 }}>
                    {me?.firm?.name ?? "Platform yönetimi"}
                  </Text>
                </Stack>
              </Group>
            </RouterLink>
          </Group>

          <Group gap={4} wrap="nowrap">
            <ColorSchemeToggle />
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar>
        <AppShell.Section grow component={ScrollArea} px="md" py="sm">
          <Stack gap="lg">
            {groups.map(({ g, items: gi }) => (
              <div key={g}>
                <Text fz={11} fw={600} c="dimmed" tt="uppercase" px="sm" mb={6} style={{ letterSpacing: "0.06em" }}>
                  {GROUP_LABELS[g]}
                </Text>
                {gi.map((item) => (
                  <NavLink
                    key={item.label}
                    component={RouterLink}
                    to={item.path}
                    label={item.label}
                    leftSection={item.icon}
                    active={isActive(item.path)}
                    onClick={close}
                    py={8}
                    mb={2}
                    className="asy-nav-link"
                  />
                ))}
              </div>
            ))}
          </Stack>
        </AppShell.Section>
        <AppShell.Section px="md" py="sm" style={{ borderTop: "1px solid var(--mantine-color-default-border)" }}>
          <Group gap="xs" wrap="nowrap">
            <UnstyledButton
              style={{ flex: 1, minWidth: 0 }}
              aria-label="Ayarlar"
              onClick={() => {
                close();
                navigate("/settings");
              }}
            >
              <Group gap="sm" wrap="nowrap">
                <Avatar size={36} radius="xl" color="brand">
                  {initials(me?.fullName)}
                </Avatar>
                <div style={{ minWidth: 0 }}>
                  <Text size="sm" fw={600} lh={1.25} truncate>
                    {me?.fullName}
                  </Text>
                  <Text size="xs" c="dimmed" lh={1.25} truncate>
                    {me?.roleLabel}
                  </Text>
                </div>
              </Group>
            </UnstyledButton>
            <Tooltip label="Çıkış yap">
              <ActionIcon variant="subtle" color="red" aria-label="Çıkış yap" onClick={() => void logout()}>
                <IconLogout size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </AppShell.Section>
      </AppShell.Navbar>

      <AppShell.Main>
        <Box maw={1520} mx="auto">
          <SubscriptionBanner />
          <Suspense fallback={<PageLoader />}>
            <Outlet />
          </Suspense>
        </Box>
      </AppShell.Main>
    </AppShell>
  );
}
