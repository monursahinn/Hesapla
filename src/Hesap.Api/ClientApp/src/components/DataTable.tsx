import type { ReactNode } from "react";
import { Card, Center, Group, Loader, Stack, Table, Text } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { IconInbox } from "@tabler/icons-react";

export interface Column<T> {
  key: string;
  header: ReactNode;
  render: (row: T) => ReactNode;
  align?: "left" | "right" | "center";
  width?: number | string;
  /** Mobil kart görünümünde gizle */
  hideOnMobile?: boolean;
}

interface Props<T> {
  rows: T[] | undefined;
  columns: Column<T>[];
  rowKey: (row: T) => string | number;
  loading?: boolean;
  emptyText?: string;
  /** Satır sonu işlem düğmeleri */
  actions?: (row: T) => ReactNode;
  onRowClick?: (row: T) => void;
  title?: ReactNode;
  toolbar?: ReactNode;
}

/**
 * Standart tablo: masaüstünde tablo (kart içinde yatay kayar), telefonda her satır kart olarak görünür.
 * Boş ve yükleniyor durumları her sayfada aynıdır.
 */
export function DataTable<T>({ rows, columns, rowKey, loading, emptyText, actions, onRowClick, title, toolbar }: Props<T>) {
  const isMobile = useMediaQuery("(max-width: 48em)");

  const header = (title || toolbar) && (
    <Group justify="space-between" mb="sm" wrap="wrap" gap="xs">
      {typeof title === "string" ? <Text fw={600}>{title}</Text> : title}
      {toolbar}
    </Group>
  );

  let body: ReactNode;

  if (loading && !rows) {
    body = (
      <Center py="xl">
        <Loader size="sm" />
      </Center>
    );
  } else if (!rows || rows.length === 0) {
    body = (
      <Center py={40}>
        <Stack align="center" gap={6}>
          <IconInbox size={32} stroke={1.4} opacity={0.5} />
          <Text c="dimmed" size="sm">
            {emptyText ?? "Kayıt bulunamadı."}
          </Text>
        </Stack>
      </Center>
    );
  } else if (isMobile) {
    body = (
      <Stack gap="xs">
        {rows.map((row) => (
          <Card
            key={rowKey(row)}
            padding="sm"
            withBorder
            onClick={onRowClick ? () => onRowClick(row) : undefined}
            style={onRowClick ? { cursor: "pointer" } : undefined}
          >
            <Stack gap={6}>
              {columns
                .filter((c) => !c.hideOnMobile)
                .map((c, i) => (
                  <Group key={c.key} justify="space-between" gap="xs" wrap="nowrap" align="flex-start">
                    {i > 0 && (
                      <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
                        {c.header}
                      </Text>
                    )}
                    <div style={{ textAlign: i === 0 ? "left" : "right", minWidth: 0, flex: i === 0 ? 1 : undefined }}>
                      {c.render(row)}
                    </div>
                  </Group>
                ))}
              {actions && (
                <Group justify="flex-end" gap="xs" mt={4}>
                  {actions(row)}
                </Group>
              )}
            </Stack>
          </Card>
        ))}
      </Stack>
    );
  } else {
    body = (
      <Table.ScrollContainer minWidth={640}>
        <Table striped={false}>
          <Table.Thead>
            <Table.Tr>
              {columns.map((c) => (
                <Table.Th key={c.key} style={{ textAlign: c.align, width: c.width, whiteSpace: "nowrap" }}>
                  {c.header}
                </Table.Th>
              ))}
              {actions && <Table.Th style={{ textAlign: "right" }}>İşlem</Table.Th>}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row) => (
              <Table.Tr
                key={rowKey(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                style={onRowClick ? { cursor: "pointer" } : undefined}
              >
                {columns.map((c) => (
                  <Table.Td key={c.key} style={{ textAlign: c.align }}>
                    {c.render(row)}
                  </Table.Td>
                ))}
                {actions && (
                  <Table.Td style={{ textAlign: "right", whiteSpace: "nowrap" }} onClick={(e) => e.stopPropagation()}>
                    <Group gap={6} justify="flex-end" wrap="nowrap">
                      {actions(row)}
                    </Group>
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    );
  }

  // Kartı sayfa çizer (altına sayfalama/özet eklenebilsin diye); tablo yalnızca içeriği verir.
  return (
    <>
      {header}
      {body}
    </>
  );
}
