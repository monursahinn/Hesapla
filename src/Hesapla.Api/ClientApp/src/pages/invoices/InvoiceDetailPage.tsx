import { useState } from "react";
import { Alert, Anchor, Button, Card, Grid, Group, Modal, SimpleGrid, Stack, Table, Text, Textarea } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { IconArrowDownLeft, IconArrowUpRight, IconBan, IconEdit, IconFileTypePdf } from "@tabler/icons-react";
import { Link, useNavigate, useParams } from "react-router";
import { api } from "@/api/client";
import type { Invoice } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { MovementModal, type MovementPreset } from "@/components/MovementModal";
import { PageHeader } from "@/components/PageHeader";
import { PageError, PageLoader } from "@/components/PageLoader";
import { downloadFile } from "@/components/Shared";
import { StatusBadge } from "@/components/StatusBadge";
import { date, money } from "@/lib/format";
import { useApiMutation } from "@/lib/mutation";
import { notify } from "@/lib/notify";

function CancelModal({ invoice, opened, onClose }: { invoice: Invoice; opened: boolean; onClose: () => void }) {
  const [reason, setReason] = useState("");
  const cancel = useApiMutation<void>({
    mutationFn: () => api.post(`/api/invoices/${invoice.id}/cancel`, { reason: reason.trim() }),
    invalidate: [["invoices"], ["customers"], ["products"], ["dashboard"], ["reports"]],
    onSuccess: onClose,
  });
  return (
    <Modal opened={opened} onClose={onClose} title={`${invoice.documentNo} iptal edilsin mi?`}>
      <Stack>
        <Text size="sm">Fatura silinmez; iptal olarak işaretlenir ve bakiye, stok ve raporlardan düşer. Bu işlem geri alınamaz.</Text>
        <Textarea label="İptal nedeni" autosize minRows={2} maxLength={255} value={reason} onChange={(e) => setReason(e.currentTarget.value)} data-autofocus />
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            Vazgeç
          </Button>
          <Button color="red" disabled={!reason.trim()} loading={cancel.isPending} onClick={() => cancel.mutate()}>
            İptal et
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

export default function InvoiceDetailPage() {
  const id = Number(useParams().id);
  const { canWrite } = useAuth();
  const navigate = useNavigate();
  const [movement, setMovement] = useState<MovementPreset | null>(null);
  const [cancelling, setCancelling] = useState(false);

  const q = useQuery({ queryKey: ["invoices", id], queryFn: () => api.get<Invoice>(`/api/invoices/${id}`) });
  if (q.isPending) return <PageLoader />;
  if (q.isError) return <PageError error={q.error} onRetry={() => q.refetch()} />;

  const inv = q.data!;
  const sale = inv.type === "SA";
  const activePayments = inv.payments.filter((p) => !p.cancelled);
  const status = inv.cancelled ? "IP" : inv.remaining <= 0 ? "OD" : new Date(inv.dueDate) < new Date(new Date().toDateString()) ? "GE" : inv.paid > 0 ? "KO" : "BE";

  return (
    <>
      <PageHeader
        title={`${sale ? "Satış" : "Alış"} faturası ${inv.documentNo}`}
        description={
          <Group gap="xs">
            <Anchor component={Link} to={`/customers/${inv.customerId}`}>
              {inv.customerTitle}
            </Anchor>
            <StatusBadge code={status} />
          </Group>
        }
        actions={
          <>
            <Button variant="default" leftSection={<IconFileTypePdf size={16} />} onClick={() => downloadFile(`/api/invoices/${id}/pdf`).catch((e) => notify.error(e.message))}>
              PDF
            </Button>
            {canWrite && !inv.cancelled && inv.remaining > 0 && (
              <Button
                leftSection={sale ? <IconArrowDownLeft size={16} /> : <IconArrowUpRight size={16} />}
                onClick={() => setMovement({ type: sale ? "TA" : "OD", customerId: inv.customerId, invoiceId: inv.id, amount: inv.remaining, lockType: true })}
              >
                {sale ? "Tahsilat ekle" : "Ödeme ekle"}
              </Button>
            )}
            {canWrite && !inv.cancelled && activePayments.length === 0 && (
              <>
                <Button variant="default" leftSection={<IconEdit size={16} />} onClick={() => navigate(`/invoices/${id}/edit`)}>
                  Düzenle
                </Button>
                <Button variant="subtle" color="red" leftSection={<IconBan size={16} />} onClick={() => setCancelling(true)}>
                  İptal et
                </Button>
              </>
            )}
          </>
        }
      />

      {inv.cancelled && (
        <Alert color="gray" variant="light" mb="md" title="Bu fatura iptal edildi">
          {inv.cancelReason}
        </Alert>
      )}

      <Grid gap="md">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <Card>
            <SimpleGrid cols={{ base: 2, sm: 4 }} mb="md">
              <div>
                <Text size="xs" c="dimmed">Tarih</Text>
                <Text size="sm">{date(inv.date)}</Text>
              </div>
              <div>
                <Text size="xs" c="dimmed">Vade</Text>
                <Text size="sm">{date(inv.dueDate)}</Text>
              </div>
              <div>
                <Text size="xs" c="dimmed">Vergi no</Text>
                <Text size="sm">{inv.customerTaxNumber ?? "—"}</Text>
              </div>
              <div>
                <Text size="xs" c="dimmed">Vergi dairesi</Text>
                <Text size="sm">{inv.customerTaxOffice ?? "—"}</Text>
              </div>
            </SimpleGrid>
            <Table.ScrollContainer minWidth={640}>
              <Table>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Açıklama</Table.Th>
                    <Table.Th ta="right">Miktar</Table.Th>
                    <Table.Th ta="right">Birim fiyat</Table.Th>
                    <Table.Th ta="right">İsk.</Table.Th>
                    <Table.Th ta="right">KDV</Table.Th>
                    <Table.Th ta="right">Tutar</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {inv.lines.map((l) => (
                    <Table.Tr key={l.order}>
                      <Table.Td>{l.description}</Table.Td>
                      <Table.Td ta="right">{l.quantity.toLocaleString("tr-TR")} {l.unit}</Table.Td>
                      <Table.Td ta="right">{money(l.unitPrice)}</Table.Td>
                      <Table.Td ta="right">{l.discountRate ? `%${l.discountRate}` : "—"}</Table.Td>
                      <Table.Td ta="right">%{l.vatRate}</Table.Td>
                      <Table.Td ta="right">{money(l.amount)}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
            {inv.description && (
              <Text size="sm" c="dimmed" mt="md">
                {inv.description}
              </Text>
            )}
          </Card>
        </Grid.Col>

        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack>
            <Card>
              <Stack gap={6}>
                {inv.discountTotal > 0 && <Row label="Toplam iskonto" value={money(inv.discountTotal)} />}
                <Row label="Ara toplam" value={money(inv.subtotal)} />
                {inv.vatBreakdown.map((v) => (
                  <Row key={v.rate} label={`KDV %${v.rate}`} value={money(v.vat)} />
                ))}
                <Row label="Genel toplam" value={money(inv.total)} strong />
                {!inv.cancelled && (
                  <>
                    <Row label={sale ? "Tahsil edilen" : "Ödenen"} value={money(inv.paid)} />
                    <Row label="Kalan" value={money(inv.remaining)} strong color={inv.remaining > 0 ? "red" : "teal"} />
                  </>
                )}
              </Stack>
            </Card>
            <Card>
              <Text fw={600} mb="sm">
                {sale ? "Tahsilatlar" : "Ödemeler"}
              </Text>
              {inv.payments.length === 0 ? (
                <Text size="sm" c="dimmed">Henüz bağlı kayıt yok.</Text>
              ) : (
                <Stack gap={8}>
                  {inv.payments.map((p) => (
                    <Group key={p.id} justify="space-between" wrap="nowrap" opacity={p.cancelled ? 0.5 : 1}>
                      <div>
                        <Text size="sm" td={p.cancelled ? "line-through" : undefined}>{date(p.date)} • {p.accountName}</Text>
                        {p.cancelled && <Text size="xs" c="dimmed">İptal: {p.cancelReason}</Text>}
                      </div>
                      <Text size="sm" fw={600} className="num">{money(p.amount)}</Text>
                    </Group>
                  ))}
                </Stack>
              )}
            </Card>
          </Stack>
        </Grid.Col>
      </Grid>

      <MovementModal preset={movement} onClose={() => setMovement(null)} />
      <CancelModal invoice={inv} opened={cancelling} onClose={() => setCancelling(false)} />
    </>
  );
}

function Row({ label, value, strong, color }: { label: string; value: string; strong?: boolean; color?: string }) {
  return (
    <Group justify="space-between">
      <Text size="sm" fw={strong ? 700 : undefined} c={strong ? undefined : "dimmed"}>
        {label}
      </Text>
      <Text size="sm" fw={strong ? 700 : 500} c={color} className="num">
        {value}
      </Text>
    </Group>
  );
}
