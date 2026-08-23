import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Save, Download } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { useCustomizeStore } from '@/store/customizeStore';
import { GlassPanel, EASE_DEPTH, Glow3DButton } from '@/design';
import { ArmorPreview3D } from '@/screens/armor/ArmorPreview3D';
import { SlideIntro } from './SlideIntro';

interface Props {
  armorLibraryId: string;
  armorLibraryName: string;
  previewUrl: string | null;
  glbUrl: string | null;
}

export function ArmorLibraryPreview({ armorLibraryId, armorLibraryName, previewUrl, glbUrl }: Props) {
  const { t } = useTranslation();
  const openImportPicker = useCustomizeStore(s => s.openImportPicker);
  const openComponent    = useCustomizeStore(s => s.openComponent);
  const setGeneric       = useCustomizeStore(s => s.setGeneric);

  const onSave = () => {
    setGeneric('armor', { kind: 'armorLibrary', armorLibraryId, armorLibraryName });
    openComponent('armor');
  };

  return (
    <SlideIntro
      resetKey={`armor-library-preview:${armorLibraryId}`}
      title={t('customize.armorLibraryPreview.title', 'Поставить этот броник?')}
      subtitle={t('customize.armorLibraryPreview.subtitle', 'Наденем поверх редукса - остальные компоненты не тронем.')}
    >
      <div className="relative h-full flex flex-col">
        <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
          <BackButton onClick={() => openImportPicker('armor')} label={t('customize.import.backToPicker')} />
          <div className="flex-1" />
        </header>

        <div className="flex-1 overflow-y-auto px-8 pb-32 flex items-center justify-center">
          <div className="max-w-[860px] w-full flex flex-col items-center py-6">
            <motion.div
              initial={{ opacity: 0, y: 12, scale: 0.99 }}
              animate={{ opacity: 1, y: 0,  scale: 1   }}
              transition={{ duration: 0.42, delay: 0.04, ease: EASE_DEPTH }}
              className="w-full"
            >
              <GlassPanel
                depth="z3" tint="ultra" rounded="3xl" highlight edge
                className="overflow-hidden relative border border-white/[0.08] group"
              >
                <span
                  aria-hidden
                  className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                             bg-gradient-to-r from-transparent via-white/40 to-transparent"
                />
                <span
                  aria-hidden
                  className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl
                             opacity-60 group-hover:opacity-100 transition-opacity duration-500 z-10"
                  style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
                />
                <div className="relative w-full aspect-[16/8] bg-bg-elevated">
                  {previewUrl ? (
                    <img
                      src={previewUrl}
                      alt={armorLibraryName}
                      draggable={false}
                      className="absolute inset-0 w-full h-full object-contain object-center select-none"
                    />
                  ) : (
                    <div className="absolute inset-0">
                      <ArmorPreview3D glbUrl={glbUrl} />
                    </div>
                  )}
                  <div style={{ height: '32%' }} className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent pointer-events-none" />
                  <div className="absolute bottom-0 left-0 right-0 p-6 z-10">
                    <h2 className="font-display font-bold text-3xl uppercase tracking-wide text-white leading-tight
                                   drop-shadow-[0_2px_10px_rgba(0,0,0,0.7)]">
                      {armorLibraryName}
                    </h2>
                  </div>
                </div>

                <div className="px-6 py-4 border-t border-glass-border
                                bg-gradient-to-r from-accent-soft via-transparent to-accent-soft/40
                                flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-accent-soft flex items-center justify-center text-accent shrink-0">
                    <Download size={16} />
                  </div>
                  <div className="flex flex-col min-w-0 flex-1">
                    <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                      {t('customize.import.willImport')}
                    </span>
                    <span className="text-sm font-bold text-text-primary uppercase tracking-wide truncate">
                      {t('customize.componentNames.armor', 'Броник')}
                    </span>
                  </div>
                </div>
              </GlassPanel>
            </motion.div>
          </div>
        </div>

        <div className="absolute left-0 right-0 bottom-0 px-8 pb-5 pt-4
                        bg-gradient-to-t from-bg-base via-bg-base/95 to-transparent">
          <div className="max-w-[860px] mx-auto">
            <GlassPanel
              depth="z2" tint="ultra" rounded="2xl" highlight edge
              className="relative overflow-hidden border border-white/[0.08] group flex items-center gap-3 p-2"
            >
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="absolute -top-16 -right-12 w-48 h-48 pointer-events-none blur-3xl
                           opacity-60 group-hover:opacity-100 transition-opacity duration-500 z-0"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
              />
              <button
                type="button"
                onClick={() => openImportPicker('armor')}
                className="px-6 h-12 rounded-xl text-sm font-semibold text-text-secondary
                           hover:text-text-primary hover:bg-glass transition-colors duration-200 ease-depth"
              >
                {t('customize.import.cancel')}
              </button>
              <Glow3DButton
                variant="secondary"
                size="lg"
                onClick={onSave}
                leading={<Save size={16} strokeWidth={2.4} />}
                className="flex-1"
              >
                {t('customize.import.save')}
              </Glow3DButton>
            </GlassPanel>
          </div>
        </div>
      </div>
    </SlideIntro>
  );
}
