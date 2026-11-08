import type { ReactNode } from "react";
import { Anchor, Breadcrumbs, Group, Stack, Text, Title } from "@mantine/core";
import { useDocumentTitle } from "@mantine/hooks";
import { Link, useLocation } from "react-router";
import { GROUP_LABELS, NAV_ITEMS } from "@/layout/nav";

interface Props {
  title: string;
  description?: ReactNode;
  icon?: ReactNode;
  /** Sağ üstteki işlem düğmeleri */
  actions?: ReactNode;
}

/**
 * Her sayfanın üst kısmı: konum yolu (Anasayfa › Bölüm › Sayfa), başlık, açıklama ve işlemler.
 * Tarayıcı sekme başlığını da ayarlar.
 */
export function PageHeader({ title, description, icon, actions }: Props) {
  useDocumentTitle(`${title} • Hesap`);
  const { pathname } = useLocation();
  const item = NAV_ITEMS.find((i) => i.path !== "/" && pathname.startsWith(i.path));

  return (
    <Group justify="space-between" align="flex-end" gap="md" mb="lg" wrap="wrap">
      <Stack gap={6} style={{ minWidth: 0 }}>
        {item && (
          <Breadcrumbs separator="›" separatorMargin={6} visibleFrom="sm">
            <Anchor component={Link} to="/" size="xs" c="dimmed">
              Pano
            </Anchor>
            <Text size="xs" c="dimmed">
              {GROUP_LABELS[item.group]}
            </Text>
            <Text size="xs" fw={500}>
              {item.label}
            </Text>
          </Breadcrumbs>
        )}
        <Group gap="sm" wrap="nowrap" style={{ minWidth: 0 }}>
          {icon}
          <Stack gap={2} style={{ minWidth: 0 }}>
            <Title order={1} fz={{ base: "h3", sm: "h2" }}>
              {title}
            </Title>
            {description && (
              <Text c="dimmed" size="sm">
                {description}
              </Text>
            )}
          </Stack>
        </Group>
      </Stack>
      {actions && <Group gap="xs">{actions}</Group>}
    </Group>
  );
}
