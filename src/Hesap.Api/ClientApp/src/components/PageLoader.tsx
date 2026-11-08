import { Alert, Button, Card, Group, SimpleGrid, Skeleton, Stack } from "@mantine/core";
import { IconAlertTriangle } from "@tabler/icons-react";
import { ApiError } from "@/api/client";

/**
 * Sayfa yüklenirken içeriğin iskeleti gösterilir (başlık, özet kartları, tablo). Boş ekranda dönen
 * simgeye göre daha sakindir ve içeriğin geleceği yeri önceden gösterir.
 */
export function PageLoader({ stats = 3, rows = 6 }: { stats?: number; rows?: number }) {
  return (
    <Stack gap="lg" aria-busy="true" aria-label="Yükleniyor">
      <Group justify="space-between" align="flex-end">
        <Stack gap={8}>
          <Skeleton h={12} w={120} />
          <Skeleton h={24} w={220} />
          <Skeleton h={14} w={300} />
        </Stack>
        <Skeleton h={36} w={130} />
      </Group>
      {stats > 0 && (
        <SimpleGrid cols={{ base: 1, xs: 2, md: stats }}>
          {Array.from({ length: stats }).map((_, i) => (
            <Card key={i}>
              <Group justify="space-between" align="flex-start">
                <Stack gap={10}>
                  <Skeleton h={12} w={90} />
                  <Skeleton h={26} w={120} />
                </Stack>
                <Skeleton h={42} w={42} />
              </Group>
            </Card>
          ))}
        </SimpleGrid>
      )}
      <Card>
        <Stack gap="md">
          <Skeleton h={14} w="30%" />
          {Array.from({ length: rows }).map((_, i) => (
            <Skeleton key={i} h={18} w={`${92 - (i % 3) * 9}%`} />
          ))}
        </Stack>
      </Card>
    </Stack>
  );
}

/** Sayfa verisi yüklenemediğinde standart hata kutusu. */
export function PageError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const message = error instanceof ApiError ? error.message : "Veriler yüklenemedi.";
  return (
    <Alert color="red" variant="light" icon={<IconAlertTriangle size={18} />} title="Bir sorun oluştu" radius="lg">
      <Stack gap="sm" align="flex-start">
        {message}
        {onRetry && (
          <Button size="xs" variant="light" color="red" onClick={onRetry}>
            Tekrar dene
          </Button>
        )}
      </Stack>
    </Alert>
  );
}
