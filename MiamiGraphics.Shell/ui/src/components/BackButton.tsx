import { ArrowLeft } from 'lucide-react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';

interface Props {
  onClick: () => void;
  label?: string;
  className?: string;
  disabled?: boolean;
}

export function BackButton({ onClick, label, className, disabled }: Props) {
  const { t } = useTranslation();
  const resolvedLabel = label ?? t('common.back', 'Назад');
  return (
    <motion.button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={resolvedLabel}
      aria-label={resolvedLabel}
      whileHover={!disabled ? { x: -2 } : undefined}
      whileTap={!disabled ? { scale: 0.94 } : undefined}
      transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
      className={
        'inline-flex items-center justify-center w-10 h-10 rounded-xl border ' +
        'bg-white/[0.04] border-white/[0.06] text-text-secondary ' +
        'backdrop-blur-md ' +
        'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18] ' +
        'transition-[background-color,color,border-color] duration-300 ease-smooth ' +
        'disabled:opacity-40 disabled:cursor-not-allowed ' +
        (className ?? '')
      }
      style={{ outline: 'none' }}
    >
      <ArrowLeft size={16} strokeWidth={2} />
    </motion.button>
  );
}
