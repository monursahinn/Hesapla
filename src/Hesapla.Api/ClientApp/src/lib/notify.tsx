import { notifications } from "@mantine/notifications";
import { IconAlertTriangle, IconCheck, IconInfoCircle, IconX } from "@tabler/icons-react";
import type { ReactNode } from "react";

type Kind = "success" | "error" | "warning" | "info";

const STYLE: Record<Kind, { color: string; icon: ReactNode; title: string; autoClose: number }> = {
  success: { color: "teal", icon: <IconCheck size={18} />, title: "İşlem tamamlandı", autoClose: 4000 },
  error: { color: "red", icon: <IconX size={18} />, title: "İşlem yapılamadı", autoClose: 7000 },
  warning: { color: "orange", icon: <IconAlertTriangle size={18} />, title: "Dikkat", autoClose: 6000 },
  info: { color: "blue", icon: <IconInfoCircle size={18} />, title: "Bilgi", autoClose: 5000 },
};

interface Options {
  title?: string;
  onClick?: () => void;
  autoClose?: number;
}

/** Uygulama genelinde tek tip bildirim (sağ üst, başlık + ikon + kısa açıklama). */
function show(kind: Kind, message: ReactNode, opts?: Options) {
  const s = STYLE[kind];
  notifications.show({
    color: s.color,
    icon: s.icon,
    title: opts?.title ?? s.title,
    message,
    autoClose: opts?.autoClose ?? s.autoClose,
    onClick: opts?.onClick,
    style: opts?.onClick ? { cursor: "pointer" } : undefined,
  });
}

export const notify = {
  success: (message: ReactNode, opts?: Options) => show("success", message, opts),
  error: (message: ReactNode, opts?: Options) => show("error", message, opts),
  warning: (message: ReactNode, opts?: Options) => show("warning", message, opts),
  info: (message: ReactNode, opts?: Options) => show("info", message, opts),
};
