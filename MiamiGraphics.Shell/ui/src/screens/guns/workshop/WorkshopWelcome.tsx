import { Hammer, Check, Info } from 'lucide-react';
import { useTranslation, Trans } from 'react-i18next';
import { Modal } from '@/design';
import { PILL_CTA } from '../custom/CustomBrowse';

interface Props {
  showAgain: boolean;
  onToggleShowAgain: (next: boolean) => void;
  onStart: () => void;
}

export function WorkshopWelcome({ showAgain, onToggleShowAgain, onStart }: Props) {
  const { t } = useTranslation();
  return (
    <Modal.Root onClose={onStart} maxWidthClassName="max-w-[500px]" showCloseButton={false}>
      <Modal.Header icon={Hammer}>
        <Modal.Title>{t('workshop.welcome.title', 'Добро пожаловать в мастерскую')}</Modal.Title>
        <Modal.Subtitle>{t('workshop.welcome.subtitle', 'Здесь ты создаёшь свой скин оружия и делишься им с другими.')}</Modal.Subtitle>
      </Modal.Header>
      <Modal.Body>
        <div className="flex gap-3 rounded-xl border border-white/[0.08] bg-white/[0.03] p-3.5">
          <Info size={18} className="text-accent shrink-0 mt-0.5" />
          <p className="text-[13px] leading-relaxed text-text-secondary">
            <Trans
              i18nKey="workshop.welcome.notice"
              defaults={'Мы постоянно улучшаем редактор, поэтому <b>не всё будет выглядеть 1 в 1</b> как в игре: предпросмотр даёт близкий результат, а финальный вид зависит от движка GTA. Экспериментируй смело - оригинал оружия всегда можно вернуть.'}
              components={{ b: <span className="text-text-primary font-medium" /> }}
            />
          </p>
        </div>

        <button
          type="button"
          role="checkbox"
          aria-checked={showAgain}
          onClick={() => onToggleShowAgain(!showAgain)}
          className="focus-glow flex items-center gap-2.5 self-start text-[13px] text-text-secondary hover:text-text-primary transition-colors rounded-md"
        >
          <span className={
            'w-5 h-5 rounded-md border flex items-center justify-center transition-colors ' +
            (showAgain ? 'bg-accent border-accent text-white' : 'bg-white/[0.04] border-white/[0.15]')
          }>
            {showAgain && <Check size={13} strokeWidth={3} />}
          </span>
          {t('workshop.welcome.showOnEntry', 'Показывать это окно при входе')}
        </button>
      </Modal.Body>
      <Modal.Actions className="justify-end">
        <button type="button" onClick={onStart} className={PILL_CTA}>
          {t('workshop.welcome.start', 'Начать')}
        </button>
      </Modal.Actions>
    </Modal.Root>
  );
}
