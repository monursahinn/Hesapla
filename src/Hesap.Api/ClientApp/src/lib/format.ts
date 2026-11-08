import dayjs from "dayjs";
import "dayjs/locale/tr";
import relativeTime from "dayjs/plugin/relativeTime";
import utc from "dayjs/plugin/utc";

dayjs.extend(relativeTime);
dayjs.extend(utc);
dayjs.locale("tr");

const moneyFmt = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 2 });
const numberFmt = new Intl.NumberFormat("tr-TR");

/** -0 ve yuvarlanınca sıfıra inen küçük negatifler "-₺0,00" görünmesin. */
export const money = (v: number | null | undefined) => moneyFmt.format(Math.round((v ?? 0) * 100) / 100 || 0);
export const num = (v: number | null | undefined) => numberFmt.format(v ?? 0);

/** Sunucu tarihleri yerel saat (Türkiye) olarak gelir; "Z" ile biten UTC değerler yerel saate çevrilir. */
export const date = (v: string | Date | null | undefined, fmt = "DD.MM.YYYY") =>
  v ? dayjs(v).format(fmt) : "—";

export const dateTime = (v: string | Date | null | undefined) => date(v, "DD.MM.YYYY HH:mm");

export const fromNow = (v: string | Date | null | undefined) => (v ? dayjs(v).fromNow() : "");

/** "ahmet YILMAZ" → "Ahmet Yılmaz" (Türkçe büyük/küçük harf kurallarıyla). */
export const titleCase = (s: string | null | undefined) =>
  (s ?? "")
    .toLocaleLowerCase("tr-TR")
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w.charAt(0).toLocaleUpperCase("tr-TR") + w.slice(1))
    .join(" ");

export const initials = (s: string | null | undefined) =>
  (s ?? "")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w.charAt(0).toLocaleUpperCase("tr-TR"))
    .join("");
