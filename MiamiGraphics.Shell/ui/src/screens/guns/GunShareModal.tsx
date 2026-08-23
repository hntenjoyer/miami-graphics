import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Check, Copy, Loader2, AlertTriangle, Search, X } from 'lucide-react';
import { Modal } from '@/design';
import { bridge } from '@/bridge';
import { useGunpackStore } from '@/store/gunpackStore';
import type { HntCode } from '@/bridge/types';

interface Props {
  userId: string;
  onClose: () => void;
}

export function GunShareModal({ userId, onClose }: Props) {
  const { t } = useTranslation();
  const installedSelectedGuns = useGunpackStore(s => s.installedSelectedGuns);

  const [query, setQuery] = useState('');
  const [picked, setPicked] = useState<Set<string>>(
    () => new Set(installedSelectedGuns.map(g => g.internalName)),
  );
  const [code, setCode] = useState<HntCode | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [generating, setGenerating] = useState(false);
  const [copied, setCopied] = useState(false);

  const mountedRef = useRef(true);
  useEffect(() => { mountedRef.current = true; return () => { mountedRef.current = false; }; }, []);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return installedSelectedGuns;
    return installedSelectedGuns.filter(g =>
      g.displayName.toLowerCase().includes(q)
      || g.internalName.toLowerCase().includes(q)
      || g.gunpackName.toLowerCase().includes(q));
  }, [installedSelectedGuns, query]);

  const toggle = (internalName: string) => {
    setPicked(prev => {
      const next = new Set(prev);
      if (next.has(internalName)) next.delete(internalName);
      else next.add(internalName);
      return next;
    });
  };
  const allPicked = picked.size === installedSelectedGuns.length;
  const toggleAll = () => {
    setPicked(allPicked ? new Set() : new Set(installedSelectedGuns.map(g => g.internalName)));
  };

  const onGenerate = async () => {
    if (generating || code || picked.size === 0) return;
    setGenerating(true);
    try {
      const r = await bridge.hntCodeExport(userId, {
        includeRedux:        false,
        includeGunpack:      false,
        includeSelectedGuns: true,
        includeComponents:   false,
        gunFilter:           [...picked],
      });
      if (mountedRef.current) setCode(r);
    } catch (e) {
      if (mountedRef.current) setError((e as Error).message);
    } finally {
      if (mountedRef.current) setGenerating(false);
    }
  };

  const onCopy = async () => {
    if (!code) return;
    try {
      await navigator.clipboard.writeText(code.code);
      setCopied(true);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = code.code;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); setCopied(true); }
      finally { document.body.removeChild(ta); }
    }
  };
  useEffect(() => {
    if (code) void onCopy();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [code]);

  return (
    <Modal.Root onClose={onClose} closeLabel={t('common.close', 'Закрыть')} maxWidthClassName="max-w-[560px]">
      <Modal.Header icon={Box}>
        <Modal.Title>{t('guns.share.title', 'Поделиться ганами')}</Modal.Title>
        <Modal.Subtitle>
          {t('guns.share.subtitle', 'Друг введёт код в «Установка по HNT-коду» и получит отмеченные пушки.')}
        </Modal.Subtitle>
      </Modal.Header>

      <Modal.Body>
        {!code && !error && (
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <div className="relative flex-1">
                <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none" />
                <input
                  value={query}
                  onChange={e => setQuery(e.target.value)}
                  placeholder={t('guns.share.searchPlaceholder', 'Поиск гана…')}
                  className="w-full h-10 pl-9 pr-8 rounded-xl bg-bg-elevated/55 border border-white/[0.08]
                             text-sm text-text-primary placeholder:text-text-muted
                             outline-none focus:border-white/[0.2] transition-colors"
                />
                {query && (
                  <button type="button" onClick={() => setQuery('')} style={{ outline: 'none' }}
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-text-muted hover:text-text-primary">
                    <X size={13} />
                  </button>
                )}
              </div>
              <button
                type="button"
                onClick={toggleAll}
                style={{ outline: 'none' }}
                className="h-10 px-3.5 rounded-xl bg-bg-elevated/55 border border-white/[0.08]
                           text-[11px] font-bold uppercase tracking-wider text-text-secondary
                           hover:border-white/[0.18] hover:text-text-primary transition-colors shrink-0"
              >
                {allPicked
                  ? t('guns.share.deselectAll', 'Снять все')
                  : t('guns.share.selectAll', 'Выбрать все')}
              </button>
            </div>

            <div className="max-h-[320px] overflow-y-auto flex flex-col gap-1.5 pr-1">
              {filtered.length === 0 ? (
                <p className="text-sm text-text-muted py-6 text-center">{t('guns.share.noResults', 'Ничего не найдено')}</p>
              ) : filtered.map(g => {
                const on = picked.has(g.internalName);
                return (
                  <button
                    key={g.internalName}
                    type="button"
                    onClick={() => toggle(g.internalName)}
                    style={{ outline: 'none' }}
                    className={
                      'w-full flex items-center gap-3 px-3.5 h-11 rounded-xl border transition-colors text-left ' +
                      (on
                        ? 'bg-accent-soft border-accent/40 text-text-primary'
                        : 'bg-white/[0.03] border-white/[0.07] text-text-secondary hover:bg-white/[0.06] hover:border-white/[0.14]')
                    }
                  >
                    <span className={
                      'shrink-0 w-5 h-5 rounded-md border flex items-center justify-center transition-colors ' +
                      (on ? 'bg-accent border-accent text-text-on-accent' : 'bg-transparent border-white/[0.18]')
                    }>
                      {on && <Check size={12} strokeWidth={3} />}
                    </span>
                    <span className="flex-1 min-w-0">
                      <span className="block text-sm font-semibold truncate">{g.displayName}</span>
                      <span className="block text-[10.5px] text-text-muted truncate">{g.gunpackName}</span>
                    </span>
                  </button>
                );
              })}
            </div>

            <div className="flex items-center justify-between gap-3 pt-1">
              <span className="text-[11px] uppercase tracking-wider text-text-muted tabular-nums">
                {t('guns.share.pickedCount', {
                  defaultValue: 'Выбрано: {{picked}} / {{total}}',
                  picked: picked.size,
                  total: installedSelectedGuns.length,
                })}
              </span>
              <button
                type="button"
                onClick={() => void onGenerate()}
                disabled={picked.size === 0 || generating}
                style={{ outline: 'none' }}
                className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                           bg-accent text-text-on-accent font-semibold text-sm
                           hover:bg-accent-hover shadow-glow-accent
                           disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                {generating
                  ? (<><Loader2 size={14} className="animate-spin" /> {t('guns.share.generating', 'Генерируем…')}</>)
                  : t('guns.share.generate', 'Создать код')}
              </button>
            </div>
          </div>
        )}

        {error && (
          <div className="py-5 flex flex-col gap-3">
            <div className="flex items-start gap-2 text-status-error">
              <AlertTriangle size={18} className="shrink-0 mt-0.5" />
              <p className="text-sm">{t('guns.share.error', 'Не удалось создать код.')}</p>
            </div>
            <code className="text-xs px-3 py-2 rounded-lg bg-glass border border-glass-border
                             text-text-secondary font-mono break-all">{error}</code>
          </div>
        )}

        {code && (
          <>
            <div className="flex flex-col items-center gap-2 px-5 py-7 rounded-2xl border border-transparent
                            bg-accent/5 shadow-glow-accent text-center">
              <span className="w-10 h-10 rounded-full flex items-center justify-center
                               bg-accent-soft border border-white/[0.08]">
                {copied ? <Check size={18} className="text-accent" /> : <Copy size={18} className="text-accent" />}
              </span>
              <p className="font-mono text-lg font-bold tracking-[0.12em] text-text-primary">{code.code}</p>
              <p className="text-sm text-text-secondary">
                {copied
                  ? t('guns.share.copied', 'Код скопирован в буфер обмена')
                  : t('guns.share.copying', 'Копирую код…')}
              </p>
              <button
                type="button"
                onClick={() => void onCopy()}
                style={{ outline: 'none' }}
                className="mt-1 text-[11px] uppercase tracking-wider text-text-muted hover:text-accent transition-colors"
              >
                {t('guns.share.copyAgain', 'Скопировать ещё раз')}
              </button>
            </div>
            <button
              type="button"
              onClick={onClose}
              style={{ outline: 'none' }}
              className="self-end inline-flex items-center justify-center gap-2 h-11 px-6 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         transition-colors text-sm font-bold uppercase tracking-wider"
            >
              {t('guns.share.done', 'Готово')}
            </button>
          </>
        )}
      </Modal.Body>
    </Modal.Root>
  );
}
