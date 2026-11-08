import { Badge } from "@mantine/core";

/** Durum kodu → renkli rozet (kodlar sunucudaki fatura durumlarıyla aynıdır); metin gelmezse koddan üretilir. */
const MAP: Record<string, { text: string; color: string }> = {
  // Fatura ödeme durumu
  BE: { text: "Bekliyor", color: "yellow" },
  KO: { text: "Kısmi ödendi", color: "blue" },
  OD: { text: "Ödendi", color: "teal" },
  GE: { text: "Vadesi geçti", color: "red" },
  IP: { text: "İptal", color: "gray" },
};

const BY_TEXT: Record<string, string> = Object.fromEntries(
  Object.values(MAP).map((v) => [v.text.toLocaleLowerCase("tr-TR"), v.color]),
);

interface Props {
  code?: string | null;
  text?: string | null;
  /** Aynı kodun farklı modüldeki anlamı için metni zorla */
  color?: string;
}

export function StatusBadge({ code, text, color }: Props) {
  const entry = code ? MAP[code.toUpperCase()] : undefined;
  const label = text || entry?.text || code || "—";
  const c = color ?? entry?.color ?? BY_TEXT[label.toLocaleLowerCase("tr-TR")] ?? "gray";

  return (
    <Badge color={c} variant="light" style={{ whiteSpace: "nowrap" }}>
      {label}
    </Badge>
  );
}
