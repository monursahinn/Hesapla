import { useState } from "react";
import { Card, Text } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { Paged } from "@/api/types";
import { DataTable } from "@/components/DataTable";
import { PageHeader } from "@/components/PageHeader";
import { PageError } from "@/components/PageLoader";
import { Pager } from "@/components/Shared";
import { dateTime } from "@/lib/format";

interface AuditRow { id: number; date: string; userName?: string | null; action: string; targetType?: string | null; targetId?: number | null; description?: string | null; ip?: string | null }

const ACTIONS: Record<string, string> = {
  FIRMA_KAYIT: "Firma kaydı", GIRIS: "Giriş", HESAP_KILITLENDI: "Hesap kilitlendi", SIFRE_SIFIRLANDI: "Şifre sıfırlandı", SIFRE_DEGISTI: "Şifre değişti",
  FIRMA_GUNCELLE: "Firma bilgileri", FIRMA_LOGO: "Logo", KULLANICI_EKLE: "Kullanıcı eklendi", KULLANICI_GUNCELLE: "Kullanıcı güncellendi",
  KULLANICI_KILIT_AC: "Kilit açıldı", CARI_EKLE: "Cari eklendi", CARI_GUNCELLE: "Cari güncellendi", URUN_EKLE: "Ürün eklendi", URUN_GUNCELLE: "Ürün güncellendi",
  HESAP_EKLE: "Kasa/banka eklendi", HESAP_GUNCELLE: "Kasa/banka güncellendi", SATIS_FATURASI: "Satış faturası", ALIS_FATURASI: "Alış faturası",
  FATURA_DUZENLE: "Fatura düzenlendi", FATURA_IPTAL: "Fatura iptal", HAREKET_TA: "Tahsilat", HAREKET_OD: "Ödeme", HAREKET_GI: "Gider", HAREKET_GE: "Gelir",
  HAREKET_VI: "Virman", HAREKET_IPTAL: "Kasa/banka kaydı iptal",
};

export default function AuditPage() {
  const [page, setPage] = useState(1);
  const q = useQuery({ queryKey: ["audit", page], queryFn: () => api.get<Paged<AuditRow>>("/api/audit", { page, pageSize: 50 }), placeholderData: (p) => p });
  return (
    <>
      <PageHeader title="Denetim kaydı" description="Kim, ne zaman, hangi işlemi yaptı" />
      {q.isError && <PageError error={q.error} onRetry={() => q.refetch()} />}
      <Card>
        <DataTable
          rows={q.data?.items}
          loading={q.isFetching}
          rowKey={(r) => r.id}
          columns={[
            { key: "date", header: "Zaman", render: (r) => dateTime(r.date), width: 150 },
            { key: "user", header: "Kullanıcı", render: (r) => r.userName ?? "—" },
            { key: "action", header: "İşlem", render: (r) => ACTIONS[r.action] ?? r.action },
            { key: "desc", header: "Ayrıntı", render: (r) => <Text size="sm" lineClamp={2}>{r.description ?? ""}</Text> },
            { key: "ip", header: "IP", render: (r) => r.ip ?? "", hideOnMobile: true },
          ]}
        />
        {q.data && <Pager page={page} total={q.data.total} pageSize={q.data.pageSize} onChange={setPage} />}
      </Card>
    </>
  );
}
