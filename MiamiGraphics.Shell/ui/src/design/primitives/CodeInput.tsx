import { useRef, type ClipboardEvent, type KeyboardEvent } from 'react';
import { motion } from 'framer-motion';
import { clsx } from 'clsx';
import { EASE_DEPTH } from '../tokens';

interface CodeInputProps {
  value:    string;
  onChange: (next: string) => void;
  length?:  number;
  disabled?: boolean;
  autoFocus?: boolean;
  className?: string;
}

export function CodeInput({
  value, onChange, length = 6, disabled, autoFocus, className,
}: CodeInputProps) {
  const refs = useRef<(HTMLInputElement | null)[]>([]);

  const focusAt = (i: number) => {
    requestAnimationFrame(() => {
      const el = refs.current[Math.max(0, Math.min(i, length - 1))];
      el?.focus();
      el?.select();
    });
  };

  const setAt = (i: number, raw: string) => {

    const ch = (raw || '').replace(/\D/g, '').slice(-1);
    const arr = value.padEnd(length, ' ').split('');
    arr[i] = ch || ' ';
    const next = arr.join('').replace(/\s/g, '');
    onChange(next.slice(0, length));
    if (ch && i < length - 1) focusAt(i + 1);
  };

  const onKey = (i: number, e: KeyboardEvent<HTMLInputElement>) => {
    const cur = value[i] ?? '';
    if (e.key === 'Backspace') {
      e.preventDefault();
      const arr = value.padEnd(length, ' ').split('');
      if (cur) {
        arr[i] = ' ';
      } else if (i > 0) {
        arr[i - 1] = ' ';
        focusAt(i - 1);
      }
      onChange(arr.join('').replace(/\s/g, ''));
    } else if (e.key === 'ArrowLeft' && i > 0) {
      e.preventDefault();
      focusAt(i - 1);
    } else if (e.key === 'ArrowRight' && i < length - 1) {
      e.preventDefault();
      focusAt(i + 1);
    } else if (e.key === 'Home') {
      e.preventDefault();
      focusAt(0);
    } else if (e.key === 'End') {
      e.preventDefault();
      focusAt(length - 1);
    }
  };

  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    const pasted = e.clipboardData.getData('text').replace(/\D/g, '').slice(0, length);
    if (pasted.length > 0) {
      e.preventDefault();
      onChange(pasted);
      focusAt(Math.min(pasted.length, length - 1));
    }
  };

  return (

    <div className={clsx('flex gap-2.5 w-full select-none', className)}>
      {Array.from({ length }, (_, i) => {
        const ch = value[i] ?? '';
        const filled = !!ch;
        return (
          <motion.input
            key={i}
            ref={(el) => { refs.current[i] = el; }}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={1}
            autoComplete="one-time-code"
            autoFocus={autoFocus && i === 0}
            disabled={disabled}
            value={ch}
            onChange={(e) => setAt(i, e.target.value)}
            onKeyDown={(e) => onKey(i, e)}
            onPaste={onPaste}
            onFocus={(e) => e.currentTarget.select()}

            animate={{ scale: filled ? 1.04 : 1 }}
            transition={{ duration: 0.18, ease: EASE_DEPTH }}

            className={clsx(
              'flex-1 min-w-0 h-14 rounded-2xl text-center text-2xl font-bold tabular-nums',
              'outline-none transition-[border-color,background-color] duration-200 ease-depth',
              'caret-accent',
              'bg-bg-elevated',
              filled
                ? 'border border-accent text-accent'
                : 'border border-border-subtle text-text-primary',
              'focus:border-accent',
              'disabled:opacity-50 disabled:cursor-not-allowed',
            )}
          />
        );
      })}
    </div>
  );
}
