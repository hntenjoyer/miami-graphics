import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Eye, ArrowRight, Download, CheckCircle2, Loader2, Lock } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { ArmorPreview3D } from './ArmorPreview3D';

export interface ArmorListItem {
  id: string;
  kind: 'redux' | 'library';
  name: string;
  author: string;
  glbUrl: string | null;
  previewUrl: string | null;
  downloadCount: number;
  installHidden: boolean;
  supportedServers: string[];
  hasMale?:   boolean;
  hasFemale?: boolean;
}

interface Props {
  item: ArmorListItem;
  isInstalled: boolean;
  installingId: string | null;
  onView3D: (item: ArmorListItem) => void;
  onInstall: (item: ArmorListItem) => void;
  onOpenRedux?: (reduxId: string) => void;
  pick?: { selected: boolean; onPick: (item: ArmorListItem) => void };
}

export function ArmorCard({ item, isInstalled, installingId, onView3D, onInstall, onOpenRedux, pick }: Props) {
  const { t } = useTranslation();
  const installing = installingId === item.id;
  const otherInstalling = installingId !== null && !installing;
  const showsMajestic = item.supportedServers.includes('majestic');
  const showsGta5rp   = item.supportedServers.includes('gta5rp');
  const canPreview3D  = !item.installHidden && !!item.glbUrl;

  return (
    <GlassPanel
      depth="z3"
      tint="ultra"
      rounded="3xl"
      highlight
      edge
      className={
        'relative overflow-hidden flex flex-col group ' +
        (pick?.selected
          ? 'border-2 border-accent/70'
          : 'border border-white/[0.08]')
      }
    >
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl
                   opacity-60 group-hover:opacity-100 transition-opacity duration-500"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
      />

      <div
        className={'relative h-[220px] bg-glass-strong overflow-hidden' + (pick ? ' cursor-pointer' : '')}
        onClick={pick ? () => pick.onPick(item) : undefined}
        role={pick ? 'button' : undefined}
        title={pick ? t('customize.armorPickHint', 'Выбрать этот броник') : undefined}
      >
        <div
          aria-hidden
          className="absolute inset-x-2 top-2 h-[78%] rounded-2xl pointer-events-none
                     opacity-70 group-hover:opacity-100
                     transition-opacity duration-700 ease-smooth"
          style={{
            background:
              'radial-gradient(ellipse at 50% 50%, rgba(255,255,255,0.18), transparent 72%)',
            filter: 'blur(20px)',
          }}
        />
        <div
          aria-hidden
          className="absolute inset-x-0 top-0 h-1/2 pointer-events-none
                     bg-gradient-to-b from-white/[0.04] to-transparent"
        />

        {item.installHidden ? (
          <div className="relative w-full h-full flex flex-col items-center justify-center gap-2
                          bg-gradient-to-b from-glass-strong to-bg-surface text-text-muted">
            <Lock size={36} strokeWidth={1.4} />
            <span className="text-[10px] uppercase tracking-wider">
              {t('armor.installHiddenPlaceholder')}
            </span>
          </div>
        ) : (
          <>
            <div className="absolute inset-0">
              {item.previewUrl ? (
                <img
                  src={item.previewUrl}
                  alt=""
                  loading="lazy"
                  decoding="async"
                  className="w-full h-full object-contain select-none
                             transition-transform duration-700 ease-smooth
                             group-hover:scale-[1.04]"
                  draggable={false}
                  onError={e => (e.currentTarget.style.display = 'none')}
                />
              ) : (
                <ArmorPreview3D glbUrl={item.glbUrl} />
              )}
            </div>
            {!item.glbUrl && !item.previewUrl && (
              <div className="absolute inset-x-0 bottom-2 flex justify-center pointer-events-none">
                <span className="px-2 py-0.5 text-[10px] uppercase tracking-wider rounded-md
                                 bg-black/50 text-text-muted backdrop-blur-sm">
                  {t('redux.componentNoModel')}
                </span>
              </div>
            )}
          </>
        )}

        {(showsGta5rp || showsMajestic) && (
          <div className="absolute top-2.5 left-2.5 z-10 flex items-center gap-1.5 pointer-events-none">
            {showsGta5rp   && <ServerBadge logo={gta5rpLogo}   alt="GTA5RP" />}
            {showsMajestic && <ServerBadge logo={majesticLogo} alt="Majestic" />}
          </div>
        )}

        <div className="absolute top-2.5 right-2.5 z-10 flex items-center gap-1.5">
          {canPreview3D && !pick && (
            <button
              type="button"
              onClick={() => onView3D(item)}
              title={t('redux.componentView3D')}
              aria-label={t('redux.componentView3D')}
              style={{ outline: 'none' }}
              className="w-8 h-8 rounded-lg flex items-center justify-center
                         bg-black/45 text-white/85 hover:text-white hover:bg-black/65
                         backdrop-blur-sm border border-white/[0.08]
                         opacity-0 group-hover:opacity-100 transition-opacity duration-300"
            >
              <Eye size={14} />
            </button>
          )}
        </div>
      </div>

      <div className="relative p-4 flex flex-col gap-3 flex-1">
        <div className="min-w-0 flex-1 flex items-center justify-between gap-3">
          <h3 className="text-[13.5px] font-bold text-text-primary truncate uppercase tracking-[0.06em]">
            {item.name}
          </h3>
          <div className="shrink-0 inline-flex items-center gap-2">
            {(item.hasMale || item.hasFemale) && (
              <span className="inline-flex items-center gap-1">
                {item.hasMale && (
                  <span title={t('armor.genderMale', 'Мужская')}
                        className="inline-flex items-center justify-center w-5 h-5 rounded-md
                                   text-[12px] font-bold leading-none
                                   bg-[color-mix(in_srgb,#60a5fa_16%,transparent)] text-[#7fb4fb]">♂</span>
                )}
                {item.hasFemale && (
                  <span title={t('armor.genderFemale', 'Женская')}
                        className="inline-flex items-center justify-center w-5 h-5 rounded-md
                                   text-[12px] font-bold leading-none
                                   bg-[color-mix(in_srgb,#f472b6_16%,transparent)] text-[#f48fc4]">♀</span>
                )}
              </span>
            )}
            <span className="inline-flex items-center gap-1 text-[11px] text-text-muted tabular-nums">
              <Download size={11} />
              {formatDownloads(item.downloadCount)}
            </span>
          </div>
        </div>

        {item.installHidden ? (
          <div className="w-full h-10 rounded-xl flex items-center justify-center gap-2
                          bg-white/[0.04] border border-white/[0.06] text-text-muted
                          text-xs font-bold uppercase tracking-wider">
            <Lock size={12} />
            {t('armor.installHiddenPill')}
          </div>
        ) : pick ? (
          <button
            type="button"
            onClick={() => onView3D(item)}
            disabled={!canPreview3D}
            style={{ outline: 'none' }}
            className="w-full inline-flex items-center justify-center gap-2 h-10 rounded-xl
                       text-[12.5px] font-bold uppercase tracking-[0.08em] transition-colors
                       disabled:opacity-55 disabled:cursor-not-allowed
                       bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                       hover:bg-bg-elevated/75 hover:border-white/[0.18]"
          >
            <Eye size={13} />
            <span>{t('redux.componentView3D', '3D просмотр')}</span>
          </button>
        ) : (
          <motion.button
            type="button"
            onClick={() => onInstall(item)}
            disabled={isInstalled || installing || otherInstalling}
            whileHover={isInstalled || installing || otherInstalling ? undefined : { scale: 1.01 }}
            whileTap={isInstalled || installing || otherInstalling ? undefined : { scale: 0.99 }}
            transition={{ duration: 0.18, ease: EASE_DEPTH }}
            style={{ outline: 'none' }}
            className={
              'w-full inline-flex items-center justify-center gap-2 h-10 rounded-xl ' +
              'text-[12.5px] font-bold uppercase tracking-[0.08em] transition-colors ' +
              'disabled:opacity-55 disabled:cursor-not-allowed ' +
              (isInstalled
                ? 'bg-[color-mix(in_srgb,var(--status-success)_14%,transparent)] ' +
                  'text-[color-mix(in_srgb,var(--status-success)_90%,white)] ' +
                  'border border-[color-mix(in_srgb,var(--status-success)_30%,transparent)]'
                : 'bg-bg-elevated/55 text-text-primary ' +
                  'border border-white/[0.08] ' +
                  'hover:bg-bg-elevated/75 hover:border-white/[0.18]')
            }
          >
            {installing
              ? <Loader2 size={13} className="animate-spin" />
              : isInstalled
                ? <CheckCircle2 size={13} />
                : <Download size={13} />}
            <span>
              {isInstalled
                ? t('armor.installed')
                : installing
                  ? t('armor.installing')
                  : t('armor.installButton')}
            </span>
          </motion.button>
        )}

        {onOpenRedux && item.kind === 'redux' && item.installHidden && (
          <button
            type="button"
            onClick={() => onOpenRedux(item.id)}
            title={t('armor.openRedux')}
            style={{ outline: 'none' }}
            className="w-full h-10 rounded-xl inline-flex items-center justify-center gap-2
                       bg-white/[0.04] border border-white/[0.06] text-text-secondary
                       hover:bg-white/[0.08] hover:text-text-primary transition-colors
                       text-xs font-bold uppercase tracking-wider"
          >
            <span>{t('armor.openRedux')}</span>
            <ArrowRight size={14} />
          </button>
        )}

        {item.installHidden && (
          <p className="text-[10px] text-text-muted leading-snug">
            {t('armor.installHiddenHint')}
          </p>
        )}
      </div>
    </GlassPanel>
  );
}

function formatDownloads(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000)     return (n / 1_000).toFixed(1) + 'k';
  return String(n);
}

function ServerBadge({ logo, alt }: { logo: string; alt: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-black/70 backdrop-blur-md flex items-center" title={alt}>
      <img src={logo} alt={alt} className="w-4 h-4 object-contain" />
    </div>
  );
}
