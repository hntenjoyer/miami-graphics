import { useState } from 'react';
import { Trash2, Loader2 } from 'lucide-react';
import { ConfirmModal } from '@/components/ConfirmModal';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';

interface Props {
  category: string;
  categoryLabel: string;
  count?: number;
  onAfterWipe: () => Promise<void> | void;
  onToast?: (tone: 'success' | 'error', message: string) => void;
}

export function WipeAllButton({
  category, categoryLabel, count, onAfterWipe, onToast,
}: Props) {
  const role = useSessionStore(s => s.auth?.role) ?? '';
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);

  if (role !== 'AdminL2') return null;

  const onConfirm = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const r = await bridge.adminWipeAll(category);
      onToast?.('success',
        r.failed === 0
          ? `Удалено ${r.deleted} ${categoryLabel}.`
          : `Удалено ${r.deleted} ${categoryLabel}, ошибок: ${r.failed}.`);
      await onAfterWipe();
    } catch (e) {
      onToast?.('error', `Не удалось удалить: ${(e as Error).message}`);
    } finally {
      setBusy(false);
      setOpen(false);
    }
  };

  const countSuffix = count !== undefined && count > 0 ? ` (${count})` : '';

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        disabled={busy || count === 0}
        title={count === 0 ? 'Нет записей для удаления' : `Удалить все ${categoryLabel} (DB + R2)`}
        className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                   bg-red-500/10 border border-red-500/30 text-red-300
                   text-sm font-semibold
                   hover:bg-red-500/20 hover:border-red-500/50
                   disabled:opacity-40 disabled:cursor-not-allowed
                   transition-colors"
        style={{ outline: 'none' }}
      >
        {busy ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
        <span>{busy ? 'Удаляем…' : `Удалить всё${countSuffix}`}</span>
      </button>

      <ConfirmModal
        open={open}
        title={`Удалить ВСЕ ${categoryLabel}?`}
        message={
          `Будут удалены все записи (${count ?? '?'}) из базы данных + связанные ` +
          `файлы на R2 (Cloudflare). Действие нельзя отменить - пострадают пользователи, ` +
          `у которых эти моды установлены или в HNT-кодах.`
        }
        confirmLabel={busy ? 'Удаляем…' : 'Удалить ВСЕ'}
        cancelLabel="Отмена"
        destructive
        onConfirm={() => { void onConfirm(); }}
        onCancel={() => { if (!busy) setOpen(false); }}
      />
    </>
  );
}
