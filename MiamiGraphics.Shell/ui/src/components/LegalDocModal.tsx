import { useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Printer, X } from 'lucide-react';
import { EASE_DEPTH } from '@/design';

export interface DocContent {
  title: string;
  updated: string;
  body: React.ReactNode;
}

interface Props {
  open: boolean;
  onClose: () => void;
  ru: DocContent;
  en: DocContent;
}

export function LegalDocModal({ open, onClose, ru, en }: Props) {
  const { t, i18n } = useTranslation();
  const [lang, setLang] = useState<'ru' | 'en'>(i18n.language === 'ru' ? 'ru' : 'en');
  const doc = lang === 'ru' ? ru : en;
  const handlePrint = () => window.print();

  return createPortal(
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[140] overflow-y-auto"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.25, ease: EASE_DEPTH }}
          style={{
            background:
              'radial-gradient(ellipse at center, rgba(12,12,18,0.18) 0%, rgba(6,6,10,0.40) 95%)',
            backdropFilter: 'blur(1.5px) saturate(115%)',
            WebkitBackdropFilter: 'blur(1.5px) saturate(115%)',
          }}
        >
          <div className="legal-no-print sticky top-0 z-10 h-14
                          backdrop-blur-xl bg-black/25 border-b border-white/10">
            <div className="h-full w-full px-6 flex items-center justify-between gap-3">
              <div className="inline-flex items-center rounded-full bg-white/[0.06]
                              border border-white/10 p-0.5">
                {(['ru', 'en'] as const).map(l => (
                  <button
                    key={l}
                    type="button"
                    onClick={() => setLang(l)}
                    style={{ outline: 'none' }}
                    className={
                      'px-3 h-7 rounded-full text-[12px] font-bold tracking-wide transition-colors ' +
                      (lang === l
                        ? 'bg-white/[0.18] text-white'
                        : 'text-white/55 hover:text-white/85')
                    }
                  >
                    {l === 'ru' ? 'RUS' : 'ENG'}
                  </button>
                ))}
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <button
                  type="button"
                  onClick={handlePrint}
                  style={{ outline: 'none' }}
                  className="group inline-flex items-center gap-2 h-9 pl-3.5 pr-4 rounded-full
                             text-[12.5px] font-semibold text-white/90
                             bg-white/[0.08] hover:bg-white/[0.16]
                             border border-white/15 hover:border-white/25
                             transition-all active:scale-[0.97]"
                >
                  <Printer size={15} className="opacity-80 group-hover:opacity-100 transition-opacity" />
                  {t('legal.savePdf', 'Сохранить как PDF')}
                </button>
                <button
                  type="button"
                  onClick={onClose}
                  aria-label={t('common.close', 'Закрыть')}
                  title={t('common.close', 'Закрыть')}
                  style={{ outline: 'none' }}
                  className="w-9 h-9 rounded-full flex items-center justify-center
                             text-white/60 hover:text-white
                             bg-white/[0.06] hover:bg-white/[0.14]
                             border border-white/10 hover:border-white/25
                             transition-all active:scale-[0.95]"
                >
                  <X size={17} />
                </button>
              </div>
            </div>
          </div>

          <div className="flex justify-center px-4 pt-6 pb-20">
            <motion.article
              key={lang}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.3, ease: EASE_DEPTH }}
              className="legal-paper w-full max-w-[820px] bg-white text-slate-700
                         rounded-2xl shadow-[0_40px_120px_-30px_rgba(0,0,0,0.8)]
                         px-8 sm:px-16 py-12 sm:py-16"
            >
              <header className="mb-8 pb-6 border-b border-slate-200">
                <p className="text-[11px] font-bold tracking-[0.22em] uppercase text-slate-400 mb-3">
                  Miami Graphics
                </p>
                <h1 className="text-[30px] font-bold text-slate-900 tracking-tight leading-tight">
                  {doc.title}
                </h1>
                <p className="mt-2 text-[13px] text-slate-400">
                  {lang === 'ru' ? 'Редакция от ' : 'Last updated '}{doc.updated}
                </p>
              </header>

              {doc.body}
            </motion.article>
          </div>

          <style>{`
            @media print {
              @page { margin: 16mm; }
              body * { visibility: hidden !important; }
              .legal-paper, .legal-paper * { visibility: visible !important; }
              .legal-paper {
                position: absolute !important;
                left: 0; top: 0;
                width: 100% !important;
                max-width: none !important;
                margin: 0 !important;
                box-shadow: none !important;
                border-radius: 0 !important;
              }
              .legal-no-print { display: none !important; }
            }
          `}</style>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );
}

export function Section({
  n, title, children, last,
}: {
  n: string;
  title: string;
  children: React.ReactNode;
  last?: boolean;
}) {
  return (
    <section className={last ? '' : 'mb-6'} style={{ breakInside: 'avoid' }}>
      <h2 className="text-[16px] font-bold text-slate-900 mb-1.5">
        {n}. {title}
      </h2>
      <p className="text-[13.5px] leading-7 text-slate-600">{children}</p>
    </section>
  );
}
