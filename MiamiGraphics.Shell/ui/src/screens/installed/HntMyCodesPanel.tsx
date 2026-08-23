import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ChevronDown, Copy, Check, Loader2, Clock, Trash2,
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { bridge } from '@/bridge';
import { ConfirmModal } from '@/components/ConfirmModal';
import type { HntCode } from '@/bridge/types';

interface Props {
  userId: string;
  refreshKey: number;
}

export function HntMyCodesPanel({ userId, refreshKey }: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [codes, setCodes] = useState<HntCode[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copiedCode, setCopiedCode] = useState<string | null>(null);

  const [deletingCode, setDeletingCode] = useState<string | null>(null);
  const [confirmCode, setConfirmCode] = useState<string | null>(null);
  const [rowError, setRowError] = useState<{ code: string; message: string } | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const r = await bridge.hntCodeListMy(userId);
        if (!cancelled) setCodes(r);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [open, userId, refreshKey]);

  useEffect(() => {
    if (!open) setCodes(null);
  }, [refreshKey, open]);

  const requestDelete = (code: string) => {
    if (deletingCode) return;
    setConfirmCode(code);
  };

  const doDelete = async () => {
    const code = confirmCode;
    if (!code) return;
    setConfirmCode(null);
    setDeletingCode(code);
    setRowError(null);
    try
    {
      await bridge.hntCodeDelete(code, userId);
      setCodes(prev => (prev ?? []).filter(c => c.code !== code));
    }
    catch (e)
    {
      const msg = (e as Error).message;
      let friendly = msg;
      if (msg.includes('HNT_CODE_NOT_FOUND')) friendly = t('myCodes.deleteErrorNotFound');
      else if (msg.includes('HNT_CODE_FORBIDDEN')) friendly = t('myCodes.deleteErrorForbidden');
      setRowError({ code, message: friendly });
    }
    finally { setDeletingCode(null); }
  };

  const onCopy = async (code: string) => {
    try {
      await navigator.clipboard.writeText(code);
      setCopiedCode(code);
      setTimeout(() => setCopiedCode(null), 2200);
    } catch {

      const ta = document.createElement('textarea');
      ta.value = code;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); setCopiedCode(code); setTimeout(() => setCopiedCode(null), 2200); }
      finally { document.body.removeChild(ta); }
    }
  };

  const count = codes?.length ?? 0;

  return (
    <div className="rounded-2xl bg-white/[0.03] border border-white/[0.06] overflow-hidden
                    hover:border-white/[0.10] transition-colors">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        style={{ outline: 'none' }}
        className="w-full px-5 py-4 flex items-center justify-between gap-4
                   hover:bg-white/[0.02] transition-colors duration-200"
      >
        <div className="flex flex-col text-left min-w-0">
          <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
            {t('myCodes.title')}
          </div>
          <div className="font-display font-bold text-base text-text-primary truncate">
            {t('myCodes.headerStatic', 'Свои HNT-коды')}
          </div>
        </div>
        <div className="flex items-center gap-2 text-text-muted">
          {codes !== null && count > 0 && (
            <span className="px-2 py-0.5 rounded-md bg-accent/15 text-accent text-xs font-bold tabular-nums">
              {count}
            </span>
          )}
          <motion.span
            animate={{ rotate: open ? 180 : 0 }}
            transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
            className="inline-flex"
          >
            <ChevronDown size={16} />
          </motion.span>
        </div>
      </button>

      <div
        className="grid transition-[grid-template-rows] duration-300 ease-out"
        style={{ gridTemplateRows: open ? '1fr' : '0fr' }}
      >
        <div className="overflow-hidden">
          <div className="px-5 pb-5 pt-1">
            {loading && (
              <div className="py-6 flex items-center justify-center gap-2 text-text-muted text-sm">
                <Loader2 size={16} className="animate-spin" />
                <span>{t('myCodes.loading')}</span>
              </div>
            )}
            {error && (
              <div className="py-4 text-sm text-status-error">{error}</div>
            )}
            {!loading && !error && codes !== null && codes.length === 0 && (
              <div className="py-6 text-center text-text-muted text-sm">
                {t('myCodes.empty')}
              </div>
            )}
            {!loading && !error && codes !== null && codes.length > 0 && (
              <div className="flex flex-col gap-1.5">
                {codes.map(c => (
                  <CodeRow
                    key={c.code}
                    code={c}
                    copied={copiedCode === c.code}
                    deleting={deletingCode === c.code}
                    rowError={rowError?.code === c.code ? rowError.message : null}
                    onCopy={() => void onCopy(c.code)}
                    onDeleteClick={() => requestDelete(c.code)}
                  />
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      <ConfirmModal
        open={!!confirmCode}
        title={t('myCodes.deleteConfirmTitle', 'Удалить HNT-код?')}
        message={confirmCode
          ? t('myCodes.deleteConfirmMessage', 'Код {{code}} перестанет работать - установить по нему сборку станет невозможно.', { code: confirmCode })
          : ''}
        confirmLabel={t('myCodes.deleteHint', 'Удалить')}
        cancelLabel={t('common.cancel', 'Отмена')}
        destructive
        onConfirm={() => void doDelete()}
        onCancel={() => setConfirmCode(null)}
      />
    </div>
  );
}

function CodeRow({
  code, copied, deleting, rowError, onCopy, onDeleteClick,
}: {
  code: HntCode;
  copied: boolean;
  deleting: boolean;
  rowError: string | null;
  onCopy: () => void;
  onDeleteClick: () => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="rounded-xl bg-glass border border-glass-border hover:border-accent/30 transition-colors group overflow-hidden">
      <div className="flex items-center gap-3 px-3 py-2.5">
      {}
      <button
        type="button"
        onClick={onCopy}
        title={t('myCodes.copyHint')}
        className="font-mono text-sm font-bold tabular-nums tracking-[0.12em] text-text-primary
                   px-3 py-1.5 rounded-lg bg-bg-elevated border border-glass-border
                   hover:border-accent/60 hover:bg-accent/10 transition-colors
                   inline-flex items-center gap-2"
      >
        <span>{code.code}</span>
        {copied
          ? <Check size={11} className="text-status-success" />
          : <Copy  size={11} className="text-text-muted opacity-0 group-hover:opacity-100 transition-opacity" />}
      </button>

      <div className="flex-1" />

      <div className="shrink-0 flex flex-col items-end text-[10px] tabular-nums">
        <span className="text-text-secondary font-semibold">
          ↓ {code.downloadsCount}
        </span>
        <span className="inline-flex items-center gap-1 text-text-muted">
          <Clock size={9} />
          {new Date(code.createdAt).toLocaleDateString()}
        </span>
      </div>

      {}
      <button
        type="button"
        onClick={(e) => { e.stopPropagation(); onDeleteClick(); }}
        disabled={deleting}
        title={t('myCodes.deleteHint')}
        className={
          'shrink-0 inline-flex items-center justify-center w-7 h-7 rounded-md transition-colors ' +
          'bg-glass border border-glass-border text-text-muted hover:text-status-error hover:bg-red-500/10 hover:border-red-500/40' +
          (deleting ? ' opacity-60 cursor-not-allowed' : '')
        }
      >
        {deleting
          ? <Loader2 size={11} className="animate-spin" />
          : <Trash2 size={11} />}
      </button>
      </div>
      <AnimatePresence initial={false}>
        {rowError && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
            className="px-3 pb-2 text-[10px] text-status-error truncate"
            title={rowError}
          >
            {rowError}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
