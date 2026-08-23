import { AnimatePresence, motion } from 'framer-motion';
import { useTranslation, Trans } from 'react-i18next';

interface Props {
  open: boolean;
  reduxName: string;
  currentArmorName: string;
  onRevertToRedux: () => void;
  onClearArmor:    () => void;
  onCancel:        () => void;
}

export function ArmorRollbackChoiceModal({
  open, reduxName, currentArmorName, onRevertToRedux, onClearArmor, onCancel,
}: Props) {
  const { t } = useTranslation();
  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[100] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
          onClick={onCancel}
          style={{
            background:
              'radial-gradient(ellipse at center, rgba(20,20,28,0.78) 0%, rgba(0,0,0,0.86) 75%)',
            backdropFilter: 'blur(14px) saturate(140%)',
            WebkitBackdropFilter: 'blur(14px) saturate(140%)',
          }}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 10 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit   ={{ opacity: 0, scale: 0.97, y: 6 }}
            transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
            onClick={(e) => e.stopPropagation()}
            className="relative w-full max-w-[460px] rounded-2xl px-6 py-6 overflow-hidden"
            style={{
              background: 'linear-gradient(180deg,#16161d 0%,#0e0e14 100%)',
              border: '1px solid rgba(255,255,255,0.10)',
              boxShadow: '0 32px 80px -12px rgba(0,0,0,0.65), inset 0 1px 0 rgba(255,255,255,0.06)',
            }}
          >
            <span
              aria-hidden
              className="absolute inset-x-6 top-0 h-px
                         bg-gradient-to-r from-transparent via-white/40 to-transparent"
            />
            <p className="text-[10px] font-bold uppercase tracking-[0.28em] text-text-muted">
              {t('armor.rollback.eyebrow', 'Откат брони')}
            </p>
            <h2 className="font-display font-bold text-lg text-text-primary mt-2 leading-tight">
              {t('armor.rollback.title', 'Что сделать с активной бронёй?')}
            </h2>
            {currentArmorName && (
              <p className="text-[12.5px] text-text-secondary mt-2 leading-relaxed">
                <Trans
                  i18nKey="armor.rollback.body"
                  defaults="Сейчас в апдейте лежит <hl>{{armor}}</hl> поверх редукса <hl>{{redux}}</hl>. Выберите, как откатить."
                  values={{ armor: currentArmorName, redux: reduxName }}
                  components={{ hl: <span className="text-text-primary font-semibold" /> }}
                />
              </p>
            )}

            <div className="flex flex-col gap-2.5 mt-5">
              <button
                type="button"
                onClick={onRevertToRedux}
                className="w-full inline-flex items-center justify-between gap-3 h-11 px-4 rounded-xl
                           bg-white text-black font-semibold text-[13px]
                           shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_8px_-2px_rgba(0,0,0,0.35)]
                           hover:bg-white/95 transition-colors"
                style={{ outline: 'none' }}
              >
                <span className="flex flex-col items-start gap-0.5">
                  <span>{t('armor.rollback.revertToRedux', 'Вернуть бронь редукса')}</span>
                  <span className="text-[10.5px] font-medium text-black/60">
                    {reduxName}
                  </span>
                </span>
                <span className="text-[10px] font-bold uppercase tracking-[0.16em] text-black/55">
                  ↺
                </span>
              </button>
              <button
                type="button"
                onClick={onClearArmor}
                className="w-full inline-flex items-center justify-between gap-3 h-11 px-4 rounded-xl
                           bg-white/[0.04] text-text-primary border border-white/[0.10] text-[13px] font-medium
                           hover:bg-white/[0.08] hover:border-white/[0.18] transition-colors"
                style={{ outline: 'none' }}
              >
                <span className="flex flex-col items-start gap-0.5">
                  <span>{t('armor.rollback.clearArmor', 'Убрать броню полностью')}</span>
                  <span className="text-[10.5px] text-text-muted">
                    {t('armor.rollback.clearArmorHint', 'Сброс к стоковой GTA')}
                  </span>
                </span>
                <span className="text-[10px] font-bold uppercase tracking-[0.16em] text-text-muted">
                  ⌫
                </span>
              </button>
            </div>

            <div className="flex justify-end mt-4">
              <button
                type="button"
                onClick={onCancel}
                className="text-[12px] text-text-muted hover:text-text-primary transition-colors px-2 py-1"
                style={{ outline: 'none' }}
              >
                {t('common.cancel', 'Отмена')}
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
