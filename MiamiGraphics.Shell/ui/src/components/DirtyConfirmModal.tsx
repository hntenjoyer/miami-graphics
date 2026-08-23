import type { MouseEvent } from 'react';
import { AlertTriangle } from 'lucide-react';
import { GlassPanel } from '@/design';
import { useDirtyConfirmStore, type DirtyConfirmAction } from '@/store/dirtyConfirmStore';

export function DirtyConfirmModal() {
  const pending = useDirtyConfirmStore(s => s.pending);
  const close   = useDirtyConfirmStore(s => s.close);
  if (!pending) return null;

  const cancel = () => { pending.onCancel?.(); close(); };
  const fire = (a: DirtyConfirmAction) => { close(); void a.run(); };

  const kindClass = (kind: DirtyConfirmAction['kind']) => {
    switch (kind) {
      case 'accent':
        return 'bg-bg-elevated/80 text-text-primary border border-white/[0.20] hover:bg-bg-elevated/95 hover:border-white/[0.30]';
      case 'danger':
        return 'bg-red-500/10 text-red-200 border border-red-500/40 hover:bg-red-500/20 hover:border-red-500/60';
      default:
        return 'bg-bg-elevated/55 text-text-primary border border-white/[0.08] hover:bg-bg-elevated/75 hover:border-white/[0.18]';
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-6"
      onClick={cancel}
    >
      <GlassPanel
        depth="z3" tint="ultra" highlight edge rounded="3xl"
        className="w-full max-w-[560px] p-6 flex flex-col gap-4 relative overflow-hidden group border border-white/[0.08]"
        onClick={(e: MouseEvent) => e.stopPropagation()}
      >
        <span
          aria-hidden
          className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                     bg-gradient-to-r from-transparent via-white/40 to-transparent"
        />
        <span
          aria-hidden
          className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl
                     opacity-60 group-hover:opacity-100 transition-opacity duration-500 z-0"
          style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
        />

        <div className="flex items-start gap-3 relative z-10">
          <AlertTriangle size={20} style={{ color: 'var(--status-warning)' }} className="shrink-0 mt-0.5" />
          <div>
            <h2 className="text-base font-bold text-text-primary mb-1">{pending.title}</h2>
            <p className="text-sm text-text-secondary leading-relaxed whitespace-pre-line">{pending.message}</p>
          </div>
        </div>

        <div className="flex flex-col gap-2 pt-1 relative z-10">
          {pending.actions.map((a, i) => (
            <button
              key={i}
              type="button"
              onClick={() => fire(a)}
              style={{ outline: 'none' }}
              className={'w-full inline-flex flex-col items-center justify-center gap-0.5 min-h-12 px-4 py-2.5 rounded-xl transition-colors ' + kindClass(a.kind)}
            >
              <span className="text-sm font-bold uppercase tracking-wider">{a.label}</span>
              {a.hint && (
                <span className={'text-xs font-normal normal-case tracking-normal ' + (a.kind === 'danger' ? 'opacity-75' : 'text-text-muted')}>
                  {a.hint}
                </span>
              )}
            </button>
          ))}
          <button
            type="button"
            onClick={cancel}
            style={{ outline: 'none' }}
            className="px-4 py-2 text-sm text-text-muted hover:text-text-primary text-center"
          >
            {pending.cancelLabel}
          </button>
        </div>
      </GlassPanel>
    </div>
  );
}
