import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FileText, Loader2 } from 'lucide-react';
import { Modal } from '@/design';
import type { CustomGun, CustomGunPatch } from '@/bridge/types';

const CATEGORIES: { value: string; key: string; ru: string }[] = [
  { value: 'assault', key: 'guns.category.assault', ru: 'Штурмовая' },
  { value: 'shotgun', key: 'guns.category.shotgun', ru: 'Дробовик' },
  { value: 'sniper',  key: 'guns.category.sniper',  ru: 'Снайперская' },
  { value: 'mg',      key: 'guns.category.mg',      ru: 'Пулемёт' },
  { value: 'pistol',  key: 'guns.category.pistol',  ru: 'Пистолет' },
  { value: 'smg',     key: 'guns.category.smg',     ru: 'ПП' },
];

interface Props {
  gun: CustomGun;
  onClose: () => void;
  onSave: (patch: CustomGunPatch) => Promise<void>;
}

export function MyGunEditModal({ gun, onClose, onSave }: Props) {
  const { t } = useTranslation();
  const [name, setName] = useState(gun.displayName);
  const [desc, setDesc] = useState(gun.description);
  const [category, setCategory] = useState(gun.category);
  const [saving, setSaving] = useState(false);

  const dirty = name !== gun.displayName || desc !== gun.description || category !== gun.category;
  const canSave = dirty && name.trim().length > 0 && !saving;

  const save = async () => {
    if (!canSave) return;
    setSaving(true);
    try {
      await onSave({ displayName: name.trim(), description: desc.trim(), category });
      onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal.Root onClose={onClose} maxWidthClassName="max-w-[460px]">
      <Modal.Header icon={FileText}>
        <Modal.Title>{t('guns.custom.editModalTitle', 'Мой скин')}</Modal.Title>
        <Modal.Subtitle>{t('guns.custom.editModalSubtitle', 'Название, описание и категория - как их увидят другие игроки.')}</Modal.Subtitle>
      </Modal.Header>
      <Modal.Body>
        <label className="flex flex-col gap-1.5">
          <span className="text-[11px] font-bold uppercase tracking-[0.1em] text-text-muted">{t('common.name', 'Название')}</span>
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            maxLength={40}
            placeholder="NEON VICE"
            className="focus-glow w-full h-11 px-3.5 rounded-xl bg-white/[0.04] border border-white/[0.08]
                       text-text-primary text-sm placeholder:text-text-muted
                       focus:bg-white/[0.06] transition-colors"
          />
        </label>

        <label className="flex flex-col gap-1.5">
          <span className="text-[11px] font-bold uppercase tracking-[0.1em] text-text-muted">{t('common.description', 'Описание')}</span>
          <textarea
            value={desc}
            onChange={e => setDesc(e.target.value)}
            maxLength={280}
            rows={3}
            placeholder={t('guns.custom.descPlaceholder', 'Пара слов о скине…')}
            className="focus-glow w-full px-3.5 py-2.5 rounded-xl bg-white/[0.04] border border-white/[0.08]
                       text-text-primary text-sm placeholder:text-text-muted resize-none
                       focus:bg-white/[0.06] transition-colors"
          />
          <span className="text-[10px] text-text-muted self-end tabular-nums">{desc.length}/280</span>
        </label>

        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-bold uppercase tracking-[0.1em] text-text-muted">{t('common.category', 'Категория')}</span>
          <div className="flex flex-wrap gap-1.5">
            {CATEGORIES.map(c => (
              <button
                key={c.value}
                type="button"
                onClick={() => setCategory(c.value)}
                className={
                  'focus-glow px-3 h-9 rounded-lg text-[12px] font-semibold border transition-colors ' +
                  (category === c.value
                    ? 'bg-accent-soft border-accent-40 text-accent'
                    : 'bg-white/[0.03] border-white/[0.08] text-text-secondary hover:text-text-primary hover:bg-white/[0.07]')
                }
              >
                {t(c.key, c.ru)}
              </button>
            ))}
          </div>
        </div>
      </Modal.Body>
      <Modal.Actions className="justify-end">
        <button type="button" onClick={onClose} className="btn-glow btn-glow--ghost">{t('common.cancel', 'Отмена')}</button>
        <button
          type="button"
          onClick={save}
          disabled={!canSave}
          className="btn-glow btn-glow--filled inline-flex items-center gap-2 disabled:opacity-50"
        >
          {saving && <Loader2 size={14} className="animate-spin" />}
          {t('common.save', 'Сохранить')}
        </button>
      </Modal.Actions>
    </Modal.Root>
  );
}
