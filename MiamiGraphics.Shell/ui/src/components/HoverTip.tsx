import { useState, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'framer-motion';

export function HoverTip({ label, children, className = '' }: {
  label?: string | null;
  children: ReactNode;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  return (
    <span
      className={'relative inline-flex ' + className}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
    >
      {children}
      <AnimatePresence>
        {open && !!label && (
          <motion.span
            key="tip"
            initial={{ opacity: 0, y: -3, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit   ={{ opacity: 0, y: -3, scale: 0.97 }}
            transition={{ duration: 0.15, ease: [0.22, 1, 0.36, 1] }}
            className="absolute left-1/2 -translate-x-1/2 top-full mt-2 z-50
                       pointer-events-none whitespace-nowrap"
            style={{ filter: 'drop-shadow(0 12px 24px rgba(0,0,0,0.55))' }}
          >
            <span
              className="block rounded-lg px-2.5 py-1.5 text-[11.5px] leading-none text-text-secondary"
              style={{
                background: 'linear-gradient(180deg,#16161d 0%,#0e0e14 100%)',
                border: '1px solid rgba(255,255,255,0.08)',
                boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.06)',
              }}
            >
              {label}
            </span>
          </motion.span>
        )}
      </AnimatePresence>
    </span>
  );
}
