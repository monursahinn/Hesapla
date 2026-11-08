import { Button, Center, Stack, Text, Title } from "@mantine/core";
import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <Center mih="60vh">
      <Stack align="center" gap="sm">
        <Title order={2}>Sayfa bulunamadı</Title>
        <Text c="dimmed">Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.</Text>
        <Button component={Link} to="/" variant="light">
          Panoya dön
        </Button>
      </Stack>
    </Center>
  );
}
