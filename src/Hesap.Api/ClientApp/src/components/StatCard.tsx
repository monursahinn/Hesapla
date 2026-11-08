import type { ReactNode } from "react";
import { Card, Group, Skeleton, Stack, Text, ThemeIcon } from "@mantine/core";
import { Link } from "react-router";

interface Props {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  icon?: ReactNode;
  /** Varsayılan marka rengi; durum anlamı taşıyorsa teal (olumlu), orange (bekleyen), red (gecikmiş/borç). */
  color?: string;
  loading?: boolean;
  /** Kart ilgili sayfaya giden gerçek bir bağlantı olur (yeni sekmede açılabilir, sayfa yenilenmez). */
  to?: string;
  /** Sayfa içi işlem (ör. listeyi bu duruma göre süz). */
  onClick?: () => void;
}

/** Özet kartı. Izgarada her zaman komşularıyla aynı yükseklikte durur. */
export function StatCard({ label, value, hint, icon, color = "brand", loading, to, onClick }: Props) {
  const body = (
    <Group justify="space-between" align="flex-start" wrap="nowrap" h="100%">
      <Stack gap={4} style={{ minWidth: 0 }} justify="space-between" h="100%">
        <div>
          <Text size="xs" tt="uppercase" fw={600} c="dimmed" style={{ letterSpacing: "0.04em" }}>
            {label}
          </Text>
          {loading ? (
            <Skeleton h={28} w={110} mt={8} />
          ) : (
            <Text fz={26} fw={700} mt={6} lh={1.15} className="num">
              {value}
            </Text>
          )}
        </div>
        {hint && (
          <Text size="xs" c="dimmed" mt={6}>
            {hint}
          </Text>
        )}
      </Stack>
      {icon && (
        <ThemeIcon variant="light" color={color} size={42} radius="md" style={{ flexShrink: 0 }}>
          {icon}
        </ThemeIcon>
      )}
    </Group>
  );

  if (to) {
    return (
      <Card component={Link} to={to} h="100%" className="asy-card-link" style={{ color: "inherit", textDecoration: "none" }}>
        {body}
      </Card>
    );
  }

  return (
    <Card
      onClick={onClick}
      h="100%"
      className={onClick ? "asy-card-link" : undefined}
      role={onClick ? "button" : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={
        onClick
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onClick();
              }
            }
          : undefined
      }
    >
      {body}
    </Card>
  );
}
