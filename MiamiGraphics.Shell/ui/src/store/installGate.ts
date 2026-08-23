import { useBackupStore } from '@/store/backupStore';
import { useGlobalToastStore } from '@/store/globalToastStore';

export function ensureBackupOrGate(): boolean {
  const ok = useBackupStore.getState().ensureBackupOrGate();
  if (!ok) {
    useGlobalToastStore.getState().push(
      'warning',
      'Сначала подготовка чистых файлов - открываю экран подготовки.');
  }
  return ok;
}
