import { createContext, useCallback, useContext, useEffect, useMemo, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, setUnauthorizedHandler } from "@/api/client";

/** SU platform yöneticisi, AD firma yöneticisi, AC muhasebe, VW yalnızca görüntüleme. */
export type RoleCode = "SU" | "AD" | "AC" | "VW";

export interface FirmSummary {
  id: number;
  name: string;
  plan: "DE" | "ST" | "PR";
  subscriptionEnd: string;
  daysLeft: number;
  expired: boolean;
  suspended: boolean;
}

export interface Me {
  userId: number;
  fullName: string;
  email: string;
  role: RoleCode;
  roleLabel: string;
  firm: FirmSummary | null;
  appName: string;
}

interface AuthContextValue {
  me: Me | null;
  loading: boolean;
  hasRole: (...roles: RoleCode[]) => boolean;
  /** Kayıt girebilir mi (yönetici/muhasebe ve abonelik geçerli). */
  canWrite: boolean;
  login: (email: string, password: string) => Promise<void>;
  setSession: (me: Me) => void;
  logout: () => Promise<void>;
  refresh: () => Promise<unknown>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export const ME_KEY = ["auth", "me"] as const;

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();

  const meQuery = useQuery({
    queryKey: ME_KEY,
    queryFn: async () => {
      try {
        return await api.get<Me>("/api/auth/me");
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }
    },
    staleTime: 5 * 60_000,
    retry: false,
  });

  // Herhangi bir istek 401 dönerse oturum düşmüş demektir: kullanıcı bilgisini temizle (giriş ekranı açılır).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      if (qc.getQueryData(ME_KEY)) qc.setQueryData(ME_KEY, null);
    });
    return () => setUnauthorizedHandler(null);
  }, [qc]);

  const setSession = useCallback(
    (me: Me) => {
      // Önceki kullanıcının verileri kalmasın.
      qc.removeQueries({ predicate: (q) => q.queryKey[0] !== ME_KEY[0] });
      qc.setQueryData(ME_KEY, me);
    },
    [qc],
  );

  const login = useCallback(
    async (email: string, password: string) => {
      const r = await api.raw<Me>("/api/auth/login", { method: "POST", body: { email, password }, skipAuthRedirect: true });
      setSession(r.data!);
    },
    [setSession],
  );

  const logout = useCallback(async () => {
    try {
      await api.post("/api/auth/logout");
    } finally {
      qc.setQueryData(ME_KEY, null);
      qc.removeQueries({ predicate: (q) => q.queryKey[0] !== ME_KEY[0] });
    }
  }, [qc]);

  const value = useMemo<AuthContextValue>(() => {
    const me = meQuery.data ?? null;
    const writer = !!me && (me.role === "AD" || me.role === "AC");
    return {
      me,
      loading: meQuery.isPending,
      hasRole: (...roles) => !!me && roles.includes(me.role),
      canWrite: writer && !!me?.firm && !me.firm.expired && !me.firm.suspended,
      login,
      setSession,
      logout,
      refresh: () => meQuery.refetch(),
    };
  }, [meQuery, login, setSession, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth, AuthProvider içinde kullanılmalı.");
  return ctx;
}
