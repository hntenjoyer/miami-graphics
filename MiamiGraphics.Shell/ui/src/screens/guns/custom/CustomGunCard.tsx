import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Crosshair, Download, Pencil, Trash2, FileText, Loader2, User, Check, Box } from 'lucide-react';
import { AnimatePresence } from 'framer-motion';
import type { CustomGun } from '@/bridge/types';
import { LazyImage } from '@/components/LazyImage';
import { GlbViewerModal } from '../GlbViewerModal';
import { prefetchGlb } from '@/utils/glbPrefetch';

interface Props {
  gun: CustomGun;
  installing?: boolean;
  installed?: boolean;
  onEditSkin: () => void;
  onEditMeta: () => void;
  onDelete:   () => void;
  onInstall:  () => void;
}

export const CATEGORY_LABEL: Record<string, string> = {
  assault: 'Штурмовая', shotgun: 'Дробовик', sniper: 'Снайперская',
  mg: 'Пулемёт', pistol: 'Пистолет', smg: 'ПП',
};

export function CustomGunCard({
  gun, installing, installed, onEditSkin, onEditMeta, onDelete, onInstall,
}: Props) {
  const { t } = useTranslation();
  const category = t(`guns.category.${gun.category}`, CATEGORY_LABEL[gun.category] ?? gun.category);
  const [viewerOpen, setViewerOpen] = useState(false);
  const mineInstallable = gun.mine && gun.status === 'published';

  return (
    <div className="group relative rounded-2xl overflow-hidden bg-bg-elevated border border-white/[0.06]
                    transition-[border-color,box-shadow,transform] duration-300 ease-smooth
                    hover:border-accent/40 hover:-translate-y-0.5 hover:shadow-glow-accent flex flex-col">
      <div className="relative aspect-[16/10] bg-gradient-to-br from-bg-elevated via-bg-base to-bg-elevated
                      flex items-center justify-center overflow-hidden">
        {gun.previewUrl ? (
          <LazyImage
            src={gun.previewUrl}
            alt=""
            aria-hidden="true"
            draggable={false}
            className="w-full h-full object-contain p-4 select-none
                       transition-transform duration-700 ease-smooth group-hover:scale-[1.05]"
            onError={e => (e.currentTarget.style.display = 'none')}
          />
        ) : (
          <Crosshair size={44} className="text-text-muted opacity-30" />
        )}

        <div className="absolute top-2.5 right-2.5 flex items-center gap-1.5">
          <span className="px-2 py-0.5 rounded-md bg-black/60 backdrop-blur-sm
                           text-[10px] font-bold uppercase tracking-wider text-white/80">
            {category}
          </span>
        </div>

        {gun.glbUrl && (
          <button
            type="button"
            onClick={() => setViewerOpen(true)}
            onPointerEnter={() => prefetchGlb(gun.glbUrl)}
            onFocus={() => prefetchGlb(gun.glbUrl)}
            title={t('guns.model3dTitle', '3D-модель')}
            aria-label={t('guns.model3dShow', 'Показать 3D-модель')}
            style={{ outline: 'none' }}
            className="absolute bottom-2 right-2 inline-flex items-center justify-center gap-1
                       h-7 px-2 rounded-lg bg-black/55 backdrop-blur-md text-white
                       hover:bg-[color-mix(in_srgb,var(--accent)_25%,rgba(0,0,0,0.55))]
                       transition-colors duration-200"
          >
            <Box size={12} />
            <span className="text-[9px] font-bold uppercase tracking-[0.18em]">3D</span>
          </button>
        )}

        {gun.mine && (
          <div className="absolute top-2.5 left-2.5 flex flex-col items-start gap-1.5">
            <span className="px-2 py-0.5 rounded-md bg-accent backdrop-blur-sm
                             text-[10px] font-bold uppercase tracking-wider text-white">
              {t('guns.custom.badgeMine', 'Мой')}
            </span>
            {gun.status === 'pending' && (
              <span className="px-2 py-0.5 rounded-md bg-status-warning backdrop-blur-sm
                               text-[10px] font-bold uppercase tracking-wider text-white">
                {t('guns.custom.statusPending', 'На модерации')}
              </span>
            )}
            {gun.status === 'rejected' && (
              <span
                title={gun.rejectReason ?? undefined}
                className="px-2 py-0.5 rounded-md bg-status-error backdrop-blur-sm cursor-help
                           text-[10px] font-bold uppercase tracking-wider text-white">
                {t('guns.custom.statusRejected', 'Отклонён')}
              </span>
            )}
          </div>
        )}
      </div>

      <div className="px-3.5 pt-2.5 pb-1 flex-1">
        <div className="font-display font-bold text-white text-[15px] uppercase tracking-wide truncate"
             title={gun.displayName}>
          {gun.displayName}
        </div>
        <div className="flex items-center justify-between mt-0.5">
          <span className="inline-flex items-center gap-1 text-xs text-text-secondary truncate max-w-[60%]">
            <User size={11} /> {gun.ownerName}
          </span>
          <span className="inline-flex items-center gap-1 text-xs font-bold tabular-nums text-accent">
            <Download size={11} /> {formatDownloads(gun.downloadCount)}
          </span>
        </div>
        {gun.description && (
          <p className="mt-1.5 text-[11.5px] leading-snug text-text-muted line-clamp-2">
            {gun.description}
          </p>
        )}
        {gun.mine && gun.status === 'rejected' && gun.rejectReason && (
          <p className="mt-1.5 text-[11px] leading-snug text-status-error">
            {t('guns.custom.rejectReason', { reason: gun.rejectReason, defaultValue: 'Причина отклонения: {{reason}}' })}
          </p>
        )}
      </div>

      <div className="px-3.5 pb-3.5 pt-2 flex items-center gap-2">
        {gun.mine ? (
          <>
            <button
              type="button"
              onClick={onEditSkin}
              className="btn-glow btn-glow--filled flex-1 !py-2 !text-[12px] inline-flex items-center justify-center gap-1.5"
            >
              <Pencil size={13} /> {t('common.edit', 'Редактировать')}
            </button>
            {mineInstallable && (
              <button
                type="button"
                onClick={onInstall}
                disabled={installing || installed}
                title={installed ? t('downloads.done.install', 'Установлено') : t('guns.custom.installSelf', 'Установить себе в игру')}
                aria-label={t('guns.custom.installSelf', 'Установить себе в игру')}
                className="w-9 h-9 shrink-0 rounded-lg flex items-center justify-center border border-white/[0.08]
                           bg-white/[0.03] text-text-secondary hover:text-text-primary hover:bg-white/[0.07]
                           transition-colors disabled:opacity-60"
                style={{ outline: 'none' }}
              >
                {installing ? <Loader2 size={14} className="animate-spin" />
                  : installed ? <Check size={14} />
                  : <Download size={14} />}
              </button>
            )}
            <button
              type="button"
              onClick={onEditMeta}
              title={t('guns.custom.editMetaTitle', 'Описание и название')}
              aria-label={t('guns.custom.editMetaTitle', 'Описание и название')}
              className="w-9 h-9 shrink-0 rounded-lg flex items-center justify-center border border-white/[0.08]
                         bg-white/[0.03] text-text-secondary hover:text-text-primary hover:bg-white/[0.07] transition-colors"
              style={{ outline: 'none' }}
            >
              <FileText size={14} />
            </button>
            <button
              type="button"
              onClick={onDelete}
              title={t('common.delete', 'Удалить')}
              aria-label={t('common.delete', 'Удалить')}
              className="w-9 h-9 shrink-0 rounded-lg flex items-center justify-center border border-white/[0.08]
                         bg-white/[0.03] text-text-secondary hover:text-status-error hover:border-status-error/40 transition-colors"
              style={{ outline: 'none' }}
            >
              <Trash2 size={14} />
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={onInstall}
            disabled={installing || installed}
            style={{ outline: 'none' }}
            className="w-full h-10 inline-flex items-center justify-center gap-2 rounded-xl
                       bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                       hover:bg-bg-elevated/75 hover:border-white/[0.18]
                       transition-colors text-[11.5px] font-bold uppercase tracking-wider
                       disabled:opacity-70"
          >
            {installing ? <Loader2 size={14} className="animate-spin" />
              : installed ? <Check size={14} />
              : <Download size={14} />}
            {installing
              ? t('guns.custom.installing', 'Устанавливаю…')
              : installed ? t('downloads.done.install', 'Установлено') : t('catalog.install', 'Установить')}
          </button>
        )}
      </div>

      <AnimatePresence>
        {viewerOpen && (
          <GlbViewerModal
            glbUrl={gun.glbUrl}
            title={gun.displayName}
            onClose={() => setViewerOpen(false)}
          />
        )}
      </AnimatePresence>
    </div>
  );
}

function formatDownloads(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
  return String(n);
}
