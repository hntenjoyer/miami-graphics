import { useEffect, useState, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { EASE_DEPTH } from '@/design';

interface SlideIntroProps {
  title: ReactNode;
  subtitle?: ReactNode;
  holdMs?: number;
  children: ReactNode;
  resetKey?: string;
}

export function SlideIntro({
  title,
  subtitle,
  holdMs = 1500,
  children,
  resetKey,
}: SlideIntroProps) {
  const [phase, setPhase] = useState<'intro' | 'content'>('intro');

  useEffect(() => {
    setPhase('intro');
    const id = window.setTimeout(() => setPhase('content'), holdMs);
    return () => window.clearTimeout(id);
  }, [resetKey, holdMs]);

  return (
    <div className="relative h-full">
      <AnimatePresence>
        {phase === 'intro' && (
          <motion.button
            type="button"
            key="intro"

            onClick={() => setPhase('content')}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit   ={{ opacity: 0 }}
            transition={{ duration: 0.45, ease: EASE_DEPTH }}
            className="absolute inset-0 z-30 flex flex-col items-center justify-center
                       text-center px-8 cursor-default focus:outline-none"
            aria-label="Skip"
          >
            {}
            <motion.h2
              initial={{ opacity: 0, y: 12, letterSpacing: '0.06em' }}
              animate={{ opacity: 1, y: 0,  letterSpacing: '-0.01em' }}
              exit   ={{ opacity: 0, y: -8, letterSpacing: '0.04em' }}
              transition={{ duration: 0.7, ease: EASE_DEPTH }}
              className="font-display font-bold text-text-primary
                         text-3xl md:text-4xl lg:text-5xl uppercase
                         max-w-3xl leading-[1.05]"
            >
              {title}
            </motion.h2>

            {subtitle && (
              <motion.p
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit   ={{ opacity: 0, y: -4 }}
                transition={{ duration: 0.6, delay: 0.12, ease: EASE_DEPTH }}
                className="mt-3 text-sm md:text-base text-text-secondary max-w-md"
              >
                {subtitle}
              </motion.p>
            )}

            {}
            <motion.div
              initial={{ opacity: 0, scaleX: 0.4 }}
              animate={{ opacity: 1, scaleX: 1   }}
              exit   ={{ opacity: 0, scaleX: 0.6 }}
              transition={{ duration: 0.6, delay: 0.2, ease: EASE_DEPTH }}
              className="mt-6 h-px w-24 bg-accent origin-center
                         shadow-[0_0_24px_var(--accent)]"
              style={{ filter: 'blur(0.3px)' }}
            />
          </motion.button>
        )}
      </AnimatePresence>

      {}
      <AnimatePresence mode="wait">
        {phase === 'content' && (
          <motion.div
            key="content"
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0  }}
            exit   ={{ opacity: 0, y: -4 }}
            transition={{ duration: 0.45, ease: EASE_DEPTH }}
            className="h-full"
          >
            {children}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
