import { useEffect, useState } from "react";
import { useIsFetching, useIsMutating } from "@tanstack/react-query";

/**
 * Üst çubuğun altında ince ilerleme çizgisi: arka planda veri yüklenirken / kaydedilirken görünür.
 * Kısa istekler (<250 ms) için hiç görünmez; titreşim yapmaz.
 */
export function TopProgress() {
  const busy = useIsFetching() + useIsMutating() > 0;
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const t = setTimeout(() => setVisible(busy), busy ? 250 : 200);
    return () => clearTimeout(t);
  }, [busy]);

  return <div className="asy-top-progress" data-visible={visible || undefined} aria-hidden />;
}
