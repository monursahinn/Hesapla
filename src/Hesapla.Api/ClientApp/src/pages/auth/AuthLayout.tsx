import type { ReactNode } from "react";
import { Box, Group, Image, List, Paper, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { useDocumentTitle } from "@mantine/hooks";
import { IconBuildingBank, IconChartBar, IconDeviceMobile, IconFileInvoice } from "@tabler/icons-react";

const FEATURES = [
  { icon: <IconFileInvoice size={16} />, text: "Satış ve alış faturası, PDF çıktısı" },
  { icon: <IconBuildingBank size={16} />, text: "Kasa, banka, tahsilat ve ödeme takibi" },
  { icon: <IconChartBar size={16} />, text: "Cari ekstre, kâr/zarar ve KDV raporları" },
  { icon: <IconDeviceMobile size={16} />, text: "Masaüstünde ve mobil uygulamada" },
];

/** Giriş / kayıt / şifre sayfaları: geniş ekranda solda tanıtım paneli, sağda form; telefonda yalnızca form. */
export function AuthLayout({ title, subtitle, children, wide }: { title: string; subtitle?: string; children: ReactNode; wide?: boolean }) {
  useDocumentTitle(`${title} • Hesapla`);
  const year = new Date().getFullYear();

  return (
    <Box mih="100dvh" className="asy-auth">
      <Box className="asy-auth-brand" visibleFrom="md">
        <Stack justify="space-between" h="100%" p={48} maw={560}>
          <Group gap="sm">
            <Image src="/app/brand/logo.svg" w={40} h={40} alt="" />
            <div>
              <Text fw={700} fz="lg" c="white" lh={1.2}>
                Hesapla
              </Text>
              <Text fz="xs" c="rgba(255,255,255,.7)">
                KOBİ ön muhasebe
              </Text>
            </div>
          </Group>

          <Stack gap="lg">
            <Title order={1} c="white" fz={34} lh={1.2}>
              İşletmenizin parası,
              <br />
              her an kontrol altında.
            </Title>
            <Text c="rgba(255,255,255,.78)" fz="md" maw={440}>
              Cari hesaplar, faturalar, kasa ve banka tek panelde. Kurulum yok, 14 gün ücretsiz deneyin.
            </Text>
            <List spacing="sm" center>
              {FEATURES.map((f) => (
                <List.Item
                  key={f.text}
                  icon={
                    <ThemeIcon size={28} radius="md" style={{ background: "rgba(255,255,255,.14)", color: "white" }}>
                      {f.icon}
                    </ThemeIcon>
                  }
                >
                  <Text c="white" fz="sm">
                    {f.text}
                  </Text>
                </List.Item>
              ))}
            </List>
          </Stack>

          <Text fz="xs" c="rgba(255,255,255,.55)">
            © {year} Hesapla
          </Text>
        </Stack>
      </Box>

      <Stack justify="center" align="center" p={{ base: "md", sm: "xl" }} mih="100dvh">
        <Stack w="100%" maw={wide ? 520 : 420} gap="lg">
          <Stack align="center" gap={6} hiddenFrom="md">
            <Image src="/app/brand/logo.svg" w={56} h={56} alt="Hesapla" />
            <Title order={2} fz="h3">
              Hesapla
            </Title>
            <Text c="dimmed" size="sm">
              KOBİ ön muhasebe
            </Text>
          </Stack>

          <Paper withBorder shadow="md" p={{ base: "lg", sm: 32 }} radius="lg">
            <Stack gap={4} mb="lg">
              <Title order={2} fz="h3">
                {title}
              </Title>
              {subtitle && (
                <Text c="dimmed" size="sm">
                  {subtitle}
                </Text>
              )}
            </Stack>
            {children}
          </Paper>
        </Stack>
      </Stack>
    </Box>
  );
}
