import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Check, Map as MapIcon, Loader2, Ban } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import { bridge } from '@/bridge';
import type { BigMap } from '@/bridge/types';
import { useCustomizeStore } from '@/store/customizeStore';
import { EASE_DEPTH } from '@/design';

export function BigMapPickerScreen() {
  const { t } = useTranslation();
  const goManage  = useCustomizeStore(s => s.goManage);
  const setBigMap = useCustomizeStore(s => s.setBigMap);
  const current   = useCustomizeStore(s => s.draft?.bigMap ?? null);

  const [maps, setMaps]       = useState<BigMap[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    (typeof bridge.bigMapList === 'function' ? bridge.bigMapList() : Promise.resolve([]))
      .then(list => { if (alive) setMaps(list ?? []); })
      .catch(() => { if (alive) setMaps([]); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);

  const pick = (m: BigMap | null) => {
    setBigMap(m ? { id: m.id, name: m.name } : null);
    goManage();
  };

  return (
    <div className="h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto flex items-center gap-3 flex-wrap">
          <BackButton onClick={goManage} label={t('customize.backToCustomize', 'К кастомизации')} />
          <div className="min-w-0">
            <h1 className="text-[20px] font-semibold tracking-tight text-text-primary flex items-center gap-2">
              <MapIcon size={18} className="text-accent" /> {t('bigmap.pickerTitle', 'Большая карта')}
            </h1>
            <p className="text-[12px] text-text-secondary mt-0.5">
              {t('bigmap.pickerSubtitle', 'Выбери карту - установится поверх редукса при «Применить».')}
            </p>
          </div>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-32">
        <div className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto mt-4">
          {loading ? (
            <div className="flex items-center justify-center gap-2 py-24 text-text-muted">
              <Loader2 size={18} className="animate-spin" /> {t('bigmap.pickerLoading', 'Загрузка карт…')}
            </div>
          ) : (
            <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5 gap-4">
              <motion.button
                type="button"
                onClick={() => pick(null)}
                initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.32, ease: EASE_DEPTH }}
                style={{ outline: 'none' }}
                className={`relative aspect-[4/3] rounded-2xl overflow-hidden border flex flex-col items-center justify-center gap-2
                            ${!current ? 'border-accent' : 'border-white/[0.08]'}
                            bg-bg-elevated/60 hover:border-white/20 transition-colors`}
              >
                <Ban size={26} className="text-text-muted" />
                <span className="text-[12px] font-bold uppercase tracking-wider text-text-secondary">{t('bigmap.none', 'Без карты')}</span>
                {!current && <Check size={16} className="absolute top-2 right-2 text-accent" />}
              </motion.button>

              {maps.map((m, i) => {
                const selected = current?.id === m.id;
                return (
                  <motion.button
                    key={m.id}
                    type="button"
                    onClick={() => pick(m)}
                    initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.32, delay: 0.03 + i * 0.03, ease: EASE_DEPTH }}
                    style={{ outline: 'none' }}
                    className={`relative aspect-[4/3] rounded-2xl overflow-hidden border text-left
                                ${selected ? 'border-accent' : 'border-white/[0.08]'}
                                bg-bg-elevated/60 hover:border-white/20 transition-colors`}
                  >
                    {m.previewUrl
                      ? <img src={m.previewUrl} alt={m.name} draggable={false}
                             className="absolute inset-0 w-full h-full object-cover select-none" />
                      : <div className="absolute inset-0 flex items-center justify-center"><MapIcon size={28} className="text-text-muted" /></div>}
                    <div className="absolute inset-x-0 bottom-0 p-3 bg-gradient-to-t from-black/85 via-black/40 to-transparent">
                      <div className="text-white text-[13px] font-bold leading-tight truncate">{m.name}</div>
                      {m.author && <div className="text-white/60 text-[10.5px] truncate">{t('bigmap.author', 'Автор: {{author}}', { author: m.author })}</div>}
                    </div>
                    {selected && <Check size={16} className="absolute top-2 right-2 text-accent drop-shadow" />}
                  </motion.button>
                );
              })}

              {maps.length === 0 && (
                <div className="col-span-full py-16 text-center text-text-muted text-sm">
                  {t('bigmap.empty', 'Пока нет доступных карт.')}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
