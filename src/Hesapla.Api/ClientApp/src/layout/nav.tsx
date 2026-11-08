import type { ReactNode } from "react";
import {
  IconBuildingBank,
  IconBuildingStore,
  IconChartBar,
  IconFileInvoice,
  IconLayoutDashboard,
  IconPackage,
  IconSettings,
  IconShieldCheck,
  IconUsersGroup,
} from "@tabler/icons-react";
import type { RoleCode } from "@/auth/AuthProvider";

export interface NavItem {
  label: string;
  path: string;
  icon: ReactNode;
  group: "main" | "records" | "account";
  roles: RoleCode[];
}

const s = { size: 20, stroke: 1.6 };
const FIRM: RoleCode[] = ["AD", "AC", "VW"];

/** Menü; her öğe yalnızca izinli rollerde görünür. */
export const NAV_ITEMS: NavItem[] = [
  { label: "Pano", path: "/", icon: <IconLayoutDashboard {...s} />, group: "main", roles: FIRM },
  { label: "Firmalar", path: "/", icon: <IconBuildingStore {...s} />, group: "main", roles: ["SU"] },
  { label: "Faturalar", path: "/invoices", icon: <IconFileInvoice {...s} />, group: "main", roles: FIRM },
  { label: "Kasa ve Banka", path: "/cash", icon: <IconBuildingBank {...s} />, group: "main", roles: FIRM },
  { label: "Cariler", path: "/customers", icon: <IconUsersGroup {...s} />, group: "records", roles: FIRM },
  { label: "Ürün ve Hizmetler", path: "/products", icon: <IconPackage {...s} />, group: "records", roles: FIRM },
  { label: "Raporlar", path: "/reports", icon: <IconChartBar {...s} />, group: "records", roles: FIRM },
  { label: "Ayarlar", path: "/settings", icon: <IconSettings {...s} />, group: "account", roles: ["SU", "AD", "AC", "VW"] },
  { label: "Denetim Kaydı", path: "/audit", icon: <IconShieldCheck {...s} />, group: "account", roles: ["AD"] },
];

export const GROUP_LABELS: Record<NavItem["group"], string> = {
  main: "Genel",
  records: "Kayıtlar",
  account: "Hesap",
};
