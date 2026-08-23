import type { ReactNode } from 'react';

interface SettingsRowProps {
  label:        string;
  description?: string;
  control:      ReactNode;
}

export function SettingsRow({ label, description, control }: SettingsRowProps) {
  return (
    <div className="flex items-center justify-between gap-6 py-3">
      <div className="min-w-0">
        <div className="text-sm font-medium text-text-primary">{label}</div>
        {description && (
          <div className="text-xs text-text-muted mt-0.5">{description}</div>
        )}
      </div>
      <div className="shrink-0">{control}</div>
    </div>
  );
}
