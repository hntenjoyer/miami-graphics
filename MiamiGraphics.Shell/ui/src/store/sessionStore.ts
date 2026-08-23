import { create } from 'zustand';
import type { AuthResult, SystemInfo, UserProfile } from '@/bridge/types';
import { bridge } from '@/bridge';
import { ADMIN_BUILD } from '@/buildFlags';

const SESSION_KEY = 'hg.session';

function persistAuth(auth: AuthResult | null) {
  try {
    if (auth) localStorage.setItem(SESSION_KEY, JSON.stringify(auth));
    else      localStorage.removeItem(SESSION_KEY);
  } catch {  }
}

function loadStoredAuth(): AuthResult | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (typeof parsed?.token !== 'string' || typeof parsed?.role !== 'string') return null;
    return parsed as AuthResult;
  } catch { return null; }
}

export type BetaStatus = 'unknown' | 'checking' | 'ok' | 'need_code' | 'denied';

interface SessionState {
  auth: AuthResult | null;
  profile: UserProfile | null;
  systemInfo: SystemInfo | null;
  systemInfoLoading: boolean;

  beta: BetaStatus;
  betaError: string | null;
  zbtEnabled: boolean;
  loadZbtFlag: () => Promise<void>;
  checkBeta: () => Promise<void>;
  redeemBeta: (code: string) => Promise<boolean>;

  loginAsGuest: () => Promise<void>;
  loginWithCredentials: (login: string, password: string, remember?: boolean) => Promise<void>;

  registerRequest: (email: string, username: string, password: string) => Promise<void>;

  registerConfirm: (email: string, code: string, remember?: boolean) => Promise<void>;

  logout: () => void;

  refreshProfile: () => Promise<void>;
  setProfile: (p: UserProfile | null) => void;

  loadSystemInfo: () => Promise<void>;
  updateGtaPath: (path: string) => Promise<void>;
}

const userIdFromAuth = (auth: AuthResult | null): string | null =>
  auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

async function ensureServerOnline(): Promise<void> {
  try {
    const status = await bridge.getServerStatus();
    if (!status.reachable) {
      throw new Error(status.message || 'Сервер недоступен. Проверьте интернет-соединение.');
    }
    if (!status.provisioned) {
      throw new Error(status.message || 'Сервер доступен, но база данных не готова.');
    }
  } catch (e) {
    const message = e instanceof Error ? e.message : '';
    if (message) throw new Error(message);
    throw new Error('Сервер недоступен. Проверьте интернет-соединение.');
  }
}

const initialAuth = loadStoredAuth();

export const useSessionStore = create<SessionState>((set, get) => ({
  auth: initialAuth,
  profile: null,
  systemInfo: null,
  systemInfoLoading: false,
  beta: 'ok',
  betaError: null,
  zbtEnabled: false,

  loadZbtFlag: async () => {
    try {
      await bridge.betaEnabled();
    } catch {}
  },

  checkBeta: async () => {
    if (!get().zbtEnabled) { set({ beta: 'ok', betaError: null }); return; }
    const auth = get().auth;
    if (!auth || auth.role === 'Guest') { set({ beta: 'denied', betaError: 'no_access' }); return; }
    set({ beta: 'checking' });
    try {
      const r = await bridge.betaCheck();
      if (r.ok)                       set({ beta: 'ok',        betaError: null });
      else if (r.error === 'no_access') set({ beta: 'need_code', betaError: null });
      else                            set({ beta: 'denied',    betaError: r.error });
    } catch { set({ beta: 'denied', betaError: 'network' }); }
  },
  redeemBeta: async (code) => {
    try {
      const r = await bridge.betaRedeem(code);
      if (r.ok) { set({ beta: 'ok', betaError: null }); return true; }
      set({ betaError: r.error });
      return false;
    } catch { set({ betaError: 'network' }); return false; }
  },

  loginAsGuest: async () => {
    const auth = await bridge.authenticateGuest();
    persistAuth(null);
    set({ auth, profile: null });
  },
  loginWithCredentials: async (login, password, remember = false) => {
    await ensureServerOnline();
    const auth = await bridge.authenticateUser(login, password, null);
    set({ auth, beta: 'unknown' });
    if (remember) persistAuth(auth);
    else          persistAuth(null);
    void get().refreshProfile();
    void get().checkBeta();
  },
  registerRequest: async (email, username, password) => {

    await ensureServerOnline();
    await bridge.registerRequest(email, username, password);
  },
  registerConfirm: async (email, code, remember = false) => {
    await ensureServerOnline();
    const auth = await bridge.registerConfirm(email, code);
    set({ auth, beta: 'unknown' });
    if (remember) persistAuth(auth);
    else          persistAuth(null);
    void get().refreshProfile();
  },
  logout: () => {
    persistAuth(null);
    set({ auth: null, profile: null, beta: 'unknown', betaError: null });
  },

  refreshProfile: async () => {
    const userId = userIdFromAuth(get().auth);
    if (!userId) { set({ profile: null }); return; }
    try {
      const p = await bridge.getUserProfile(userId);
      set({ profile: p });
    } catch (e) {
      console.warn('[session] refreshProfile failed', e);
    }
  },
  setProfile: (p) => set({ profile: p }),

  loadSystemInfo: async () => {
    set({ systemInfoLoading: true });
    try {
      const info = await bridge.getSystemInfo();
      set({ systemInfo: info, systemInfoLoading: false });
    } catch (e) {
      console.warn('[session] loadSystemInfo failed, falling back to notfound', e);
      set({
        systemInfo: { gtaPath: null, gpuName: '', gtaExeVersion: null, isGtaFound: false },
        systemInfoLoading: false,
      });
    }
  },
  updateGtaPath: async (path) => {
    const isValid = await bridge.validateGtaPath(path);
    if (!isValid) throw new Error('Invalid GTA path');
    const current = get().systemInfo;
    if (!current) {
      set({ systemInfo: { gtaPath: path, gpuName: '', gtaExeVersion: null, isGtaFound: true } });
      return;
    }
    set({ systemInfo: { ...current, gtaPath: path, gtaExeVersion: null, isGtaFound: true } });
  },
}));

export const selectIsTester = (s: { auth: AuthResult | null }): boolean =>
  s.auth?.tester === true;

export const useIsTester = (): boolean => useSessionStore(selectIsTester);

const selectIsStaff = (s: { auth: AuthResult | null }): boolean =>
  s.auth?.role === 'Moderator' || s.auth?.role === 'AdminL1' || s.auth?.role === 'AdminL2';

export const useCanSeeTesterFeature = (): boolean =>
  useSessionStore(s => ADMIN_BUILD || selectIsTester(s) || selectIsStaff(s));

export const canSeeTesterFeature = (): boolean =>
  ADMIN_BUILD || selectIsTester(useSessionStore.getState()) || selectIsStaff(useSessionStore.getState());

export const useCanSeeCustomGuns = (): boolean => true;

Promise.resolve().then(() => useSessionStore.getState().loadZbtFlag());

if (initialAuth && initialAuth.role !== 'Guest') {
  Promise.resolve().then(() => {
    useSessionStore.getState().refreshProfile();
    useSessionStore.getState().checkBeta();
  });
}
