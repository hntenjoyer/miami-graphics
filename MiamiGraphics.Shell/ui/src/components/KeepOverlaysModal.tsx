import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Map as MapIcon, CircleDashed, Shield, Check, FastForward, type LucideIcon } from 'lucide-react';
import { ConfirmModal } from '@/components/ConfirmModal';
import { Toast } from '@/components/Toast';
import { bridge } from '@/bridge';
import { useKeepOverlaysStore } from '@/store/keepOverlaysStore';

export function KeepOverlaysModal() {
  const { t } = useTranslation();
  const pending = useKeepOverlaysStore(s => s.pending);
  const close   = useKeepOverlaysStore(s => s.close);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const [sel, setSel] = useState({ armor: true, minimap: true, rings: true, fastJoin: true });
  useEffect(() => {
    if (pending) setSel({ armor: true, minimap: true, rings: true, fastJoin: true });
  }, [pending]);

  const nothingSelected =
    !(sel.armor && pending?.armor) &&
    !(sel.minimap && pending?.minimap) &&
    !(sel.rings && (pending?.rings.length ?? 0) > 0) &&
    !(sel.fastJoin && pending?.fastJoin);

  const confirm = () => {
    if (!pending) return;
    if (nothingSelected) { close(); return; }
    const { rings, minimap, armor, fastJoin } = pending;
    const doMap   = sel.minimap && !!minimap;
    const doArmor = sel.armor && !!armor;
    const doRings = sel.rings && rings.length > 0;
    const doFastJoin = sel.fastJoin && !!fastJoin;

    close();

    void (async () => {
      try {
        if (doMap && minimap) {
          const mr = await bridge.reduxApplyMinimap('library', minimap.id, minimap.name);
          if (!mr.success) throw new Error(mr.errorMessage ?? t('redux.overlaysMapFail', 'Не удалось вернуть миникарту.'));
        }
        if (doArmor && armor) {
          const ar = await bridge.armorLibraryInstall(armor.id, true, true);
          if (!ar.success) throw new Error(ar.errorMessage ?? t('redux.overlaysArmorFail', 'Не удалось вернуть бронежилет.'));
        }
        if (doRings && rings.length > 0) {
          const rr = await bridge.minimapSetRangeRings(rings);
          if (!rr.success) throw new Error(rr.errorMessage ?? t('redux.ringsKeepFail', 'Не удалось нанести круги.'));
        }
        if (doFastJoin) {
          const fr = await bridge.otherSetFastJoin(true);
          if (!fr.success) throw new Error(fr.errorMessage ?? t('redux.fastjoinKeepFail', 'Не удалось перенести фаст заход.'));
        }
        setToast({ tone: 'success', message: t('redux.overlaysKeptToast', 'Наложения перенесены на новый редукс.') });
      } catch (e) {
        setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
      } finally {
        useKeepOverlaysStore.getState().bumpReapplyTick();
      }
    })();
  };

  const Row = ({ icon: Icon, label, value, checked, onToggle }: {
    icon: LucideIcon;
    label: string; value: string; checked: boolean; onToggle: () => void;
  }) => (
    <button
      type="button"
      onClick={onToggle}
      className="w-full flex items-center gap-3 px-3.5 py-2.5 text-left
                 transition-colors hover:bg-white/[0.03]"
      style={{ outline: 'none' }}
    >
      <Icon size={17} className="text-accent shrink-0" />
      <span className="flex-1 min-w-0 text-[12.5px] text-text-muted">{label}</span>
      <span className={'text-[13px] font-semibold truncate max-w-[45%] text-right '
        + (checked ? 'text-text-primary' : 'text-text-muted line-through')}>{value}</span>
      <span className={'shrink-0 w-[18px] h-[18px] rounded-md flex items-center justify-center border transition-colors '
        + (checked
            ? 'bg-accent border-accent text-text-on-accent'
            : 'bg-transparent border-white/25 text-transparent')}>
        <Check size={12} strokeWidth={3} />
      </span>
    </button>
  );

  return (
    <>
      <ConfirmModal
        open={pending !== null}
        title={t('redux.keepOverlaysTitle', 'Перенести наложения?')}
        message={t('redux.keepOverlaysBody', 'Ваши наложения слетели при установке редукса. Перенести их на новый редукс?')}
        details={pending ? (
          <>
            {pending.armor && (
              <Row icon={Shield} label={t('redux.overlaysArmor', 'Бронежилет')}
                value={pending.armor.name || pending.armor.id}
                checked={sel.armor} onToggle={() => setSel(s => ({ ...s, armor: !s.armor }))} />
            )}
            {pending.armor && (pending.minimap || pending.rings.length > 0) && (
              <div className="h-px bg-white/[0.07]" aria-hidden />
            )}
            {pending.minimap && (
              <Row icon={MapIcon} label={t('redux.overlaysMap', 'Миникарта')}
                value={pending.minimap.name || pending.minimap.id}
                checked={sel.minimap} onToggle={() => setSel(s => ({ ...s, minimap: !s.minimap }))} />
            )}
            {pending.minimap && pending.rings.length > 0 && (
              <div className="h-px bg-white/[0.07]" aria-hidden />
            )}
            {pending.rings.length > 0 && (
              <Row icon={CircleDashed} label={t('redux.overlaysRings', 'Круги дальности')}
                value={`${pending.rings.join(' / ')} ${t('redux.overlaysMetres', 'м')}`}
                checked={sel.rings} onToggle={() => setSel(s => ({ ...s, rings: !s.rings }))} />
            )}
            {pending.fastJoin && ((pending.rings.length > 0) || pending.minimap || pending.armor) && (
              <div className="h-px bg-white/[0.07]" aria-hidden />
            )}
            {pending.fastJoin && (
              <Row icon={FastForward} label={t('redux.overlaysFastJoin', 'Фаст заход')}
                value={t('redux.overlaysFastJoinOn', 'Включён')}
                checked={sel.fastJoin} onToggle={() => setSel(s => ({ ...s, fastJoin: !s.fastJoin }))} />
            )}
          </>
        ) : undefined}
        confirmLabel={nothingSelected
          ? t('redux.keepOverlaysNone', 'Не переносить')
          : t('redux.keepOverlaysConfirm', 'Перенести')}
        cancelLabel={t('redux.keepOverlaysCancel', 'Отмена')}
        hideConfirmArrow
        imageUrl={pending ? pending.reduxThumb : undefined}
        onConfirm={confirm}
        onCancel={close}
      />

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />
    </>
  );
}
