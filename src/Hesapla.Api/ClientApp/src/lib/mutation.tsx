import { useMutation, useQueryClient, type QueryKey } from "@tanstack/react-query";
import { notify } from "@/lib/notify";
import { modals } from "@mantine/modals";
import { Text } from "@mantine/core";
import { ApiError, type ApiEnvelope } from "@/api/client";

interface Options<TVars, TData> {
  mutationFn: (vars: TVars) => Promise<ApiEnvelope<TData>>;
  /** Başarıda yenilenecek sorgular */
  invalidate?: QueryKey[];
  /** Sunucu mesajı yerine gösterilecek metin */
  successMessage?: string;
  onSuccess?: (data: TData | undefined, vars: TVars) => void;
}

/**
 * Tüm yazma işlemleri için standart davranış: başarıda yeşil, hatada kırmızı bildirim ve ilgili listelerin yenilenmesi.
 */
export function useApiMutation<TVars = void, TData = unknown>({ mutationFn, invalidate, successMessage, onSuccess }: Options<TVars, TData>) {
  const qc = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: async (env, vars) => {
      const msg = successMessage ?? env.message;
      if (msg) notify.success(msg);
      await Promise.all((invalidate ?? []).map((k) => qc.invalidateQueries({ queryKey: k })));
      onSuccess?.(env.data, vars);
    },
    onError: (e) => {
      notify.error(e instanceof ApiError ? e.message : "Beklenmeyen bir hata oluştu.");
    },
  });
}

/** Geri alınamaz işlemler için onay penceresi. */
export function confirmAction(opts: { title: string; message: string; confirmLabel?: string; danger?: boolean; onConfirm: () => void }) {
  modals.openConfirmModal({
    title: opts.title,
    children: <Text size="sm">{opts.message}</Text>,
    labels: { confirm: opts.confirmLabel ?? "Onayla", cancel: "Vazgeç" },
    confirmProps: { color: opts.danger ? "red" : undefined },
    cancelProps: { variant: "default" },
    onConfirm: opts.onConfirm,
  });
}
