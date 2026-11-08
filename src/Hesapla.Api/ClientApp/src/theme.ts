import { createTheme, type CSSVariablesResolver, type MantineColorsTuple } from "@mantine/core";

/** Marka rengi (logo ve mobil uygulamayla aynı çivit mavisi: #4F46E5). */
const brand: MantineColorsTuple = [
  "#eef0ff",
  "#dcdffd",
  "#b7bcf7",
  "#9097f1",
  "#6f77ec",
  "#5a62e9",
  "#4f46e5",
  "#4239c9",
  "#3730a3",
  "#2b257f",
];

/** Kum teması vurgusu (toprak / bakır tonları). */
const sand: MantineColorsTuple = [
  "#fbf6ee",
  "#f2e8d6",
  "#e6d2ad",
  "#d9bb82",
  "#cea75d",
  "#c79a45",
  "#b98a35",
  "#a0742a",
  "#8a6322",
  "#745219",
];

/** Koyu tema yüzeyleri (Mantine "dark" paleti: 0 metin … 7 kart … 9 en koyu). */
const darkSlate: MantineColorsTuple = [
  "#e5e7eb",
  "#c1c6cf",
  "#9aa3b2",
  "#6b7587",
  "#3a4456",
  "#2b3445",
  "#1c2331",
  "#151a24",
  "#0d121b",
  "#070b12",
];

/** Lacivert tema yüzeyleri. */
const darkNavy: MantineColorsTuple = [
  "#e3e8f6",
  "#bcc6e3",
  "#93a1cc",
  "#6677ad",
  "#3a4a80",
  "#2c3a6b",
  "#1f2b55",
  "#172146",
  "#101836",
  "#0a1028",
];

export interface ThemeDef {
  value: string;
  label: string;
  scheme: "dark" | "light";
  primary: string;
  /** Ayarlar sayfasındaki örnek renkler: [arka plan, vurgu]. */
  swatch: [string, string];
  /** Koyu temalarda yüzey paleti. */
  dark?: MantineColorsTuple;
  /** Sayfa zemini, kart/üst çubuk yüzeyi, kenar menü, kenarlık. */
  surfaces: { page: string; body: string; nav: string; border: string };
}

export const THEMES: ThemeDef[] = [
  {
    value: "Dark",
    label: "Koyu",
    scheme: "dark",
    primary: "brand",
    swatch: ["#0d121b", "#6f77ec"],
    dark: darkSlate,
    surfaces: { page: "#0d121b", body: "#151a24", nav: "#111620", border: "#2b3445" },
  },
  {
    value: "Light",
    label: "Açık",
    scheme: "light",
    primary: "brand",
    swatch: ["#f4f6f9", "#4f46e5"],
    surfaces: { page: "#f4f6f9", body: "#ffffff", nav: "#ffffff", border: "#e2e6ec" },
  },
  {
    value: "DarkBlue",
    label: "Lacivert",
    scheme: "dark",
    primary: "blue",
    swatch: ["#101836", "#4dabf7"],
    dark: darkNavy,
    surfaces: { page: "#0c132d", body: "#172146", nav: "#121a3a", border: "#2c3a6b" },
  },
  {
    value: "Turquoise",
    label: "Turkuaz",
    scheme: "light",
    primary: "cyan",
    swatch: ["#e3f8fb", "#0c8599"],
    surfaces: { page: "#e3f8fb", body: "#ffffff", nav: "#f2fcfd", border: "#b9e6ee" },
  },
  {
    value: "Sand",
    label: "Kum",
    scheme: "light",
    primary: "sand",
    swatch: ["#f3eee3", "#a0742a"],
    surfaces: { page: "#f3eee3", body: "#fffdf8", nav: "#faf6ee", border: "#e2d9c6" },
  },
  {
    value: "Gray",
    label: "Gri",
    scheme: "light",
    primary: "gray",
    swatch: ["#e7eaee", "#495057"],
    surfaces: { page: "#e7eaee", body: "#f8f9fa", nav: "#f1f3f5", border: "#cfd5dc" },
  },
];

/** Bilinmeyen değerler açık temaya düşer; büyük-küçük harf duyarsız. */
export function themeDef(pref: string | undefined): ThemeDef {
  const p = (pref ?? "").toLowerCase();
  return THEMES.find((t) => t.value.toLowerCase() === p) ?? THEMES[1];
}

/** Kullanıcının "Yazı Boyutu" tercihi tüm arayüzü ölçekler (rem tabanlı). */
export const FONT_SCALE: Record<string, number> = { S: 0.92, M: 1, L: 1.08, XL: 1.16 };

