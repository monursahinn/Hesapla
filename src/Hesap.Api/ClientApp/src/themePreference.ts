import { createContext, useContext } from "react";

export interface ThemePref {
  /** Şu an uygulanan tema (Ayarlar sayfasındaki önizleme dahil). */
  pref: string;
  apply: (pref: string) => void;
}

export const ThemePrefContext = createContext<ThemePref>({ pref: "Dark", apply: () => undefined });

/** Tema tercihini okur / anında uygular (Ayarlar önizlemesi, üst çubuktaki açık-koyu düğmesi). */
export const useThemePreference = () => useContext(ThemePrefContext);
