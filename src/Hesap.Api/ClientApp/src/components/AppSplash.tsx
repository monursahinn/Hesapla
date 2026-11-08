import { Center, Image, Stack } from "@mantine/core";

/** Oturum bilgisi gelene kadar tam ekran açılış görünümü (index.html'deki ilk ekranla aynı). */
export function AppSplash() {
  return (
    <Center mih="100dvh" role="status" aria-label="Yükleniyor">
      <Stack align="center" gap={14}>
        <Image src="/app/brand/logo.svg" w={64} h={64} alt="" />
        <div className="asy-splash-bar" />
      </Stack>
    </Center>
  );
}