/**
 * Kurumsal tasarım sistemi: sakin yüzeyler, tek vurgu rengi, anlamlı yerde durum renkleri; tutarlı köşe,
 * gölge ve yazı ölçüleri. Bileşen varsayılanları burada; sayfalar yalnızca içerik kurar.
 */
export function buildTheme(def: ThemeDef) {
  return createTheme({
    primaryColor: def.primary,
    primaryShade: { light: 7, dark: def.primary === "brand" ? 5 : 6 },
    colors: { brand, sand, ...(def.dark ? { dark: def.dark } : {}) },
    fontFamily:
      '"Segoe UI Variable Text", "Segoe UI", Inter, system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, "Noto Sans", sans-serif',
    fontFamilyMonospace: '"Cascadia Mono", Consolas, "SFMono-Regular", Menlo, monospace',
    headings: {
      fontFamily:
        '"Segoe UI Variable Display", "Segoe UI", Inter, system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif',
      fontWeight: "650",
      sizes: {
        h1: { fontSize: "1.75rem", lineHeight: "1.25" },
        h2: { fontSize: "1.375rem", lineHeight: "1.3" },
        h3: { fontSize: "1.125rem", lineHeight: "1.35" },
        h4: { fontSize: "1rem", lineHeight: "1.4" },
      },
    },
    radius: { xs: "4px", sm: "6px", md: "8px", lg: "12px", xl: "16px" },
    defaultRadius: "md",
    shadows: {
      xs: "0 1px 2px rgba(16, 24, 40, .05)",
      sm: "0 1px 3px rgba(16, 24, 40, .08), 0 1px 2px rgba(16, 24, 40, .04)",
      md: "0 4px 12px rgba(16, 24, 40, .08), 0 2px 4px rgba(16, 24, 40, .04)",
      lg: "0 12px 28px rgba(16, 24, 40, .12), 0 4px 8px rgba(16, 24, 40, .06)",
      xl: "0 24px 48px rgba(16, 24, 40, .18)",
    },
    cursorType: "pointer",
    focusRing: "auto",
    components: {
      Card: { defaultProps: { withBorder: true, radius: "lg", padding: "lg", shadow: "xs" } },
      Paper: { defaultProps: { radius: "lg" } },
      Button: { defaultProps: { radius: "md" }, styles: { root: { fontWeight: 600 } } },
      ActionIcon: { defaultProps: { radius: "md" } },
      Modal: {
        defaultProps: { radius: "lg", centered: true, overlayProps: { backgroundOpacity: 0.5, blur: 2 } },
        styles: { title: { fontWeight: 650, fontSize: "var(--mantine-font-size-lg)" } },
      },
      Drawer: { styles: { title: { fontWeight: 650, fontSize: "var(--mantine-font-size-lg)" } } },
      TextInput: { defaultProps: { radius: "md" } },
      Select: { defaultProps: { radius: "md", allowDeselect: false, comboboxProps: { withinPortal: true, shadow: "md" } } },
      InputWrapper: { styles: { label: { fontWeight: 500, marginBottom: 4 } } },
      Table: {
        defaultProps: { verticalSpacing: "sm", horizontalSpacing: "md", highlightOnHover: true },
        styles: {
          th: {
            fontSize: "var(--mantine-font-size-xs)",
            fontWeight: 600,
            textTransform: "uppercase",
            letterSpacing: "0.04em",
            color: "var(--mantine-color-dimmed)",
          },
        },
      },
      Badge: {
        defaultProps: { radius: "sm", variant: "light", size: "md" },
        styles: { root: { textTransform: "none", letterSpacing: 0, fontWeight: 600 } },
      },
      Tooltip: { defaultProps: { withArrow: true, openDelay: 300 } },
      Notification: {
        defaultProps: { withBorder: true, radius: "md" },
        styles: {
          root: { boxShadow: "var(--mantine-shadow-lg)", paddingBlock: 12 },
          title: { fontWeight: 650 },
          description: { color: "var(--mantine-color-text)", opacity: 0.85 },
        },
      },
      Menu: { defaultProps: { shadow: "md", radius: "md" } },
      Popover: { defaultProps: { shadow: "md", radius: "md" } },
      Tabs: { styles: { tab: { fontWeight: 500 } } },
      Skeleton: { defaultProps: { radius: "md" } },
    },
  });
}

/** Tema yüzey renkleri CSS değişkenleri olarak (styles.css ve AppShell bunları kullanır). */
export function cssResolver(def: ThemeDef): CSSVariablesResolver {
  return () => {
    const s = def.surfaces;
    const vars = {
      "--mantine-color-body": s.body,
      "--mantine-color-default-border": s.border,
      "--asy-page-bg": s.page,
      "--asy-nav-bg": s.nav,
    };
    return { variables: {}, light: def.scheme === "light" ? vars : {}, dark: def.scheme === "dark" ? vars : {} };
  };
}
