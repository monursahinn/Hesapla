/**
 * Hesapla API istemcisi. Sunucu yanıt zarfı: { success, message, data, errors }.
 * Kimlik doğrulama HttpOnly çerezle yapılır; durum değiştiren isteklerde CSRF koruması için
 * X-Requested-With başlığı gönderilir (sunucu bu başlığı /api altında zorunlu tutar).
 */

export interface ApiEnvelope<T> {
  success: boolean;
  message: string;
  data?: T;
  errors?: Record<string, string[]> | null;
}

export class ApiError extends Error {
  readonly status: number;
  readonly errors?: Record<string, string[]> | null;

  constructor(message: string, status: number, errors?: Record<string, string[]> | null) {
    super(message);
    this.status = status;
    this.errors = errors;
  }
}

/** Oturum düştüğünde (401) çağrılır; AuthProvider giriş ekranına yönlendirir. */
let onUnauthorized: (() => void) | null = null;
export const setUnauthorizedHandler = (fn: (() => void) | null) => {
  onUnauthorized = fn;
};

const DEFAULT_MESSAGES: Record<number, string> = {
  400: "İşlem tamamlanamadı.",
  401: "Oturumunuz sona erdi. Lütfen tekrar giriş yapın.",
  403: "Bu işlem için yetkiniz yok.",
  402: "Abonelik süreniz doldu. Yeni kayıt için aboneliğinizi yenileyin.",
  404: "Kayıt bulunamadı.",
  423: "Hesabınız kilitlendi. \"Şifremi unuttum\" ile yeni şifre belirleyin.",
  429: "Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin.",
};

function messageFor(status: number): string {
  if (status >= 500) return "Sunucuda bir hata oluştu. Lütfen daha sonra tekrar deneyin.";
  return DEFAULT_MESSAGES[status] ?? "İşlem tamamlanamadı.";
}

type Query = Record<string, string | number | boolean | null | undefined>;

export function withQuery(path: string, query?: Query): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === "") continue;
    params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `${path}${path.includes("?") ? "&" : "?"}${qs}` : path;
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  query?: Query;
  signal?: AbortSignal;
  /** 401'de oturum düştü sayma (giriş isteği gibi). */
  skipAuthRedirect?: boolean;
}

async function request<T>(path: string, opts: RequestOptions = {}): Promise<ApiEnvelope<T>> {
  const method = opts.method ?? "GET";
  const headers: Record<string, string> = {
    Accept: "application/json",
    "X-Requested-With": "XMLHttpRequest",
  };

  let body: BodyInit | undefined;
  if (opts.body instanceof FormData) {
    body = opts.body;
  } else if (opts.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(opts.body);
  }

  let res: Response;
  try {
    res = await fetch(withQuery(path, opts.query), {
      method,
      headers,
      body,
      credentials: "same-origin",
      signal: opts.signal,
    });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("Sunucuya bağlanılamadı. İnternet bağlantınızı kontrol edin.", 0);
  }

  if (res.status === 401 && !opts.skipAuthRedirect) onUnauthorized?.();

  const text = await res.text();
  let json: unknown = null;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = null;
    }
  }

  const isEnvelope = !!json && typeof json === "object" && !Array.isArray(json) && "success" in (json as object);

  if (!res.ok) {
    const env = isEnvelope ? (json as ApiEnvelope<T>) : null;
    throw new ApiError(env?.message || messageFor(res.status), res.status, env?.errors);
  }

  if (isEnvelope) {
    const env = json as ApiEnvelope<T>;
    if (!env.success) throw new ApiError(env.message || messageFor(res.status), res.status, env.errors);
    return env;
  }

  return { success: true, message: "", data: json as T };
}

export const api = {
  get: async <T>(path: string, query?: Query, signal?: AbortSignal) =>
    (await request<T>(path, { query, signal })).data as T,

  /** Sunucu mesajıyla birlikte döner (bildirim göstermek için). */
  post: <T = unknown>(path: string, body?: unknown, query?: Query) =>
    request<T>(path, { method: "POST", body, query }),

  put: <T = unknown>(path: string, body?: unknown) => request<T>(path, { method: "PUT", body }),

  del: <T = unknown>(path: string, query?: Query) => request<T>(path, { method: "DELETE", query }),

  raw: request,
};
