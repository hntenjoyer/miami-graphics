import { useEffect, useMemo, useRef, useState } from 'react';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { X, Crosshair, Slash, Check, Boxes, Eye, Search } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { useGunpackStore, type FlatGun } from '@/store/gunpackStore';
import type { GunSlotState } from '@/store/userBuildsStore';
import { GlbViewerModal } from '../guns/GlbViewerModal';

interface Props {
  open: boolean;
  internalName: string | null;
  displayName: string;
  category:    string;
  basePackId: string;
  current: GunSlotState | null;
  onPickOverride: (gunpackId: string, gunId: string) => void;
  onUseDefault: () => void;
  onUseVanilla: () => void;
  onClose: () => void;
  onOpenGunpack?: (packId: string) => void;
}

export function GunReplaceModal({
  open, internalName, displayName, category,
  basePackId, current,
  onPickOverride, onUseDefault, onUseVanilla, onClose, onOpenGunpack,
}: Props) {
  const { t } = useTranslation();
  const allGuns = useGunpackStore(s => s.allGuns) ?? [];
  const loadingAllGuns = useGunpackStore(s => s.loadingAllGuns);
  const loadAllGuns = useGunpackStore(s => s.loadAllGuns);

  const [glbView, setGlbView] = useState<FlatGun | null>(null);
  const [bigPreview, setBigPreview] = useState<FlatGun | null>(null);
  const [query, setQuery] = useState('');

  useEffect(() => {
    if (open) void loadAllGuns();
  }, [open]);

  useEffect(() => {
    if (!open) { setGlbView(null); setBigPreview(null); setQuery(''); }
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  const candidates = useMemo<FlatGun[]>(() => {
    if (!internalName) return [];
    const matches = allGuns.filter(g => `${g.weaponPrefix}${g.baseName}` === internalName);
    matches.sort((a, b) => {
      if (a.packId === basePackId && b.packId !== basePackId) return -1;
      if (b.packId === basePackId && a.packId !== basePackId) return  1;
      return a.packName.localeCompare(b.packName);
    });
    return matches;
  }, [allGuns, internalName, basePackId]);

  const filtered = useMemo<FlatGun[]>(() => {
    const q = query.trim().toLowerCase();
    if (!q) return candidates;
    return candidates.filter(g =>
      (g.packName ?? '').toLowerCase().includes(q) ||
      (g.displayName ?? '').toLowerCase().includes(q) ||
      (g.baseName ?? '').toLowerCase().includes(q));
  }, [candidates, query]);

  const isCurrent = (gunpackId: string, gunId: string) =>
    current?.kind === 'override' && current.gunpackId === gunpackId && current.gunId === gunId;

  const isDefault = current === null;
  const isVanilla = current?.kind === 'vanilla';

  const grid: Variants = {
    hidden:  { opacity: 0 },
    visible: { opacity: 1, transition: { staggerChildren: 0.045, delayChildren: 0.05 } },
  };
  const cardV: Variants = {
    hidden:  { opacity: 0, y: 10, scale: 0.96 },
    visible: { opacity: 1, y: 0, scale: 1, transition: { duration: 0.32, ease: EASE_DEPTH } },
  };

  return (
    <>
    {}
    {open && (
        <motion.div
          className="fixed inset-0 z-[110] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.22, ease: EASE_DEPTH }}
          onClick={onClose}
          style={{
            background: 'rgba(0, 0, 0, 0.55)',
            backdropFilter: 'blur(28px) saturate(160%)',
            WebkitBackdropFilter: 'blur(28px) saturate(160%)',
          }}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.94, y: 18 }}
            animate={{ opacity: 1, scale: 1,    y: 0 }}
            transition={{ duration: 0.32, ease: EASE_DEPTH }}
            onClick={e => e.stopPropagation()}

            className="w-full max-w-[1280px]"
            style={{
              filter: [
                'drop-shadow(0 1px 0 rgba(255,255,255,0.06))',
                'drop-shadow(0 12px 28px rgba(0,0,0,0.45))',
                'drop-shadow(0 36px 72px rgba(0,0,0,0.55))',
              ].join(' '),
            }}
          >
            <GlassPanel
              depth="z3"
              tint="ultra"
              rounded="3xl"
              className="relative overflow-hidden flex flex-col shadow-glass-inner"
              style={{
                padding: '22px 22px 18px',
                gap: 16,
                height: '86vh',

                boxShadow: [
                  '0 0 0 1px color-mix(in srgb, var(--accent) 22%, transparent)',
                  '0 0 0 6px color-mix(in srgb, var(--accent) 6%, transparent)',
                  '0 32px 80px -12px color-mix(in srgb, var(--accent) 28%, transparent)',
                  '0 12px 32px rgba(0,0,0,0.55)',
                ].join(', '),
              }}
            >
              <button
                type="button"
                onClick={onClose}
                aria-label={t('userBuilds.close', 'Закрыть')}
                className="absolute top-3.5 right-3.5 w-8 h-8 rounded-full flex items-center justify-center
                           text-text-secondary hover:text-text-primary transition-colors duration-150 z-10"
                style={{
                  background: 'rgba(255,255,255,0.08)',
                  backdropFilter: 'blur(20px) saturate(140%)',
                  WebkitBackdropFilter: 'blur(20px) saturate(140%)',
                }}
              >
                <X size={14} strokeWidth={2.5} />
              </button>

              {}
              <header className="flex items-start gap-4 pt-0.5 pb-0.5 pr-10">
                <div className="flex flex-col items-start gap-1 min-w-0 flex-1">
                  <h2 className="text-[20px] font-semibold text-text-primary tracking-tight truncate max-w-full">
                    {displayName}
                  </h2>
                  <p className="text-[12px] text-text-muted leading-relaxed">
                    {t('userBuilds.replaceHint', 'Выберите вариант из любого ганпака или сделайте слот ванильным.')}
                    <span className="mx-1.5 opacity-50">·</span>
                    <span className="text-text-secondary">{category}</span>
                  </p>
                </div>

                <div className="group relative w-full max-w-[280px] shrink-0 self-center">
                  <span
                    aria-hidden
                    className="pointer-events-none absolute inset-x-4 top-0 h-px
                               bg-gradient-to-r from-transparent via-white/25 to-transparent
                               group-focus-within:via-[color-mix(in_srgb,var(--accent)_70%,transparent)]
                               transition-colors duration-300"
                  />
                  <Search
                    size={14}
                    className="absolute left-4 top-1/2 -translate-y-1/2 text-text-muted
                               group-focus-within:text-accent transition-colors duration-300"
                  />
                  <input
                    type="text"
                    value={query}
                    onChange={e => setQuery(e.target.value)}
                    placeholder={t('userBuilds.replaceSearchPlaceholder', 'Поиск по названию')}
                    className="w-full h-10 pl-10 pr-9 rounded-2xl
                               bg-glass-strong border border-glass-border
                               backdrop-blur-glass text-sm text-text-primary
                               placeholder:text-text-muted/70
                               shadow-[inset_0_1px_0_rgba(255,255,255,0.06)]
                               outline-none hover:border-white/20
                               focus:border-accent/60 focus:shadow-glow-accent
                               transition-[border-color,box-shadow] duration-300 ease-depth"
                  />
                  {query && (
                    <button
                      type="button"
                      onClick={() => setQuery('')}
                      aria-label={t('customize.import.clearSearch', 'Очистить поиск')}
                      style={{ outline: 'none' }}
                      className="absolute right-2.5 top-1/2 -translate-y-1/2 w-6 h-6 rounded-lg
                                 flex items-center justify-center text-text-muted
                                 hover:text-text-primary hover:bg-white/10 transition-colors"
                    >
                      <X size={12} />
                    </button>
                  )}
                </div>
              </header>

              <motion.div
                variants={grid}
                initial="hidden"
                animate="visible"
                className="overflow-y-auto -mx-1 px-1 pt-1.5 flex flex-col gap-3 flex-1 min-h-0"
              >
                {}
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
                  <motion.div variants={cardV}>
                    <SpecialOptionCard
                      icon={<Boxes size={16} />}
                      title={t('userBuilds.useDefault', 'По умолчанию')}
                      subtitle={t('userBuilds.useDefaultHint', 'Пушка из выбранного базового ганпака')}
                      selected={isDefault}
                      onClick={onUseDefault}
                    />
                  </motion.div>
                  <motion.div variants={cardV}>
                    <SpecialOptionCard
                      icon={<Slash size={16} />}
                      title={t('userBuilds.useVanilla', 'Ванильная')}
                      subtitle={t('userBuilds.useVanillaHint', 'Без замены, оригинальная пушка из GTA')}
                      selected={isVanilla}
                      onClick={onUseVanilla}
                      tone="muted"
                    />
                  </motion.div>
                </div>

                {loadingAllGuns ? (

                  <div className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-2.5">
                    {Array.from({ length: 18 }).map((_, i) => (
                      <CandidateSkeleton key={i} />
                    ))}
                  </div>
                ) : candidates.length === 0 ? (
                  <Hint text={t('userBuilds.replaceNoMatches', 'Никто из текущих ганпаков не заменяет эту пушку.')} />
                ) : filtered.length === 0 ? (
                  <Hint text={t('userBuilds.replaceNoSearchMatches', 'Ничего не найдено по этому запросу.')} />
                ) : (
                  <div className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-2.5">
                    {filtered.map((g) => (
                      <motion.div key={`${g.packId}::${g.gunId}`} variants={cardV}>
                        <GunCandidateCard
                          gun={g}
                          isBasePack={g.packId === basePackId}
                          selected={isCurrent(g.packId, g.gunId)}
                          onClick={() => onPickOverride(g.packId, g.gunId)}
                          onPreview3D={g.glbUrl ? () => setGlbView(g) : null}
                          onOpenGunpack={onOpenGunpack}
                          onLongHover={g.previewUrl ? (gun) => setBigPreview(gun) : undefined}
                          onLongHoverEnd={() => setBigPreview(null)}
                        />
                      </motion.div>
                    ))}
                  </div>
                )}
              </motion.div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}

    {}
    {glbView && (
      <div
        style={{
          position: 'fixed',
          inset: 0,
          zIndex: 300,
        }}
      >
        <GlbViewerModal
          glbUrl={glbView.glbUrl}
          title={glbView.displayName || glbView.baseName}
          subjectKind="gun"
          onClose={() => setGlbView(null)}
        />
      </div>
    )}

    <AnimatePresence>
      {bigPreview?.previewUrl && (
        <motion.div
          key="gun-big-preview"
          initial={{ opacity: 1 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
          className="fixed inset-0 z-[280] pointer-events-none flex items-center justify-center p-10"
        >
          <div className="absolute inset-0 bg-black/55 backdrop-blur-lg" />
          <motion.div
            initial={{ scale: 0.94 }}
            animate={{ scale: 1 }}
            transition={{ type: 'spring', stiffness: 280, damping: 26 }}
            className="relative w-[62vw] max-w-[940px] h-[60vh] max-h-[600px]"
          >
            <img
              src={bigPreview.previewUrl}
              alt={bigPreview.displayName || bigPreview.baseName}
              draggable={false}
              className="w-full h-full object-contain select-none drop-shadow-[0_24px_80px_rgba(0,0,0,0.85)]"
            />
          </motion.div>
          <div className="absolute bottom-10 inset-x-0 flex flex-col items-center gap-1 text-center px-6">
            <span className="text-lg font-bold text-white tracking-tight">
              {bigPreview.packName}
            </span>
            <span className="text-sm text-white/70">{bigPreview.displayName || bigPreview.baseName}</span>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
    </>
  );
}

function CandidateSkeleton() {

  return (
    <div
      aria-hidden
      className="relative w-full flex flex-col rounded-2xl overflow-hidden animate-pulse"
      style={{
        background: 'var(--bg-elevated)',
        boxShadow:
          '0 0 0 1px color-mix(in srgb, var(--accent) 10%, transparent), ' +
          '0 8px 22px -12px rgba(0,0,0,0.45)',
      }}
    >
      <div className="aspect-[4/3] w-full bg-glass" />
      <div className="flex flex-col gap-2 p-3">
        <div className="h-3.5 w-3/4 rounded-md bg-glass" />
        <div className="h-2.5 w-1/3 rounded-md bg-glass" />
        <div className="h-2.5 w-2/5 rounded-md bg-glass mt-0.5" />
      </div>
    </div>
  );
}

function Hint({ text }: { text: string }) {
  return (
    <div className="rounded-xl bg-glass/40 p-4 text-center text-xs text-text-muted">
      {text}
    </div>
  );
}

function SpecialOptionCard({
  icon, title, subtitle, selected, onClick, tone,
}: {
  icon: React.ReactNode;
  title: string;
  subtitle: string;
  selected: boolean;
  onClick: () => void;
  tone?: 'muted';
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -2 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}
      style={{ outline: 'none' }}
      className={
        'group relative w-full h-[78px] flex items-center gap-3 px-4 rounded-xl text-left ' +
        'border transition-colors ' +
        (selected
          ? 'bg-accent-soft border-accent shadow-glow-accent'
          : 'bg-bg-elevated/55 border-white/[0.08] hover:bg-bg-elevated/80 hover:border-white/[0.20]')
      }
    >
      <span
        className={
          'shrink-0 w-10 h-10 rounded-xl flex items-center justify-center ' +
          (selected
            ? 'bg-accent text-text-on-accent'
            : tone === 'muted' ? 'bg-glass text-text-muted' : 'bg-accent-soft text-accent')
        }
      >
        {icon}
      </span>
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        <span className="text-sm font-bold text-text-primary truncate leading-tight">{title}</span>
        <span className="text-[10.5px] text-text-secondary truncate">{subtitle}</span>
      </div>
      {selected && (
        <motion.span
          initial={{ scale: 0.6, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: 'spring', stiffness: 420, damping: 26 }}
          className="shrink-0 w-6 h-6 rounded-full bg-accent text-bg-primary flex items-center justify-center"
        >
          <Check size={12} strokeWidth={3} />
        </motion.span>
      )}
    </motion.button>
  );
}

function GunCandidateCard({
  gun, isBasePack, selected, onClick, onPreview3D, onOpenGunpack,
  onLongHover, onLongHoverEnd,
}: {
  gun: FlatGun;
  isBasePack: boolean;
  selected: boolean;
  onClick: () => void;
  onPreview3D: (() => void) | null;
  onOpenGunpack?: (packId: string) => void;
  onLongHover?: (gun: FlatGun) => void;
  onLongHoverEnd?: () => void;
}) {
  const { t } = useTranslation();
  const hoverTimer = useRef<number | null>(null);
  const clearHover = () => {
    if (hoverTimer.current != null) { clearTimeout(hoverTimer.current); hoverTimer.current = null; }
  };
  useEffect(() => clearHover, []);
  return (
    <motion.button
      type="button"
      onClick={onClick}
      onMouseEnter={onLongHover ? () => {
        clearHover();
        hoverTimer.current = window.setTimeout(() => onLongHover(gun), 1200);
      } : undefined}
      onMouseLeave={onLongHover ? () => { clearHover(); onLongHoverEnd?.(); } : undefined}
      whileTap={{ scale: 0.97 }}
      whileHover={{ y: -3 }}
      transition={{ type: 'spring', stiffness: 360, damping: 22 }}

      style={{ outline: 'none' }}
      className={
        'group relative w-full flex flex-col rounded-3xl text-left overflow-hidden border ' +
        'bg-white/[0.04] backdrop-blur-xl ' +
        'transition-[border-color,box-shadow,background-color] duration-300 ease-smooth ' +
        (selected
          ? 'border-accent shadow-glow-accent'
          : 'border-white/[0.08] hover:bg-white/[0.06] hover:border-[color-mix(in_srgb,var(--accent)_45%,transparent)]')
      }
    >
      {}
      <div className="relative aspect-[4/3] w-full bg-glass overflow-hidden flex items-center justify-center">
        {gun.previewUrl ? (
          <img
            src={gun.previewUrl}
            alt={gun.displayName || gun.baseName}
            draggable={false}
            className="max-w-full max-h-full w-auto h-auto object-contain select-none p-3"
          />
        ) : (
          <Crosshair size={32} strokeWidth={1.4} className="text-text-muted" />
        )}
        {}
        <span aria-hidden className="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/55 to-transparent" />
        {}
        {onPreview3D && (
          <button
            type="button"
            onClick={(e) => { e.stopPropagation(); onPreview3D(); }}
            className="absolute top-2 right-2 inline-flex items-center gap-1.5 h-8 px-2.5
                       rounded-full bg-black/55 backdrop-blur-md text-white
                       text-[10.5px] font-bold uppercase tracking-wider
                       opacity-0 group-hover:opacity-100 hover:bg-black/70
                       transition-opacity duration-150"
            style={{ outline: 'none' }}
            title={t('userBuilds.preview3D', '3D просмотр')}
          >
            <Eye size={12} strokeWidth={2.4} />
            3D
          </button>
        )}
      </div>

      <div className="flex items-center gap-2 p-3">
        <div className="flex-1 min-w-0 flex flex-col gap-0.5">
          <span
            onClick={onOpenGunpack ? (e) => { e.stopPropagation(); onOpenGunpack(gun.packId); } : undefined}
            title={onOpenGunpack ? t('userBuilds.openModPage', 'Открыть страницу мода') : undefined}
            className={
              'text-[13px] font-bold truncate leading-tight ' +
              (isBasePack ? 'text-accent' : 'text-text-primary') +
              (onOpenGunpack ? ' cursor-pointer hover:underline underline-offset-2 decoration-from-font' : '')
            }
          >
            {isBasePack && '★ '}
            {gun.packName}
          </span>
          <span className="text-[10.5px] text-text-secondary truncate">
            {gun.displayName || gun.baseName}
          </span>
        </div>
        {selected && (
          <motion.span
            layoutId="gun-replace-current"
            transition={{ type: 'spring', stiffness: 420, damping: 30 }}
            className="shrink-0 w-7 h-7 rounded-full bg-accent text-bg-primary flex items-center justify-center"
          >
            <Check size={13} strokeWidth={3} />
          </motion.span>
        )}
      </div>
    </motion.button>
  );
}
