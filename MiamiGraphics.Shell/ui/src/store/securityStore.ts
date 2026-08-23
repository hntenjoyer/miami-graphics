import { create } from 'zustand';
import type { LegitReport, LegitCheckProgress } from '@/bridge/types';
import { bridge } from '@/bridge';
import i18n from '@/i18n';

export const STAGE_LABELS: Record<LegitCheckProgress['stage'], string> = {
  get manifest() { return i18n.t('security.stage.manifest', 'Читаем манифест мода'); },
  get download() { return i18n.t('security.stage.download', 'Скачиваем файлы мода'); },
  get scan()     { return i18n.t('security.stage.scan',     'Сверяем с чистой GTA'); },
  get done()     { return i18n.t('security.stage.done',     'Готово'); },
};

interface SecurityState {
  running:  boolean;
  progress: LegitCheckProgress | null;
  report:   LegitReport | null;
  error:    string | null;

  preselect: { reduxId: string; name: string } | null;
  setPreselect: (p: { reduxId: string; name: string } | null) => void;

  sharedCode: string | null;
  sharing:    boolean;

  checkRedux:  (reduxId: string, versionId?: string | null) => Promise<void>;
  checkOwnRpf: (rpfPath: string | null) => Promise<void>;
  fetchByCode: (code: string) => Promise<void>;
  share:       (userId: string) => Promise<string | null>;
  reset:       () => void;
}

export const useSecurityStore = create<SecurityState>((set, get) => {
  const onProgress = (p: LegitCheckProgress) => set({ progress: p });

  async function run(job: () => Promise<LegitReport>) {
    if (get().running) return;
    set({ running: true, progress: null, report: null, error: null, sharedCode: null });
    bridge.events.off('legitcheck:progress', onProgress);
    bridge.events.on('legitcheck:progress', onProgress);
    try {
      const report = await job();
      set({ report, running: false, progress: null });
    } catch (e) {
      set({ error: humanizeError(e), running: false, progress: null });
    } finally {
      bridge.events.off('legitcheck:progress', onProgress);
    }
  }

  return {
    running: false,
    progress: null,
    report: null,
    error: null,

    preselect: null,
    setPreselect: (preselect) => set({ preselect }),

    sharedCode: null,
    sharing: false,

    checkRedux: (reduxId, versionId) =>
      run(() => bridge.legitCheckRedux(reduxId, versionId ?? null)),

    checkOwnRpf: (rpfPath) =>
      run(() => bridge.legitCheckUpdateRpf(rpfPath)),

    fetchByCode: async (code) => {
      set({ error: null });
      try {
        const report = await bridge.legitReportFetch(code);
        set({ report, sharedCode: null });
      } catch (e) {
        set({ error: humanizeError(e) });
      }
    },

    share: async (userId) => {
      const { report, sharedCode, sharing } = get();
      if (!report || sharing) return sharedCode;
      if (sharedCode) return sharedCode;
      set({ sharing: true });
      try {
        const code = await bridge.legitReportShare(userId, report);
        set({ sharedCode: code, sharing: false });
        return code;
      } catch (e) {
        set({ error: humanizeError(e), sharing: false });
        return null;
      }
    },

    reset: () => set({
      running: false, progress: null, report: null, error: null, sharedCode: null,
    }),
  };
});

function humanizeError(e: unknown): string {
  const msg = e instanceof Error ? e.message : String(e);
  const t = i18n.t.bind(i18n);
  if (msg.includes('LEGIT_NO_CLEAN'))       return t('security.error.noClean', 'Нет чистой копии update.rpf - сначала сделай Backup.');
  if (msg.includes('LEGIT_NO_GTA'))         return t('security.error.noGta', 'GTA не найдена - укажи путь в Настройках.');
  if (msg.includes('LEGIT_FILE_NOT_FOUND')) return t('security.error.fileNotFound', 'Файл update.rpf не найден.');
  if (msg.includes('LEGIT_OPEN_TARGET'))    return t('security.error.openTarget', 'Не удалось открыть update.rpf - возможно, он зашифрован (NG) или занят игрой. Закрой GTA и Rockstar Launcher и попробуй снова.');
  if (msg.includes('LEGIT_OPEN_CLEAN'))     return t('security.error.openClean', 'Не удалось открыть чистую копию update.rpf. Сделай Backup заново.');
  if (msg.includes('LEGIT_NO_MANIFEST'))    return t('security.error.noManifest', 'У этого мода нет манифеста изменений - проверка невозможна.');
  if (msg.includes('LEGIT_REDUX_NOT_FOUND')) return t('security.error.reduxNotFound', 'Мод не найден в каталоге.');
  if (msg.includes('LGT_AUTH_REQUIRED'))    return t('security.error.authRequired', 'Войди в аккаунт, чтобы отправить отчёт.');
  if (msg.includes('LGT_CODE_NOT_FOUND'))   return t('security.error.codeNotFound', 'Код не найден. Проверь, правильно ли он введён.');
  if (msg.includes('LGT_CODE_EXPIRED'))     return t('security.error.codeExpired', 'Срок действия кода истёк (30 дней без просмотров).');
  if (msg.includes('LGT_CODE_WRONG_KIND'))  return t('security.error.codeWrongKind', 'Это код другого типа (HNT/KNK), а не отчёт проверки.');
  return msg;
}
