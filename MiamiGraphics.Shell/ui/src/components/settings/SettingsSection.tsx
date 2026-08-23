import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { GlassPanel } from '@/design';

interface SettingsSectionProps {
  icon:        LucideIcon;
  title:       string;
  description?: string;
  children:    ReactNode;
  className?:  string;
}

export function SettingsSection({ icon: Icon, title, description, children, className }: SettingsSectionProps) {
  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className={`relative overflow-hidden border border-white/[0.08] p-5${className ? ` ${className}` : ''}`}
    >
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-24 -right-16 w-56 h-56 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
      />

      <header className="relative flex items-start gap-3 mb-4">
        <div className="shrink-0 w-9 h-9 rounded-lg bg-accent-soft text-accent
                        flex items-center justify-center shadow-z1">
          <Icon size={18} />
        </div>
        <div className="min-w-0">
          <h2 className="text-base font-semibold text-text-primary leading-tight">
            {title}
          </h2>
          {description && (
            <p className="text-sm text-text-secondary mt-0.5">{description}</p>
          )}
        </div>
      </header>

      <div className="relative flex flex-col divide-y divide-glass-border">
        {children}
      </div>
    </GlassPanel>
  );
}
