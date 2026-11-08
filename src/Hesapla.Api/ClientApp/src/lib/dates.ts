import dayjs from "dayjs";
import customParseFormat from "dayjs/plugin/customParseFormat";

dayjs.extend(customParseFormat);

const FORMATS = ["DD.MM.YYYY", "D.M.YYYY", "DD/MM/YYYY", "D/M/YYYY", "DD-MM-YYYY", "YYYY-MM-DD"];

/**
 * Elle yazılan tarihi ("15.05.1990", "1.5.1990", "1990-05-15") okur; DateInput'un `dateParser`'ı için.
 * Varsayılan ayrıştırıcı "GG.AA.YYYY" biçimini tanımadığından yazılan tarih alandan çıkınca siliniyordu.
 */
export function parseTrDate(value: string): string | null {
  const d = dayjs(value.trim(), FORMATS, true);
  return d.isValid() ? d.format("YYYY-MM-DD") : null;
}

/** Sunucudan gelen "1990-05-15T00:00:00" değerini tarih kutusunun beklediği "1990-05-15" biçimine çevirir. */
export const toDateValue = (v?: string | null) => (v ? v.slice(0, 10) : null);
