import { useEffect, type ReactNode } from 'react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { X, type LucideIcon } from 'lucide-react';
import { clsx } from 'clsx';
import { GlassPanel } from './GlassPanel';
import { EASE_DEPTH } from '../tokens';

interface ModalRootProps {
  onClose: () => void;
  children: ReactNode;
  maxWidthClassName?: string;
  closeLabel?: string;
  showCloseButton?: boolean;
}

function ModalRoot({
  onClose, children, maxWidthClassName = 'max-w-[480px]', closeLabel, showCloseButton = true,
}: ModalRootProps) {
  const { t } = useTranslation();
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <motion.div
      className="fixed inset-0 z-50 flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.24, ease: EASE_DEPTH }}
      onClick={onClose}
    >
      <div className="absolute inset-0 bg-black/60 backdrop-blur-sm" />
      <motion.div
        aria-hidden
        initial={{ opacity: 0 }}
        animate={{ opacity: 0.45 }}
        transition={{ duration: 0.55, ease: EASE_DEPTH, delay: 0.05 }}
        className="absolute pointer-events-none w-[560px] h-[560px] blur-3xl"
        style={{ background: 'radial-gradient(circle at 50% 50%, var(--accent) 0%, transparent 65%)' }}
      />
      <motion.div
        initial={{ opacity: 0, scale: 0.94, y: 12, filter: 'blur(8px)' }}
        animate={{ opacity: 1, scale: 1,    y: 0,  filter: 'blur(0px)' }}
        exit   ={{ opacity: 0, scale: 0.94, y: 12, filter: 'blur(8px)' }}
        transition={{ duration: 0.35, ease: EASE_DEPTH }}
        onClick={(e) => e.stopPropagation()}
        className={clsx('relative w-full max-h-[88vh]', maxWidthClassName)}
      >
        <GlassPanel
          depth="z3"
          tint="strong"
          rounded="3xl"
          highlight
          edge
          className="relative overflow-hidden border border-white/[0.08] max-h-[88vh] overflow-y-auto flex flex-col"
          style={{ background: 'color-mix(in srgb, var(--bg-elevated) 96%, transparent)' }}
        >
          <span
            aria-hidden
            className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
            style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
          />

          {showCloseButton && (
            <button
              type="button"
              onClick={onClose}
              aria-label={closeLabel ?? t('common.close', 'Закрыть')}
              className="absolute top-4 right-4 z-10 w-8 h-8 rounded-lg flex items-center justify-center
                         text-text-muted hover:text-text-primary hover:bg-glass transition-colors"
              style={{ outline: 'none' }}
            >
              <X size={16} />
            </button>
          )}

          {children}
        </GlassPanel>
      </motion.div>
    </motion.div>
  );
}

interface ModalHeaderProps {
  icon?: LucideIcon;
  pulse?: boolean;
  children: ReactNode;
}

function ModalHeader({ icon: Icon, pulse = true, children }: ModalHeaderProps) {
  return (
    <header className="px-7 pt-6 pb-5 border-b border-glass-border flex items-center gap-4 shrink-0">
      {Icon && (
        <div className="relative w-12 h-12 shrink-0">
          <motion.span
            aria-hidden
            className="absolute inset-0 rounded-2xl blur-md"
            style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
            initial={{ opacity: 0.3, scale: 0.85 }}
            animate={pulse ? { opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.12, 0.85] } : { opacity: 0.3, scale: 0.85 }}
            transition={pulse ? { duration: 2.8, ease: 'easeInOut', repeat: Infinity } : undefined}
          />
          <div className="relative w-12 h-12 rounded-2xl flex items-center justify-center
                          border border-white/[0.08] bg-accent-soft">
            <Icon size={20} className="text-accent" />
          </div>
        </div>
      )}
      <div className="flex-1 min-w-0 pr-6">{children}</div>
    </header>
  );
}

function ModalTitle({ children }: { children: ReactNode }) {
  return (
    <h2 className="font-display text-[22px] font-bold text-text-primary tracking-tight leading-tight">
      {children}
    </h2>
  );
}

function ModalSubtitle({ children }: { children: ReactNode }) {
  return (
    <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">
      {children}
    </p>
  );
}

function ModalBody({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={clsx('px-7 pt-5 pb-6 flex flex-col gap-4', className)}>
      {children}
    </div>
  );
}

function ModalActions({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={clsx('px-7 pb-6 flex items-center gap-2 shrink-0', className ?? 'justify-end')}>
      {children}
    </div>
  );
}

export const Modal = {
  Root:     ModalRoot,
  Header:   ModalHeader,
  Title:    ModalTitle,
  Subtitle: ModalSubtitle,
  Body:     ModalBody,
  Actions:  ModalActions,
};
