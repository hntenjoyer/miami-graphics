import { Fragment, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  ShieldCheck, ShieldAlert, ShieldQuestion, ChevronDown, ChevronRight,
  Send, Copy, FileWarning, FileCheck2, FilePlus2, FileMinus2,
  FileSearch, Clock, Package, Eye,
} from 'lucide-react';
import type { LegitReport, LegitFileFinding, LegitFieldDiff } from '@/bridge/types';
import { GlassPanel } from '@/design/primitives/GlassPanel';
import i18n from '@/i18n';

const VERDICT = {
  danger: {
    Icon: ShieldAlert, ring: 'var(--status-error)',
    chip: 'text-status-error', bg: 'rgba(239,68,68,0.08)', bd: 'rgba(239,68,68,0.35)',
  },
  mixed: {
    Icon: ShieldQuestion, ring: 'var(--status-warning)',
    chip: 'text-status-warning', bg: 'rgba(234,179,8,0.08)', bd: 'rgba(234,179,8,0.35)',
  },
  safe: {
    Icon: ShieldCheck, ring: 'var(--status-success)',
    chip: 'text-status-success', bg: 'rgba(34,197,94,0.08)', bd: 'rgba(34,197,94,0.35)',
  },
} as const;

const SEV_PILL: Record<string, { text: string; bg: string }> = {
  danger:  { text: '#f87171', bg: 'rgba(239,68,68,0.14)' },
  warning: { text: '#eab308', bg: 'rgba(234,179,8,0.14)' },
  visual:  { text: '#4ade80', bg: 'rgba(34,197,94,0.14)' },
  neutral: { text: 'var(--text-muted)', bg: 'var(--track)' },
};

function formatCheckedAt(iso: string): string {
  try {
    return new Date(iso).toLocaleString(i18n.language, {
      day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  } catch { return iso; }
}

function DetailRow({ icon: Icon, label, value }: { icon: typeof Package; label: string; value: string }) {
  return (
    <div className="flex items-center gap-2.5 px-3 py-2 border-b border-glass-border last:border-b-0">
      <Icon size={14} className="text-text-muted shrink-0" />
      <span className="text-[12px] text-text-secondary shrink-0">{label}</span>
      <span className="ml-auto text-[12.5px] text-text-primary text-right truncate" style={{ fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </span>
    </div>
  );
}

function ChangeIcon({ change }: { change: string }) {
  if (change === 'added')   return <FilePlus2 size={14} className="text-text-muted shrink-0" />;
  if (change === 'deleted') return <FileMinus2 size={14} className="text-text-muted shrink-0" />;
  return <FileWarning size={14} className="text-text-muted shrink-0" />;
}

function DeltaBadge({ d }: { d: LegitFieldDiff }) {
  if (d.deltaPercent == null) return null;
  const up = d.deltaPercent > 0;
  return (
    <span className={up ? 'text-status-error' : 'text-status-error'} style={{ fontVariantNumeric: 'tabular-nums' }}>
      {up ? '+' : ''}{d.deltaPercent}%
    </span>
  );
}

function FileRow({ f }: { f: LegitFileFinding }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(f.severity === 'danger' && f.fieldDiffs.length > 0);
  const pill = SEV_PILL[f.severity] ?? SEV_PILL.neutral;
  const hasDiffs = f.fieldDiffs.length > 0;

  return (
    <div className="border-b border-glass-border last:border-b-0">
      <button
        type="button"
        onClick={() => hasDiffs && setOpen(o => !o)}
        className={`w-full flex items-center gap-2 px-3 py-2.5 text-left ${hasDiffs ? 'hover:bg-glass cursor-pointer' : 'cursor-default'}`}
      >
        {hasDiffs
          ? (open ? <ChevronDown size={15} className="text-text-muted shrink-0" /> : <ChevronRight size={15} className="text-text-muted shrink-0" />)
          : <ChangeIcon change={f.change} />}
        <code className="text-[12.5px] text-text-primary truncate min-w-0 flex-1">{f.path}</code>
        {f.categoryLabel && (
          <span className="text-[10.5px] px-2 py-0.5 rounded-full shrink-0 whitespace-nowrap"
                style={{ color: pill.text, background: pill.bg }}>
            {f.categoryLabel}
          </span>
        )}
        {f.change === 'added'   && <span className="text-[10.5px] text-text-muted shrink-0">{t('security.report.file.added', 'добавлен')}</span>}
        {f.change === 'deleted' && <span className="text-[10.5px] text-text-muted shrink-0">{t('security.report.file.deleted', 'удалён')}</span>}
        {hasDiffs && <span className="text-[11px] text-text-secondary shrink-0">{t('security.report.file.fields', { count: f.fieldDiffs.length, defaultValue: '{{count}} полей' })}</span>}
      </button>

      {f.note && !open && (
        <p className="px-3 pb-2 -mt-1 text-[11.5px] text-text-secondary pl-9">{f.note}</p>
      )}

      <AnimatePresence initial={false}>
        {open && hasDiffs && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.22 }}
            className="overflow-hidden"
          >
            <div className="px-3 pb-3 pl-9">
              {f.note && <p className="mb-2 text-[11.5px] text-text-secondary">{f.note}</p>}
              <FieldTable diffs={f.fieldDiffs} />
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function FieldTable({ diffs }: { diffs: LegitFieldDiff[] }) {
  const { t } = useTranslation();
  const groups = new Map<string, LegitFieldDiff[]>();
  for (const d of diffs) {
    const k = d.owner || '-';
    let arr = groups.get(k);
    if (!arr) { arr = []; groups.set(k, arr); }
    arr.push(d);
  }
  return (
    <div className="rounded-lg border border-glass-border overflow-hidden">
     <div className="max-h-[360px] overflow-y-auto">
      <table className="w-full text-[12px]" style={{ fontVariantNumeric: 'tabular-nums' }}>
        <thead className="sticky top-0 z-10">
          <tr className="text-text-muted text-[11px] bg-glass-strong">
            <td className="py-1.5 px-2 w-[42%]">{t('security.report.table.field', 'Поле')}</td>
            <td className="py-1.5 px-2">{t('security.report.table.original', 'Оригинал')}</td>
            <td className="py-1.5 px-2">{t('security.report.table.inMod', 'В моде')}</td>
            <td className="py-1.5 px-2 text-right">{t('security.report.table.delta', 'Изм.')}</td>
          </tr>
        </thead>
        <tbody>
          {[...groups.entries()].map(([owner, rows]) => (
            <Fragment key={`g-${owner}`}>
              {owner !== '-' && (
                <tr>
                  <td colSpan={4} className="pt-2 pb-1 px-2 text-[11px] text-text-secondary">{owner}</td>
                </tr>
              )}
              {rows.map((d, i) => (
                <tr key={`${owner}-${d.field}-${i}`} className="border-t border-glass-border">
                  <td className="py-1.5 px-2 pl-3">
                    <code className={d.isRed ? 'text-status-error' : 'text-text-secondary'}>{d.field}</code>
                  </td>
                  <td className="py-1.5 px-2 text-text-secondary">{d.cleanValue}</td>
                  <td className={`py-1.5 px-2 ${d.isRed ? 'text-status-error' : 'text-text-primary'}`}>{d.modValue}</td>
                  <td className="py-1.5 px-2 text-right"><DeltaBadge d={d} /></td>
                </tr>
              ))}
            </Fragment>
          ))}
        </tbody>
      </table>
     </div>
    </div>
  );
}

function Section({ title, tone, findings, collapsible }: {
  title: string; tone: 'danger' | 'warning' | 'rest'; findings: LegitFileFinding[]; collapsible?: boolean;
}) {
  const [open, setOpen] = useState(!collapsible);
  if (findings.length === 0) return null;
  const color = tone === 'danger' ? 'text-status-error' : tone === 'warning' ? 'text-status-warning' : 'text-text-secondary';
  const bd = tone === 'danger' ? 'rgba(239,68,68,0.35)' : 'var(--glass-border)';
  return (
    <div>
      <button
        type="button"
        onClick={() => collapsible && setOpen(o => !o)}
        className={`mb-1.5 flex items-center gap-1.5 text-[13px] font-medium ${color} ${collapsible ? 'cursor-pointer' : 'cursor-default'}`}
      >
        {collapsible && (open ? <ChevronDown size={14} /> : <ChevronRight size={14} />)}
        {title} - {findings.length}
      </button>
      {open && (
        <div className="rounded-xl overflow-hidden bg-glass" style={{ border: `1px solid ${bd}` }}>
          {findings.map((f, i) => <FileRow key={`${f.change}-${f.path}-${i}`} f={f} />)}
        </div>
      )}
    </div>
  );
}

interface Props {
  report: LegitReport;
  onShare?: () => void;
  sharedCode?: string | null;
  sharing?: boolean;
}

export function LegitReportView({ report, onShare, sharedCode, sharing }: Props) {
  const { t } = useTranslation();
  const v = VERDICT[report.verdict];
  const danger = report.findings.filter(f => f.severity === 'danger');
  const warning = report.findings.filter(f => f.severity === 'warning');
  const rest = report.findings.filter(f => f.severity === 'visual' || f.severity === 'neutral');
  const [showDetails, setShowDetails] = useState(false);

  const verdictLabel =
    report.verdict === 'danger' ? t('security.report.verdict.danger', 'Опасно')
    : report.verdict === 'mixed' ? t('security.report.verdict.mixed', 'Требует внимания')
    : t('security.report.verdict.safe', 'Безопасно');

  return (
    <div className="flex flex-col gap-4">
      <GlassPanel depth="z2" tint="strong" rounded="2xl" className="p-4"
                  style={{ borderColor: v.bd, background: v.bg }}>
        <div className="flex items-start gap-3">
          <div className="w-11 h-11 rounded-xl flex items-center justify-center shrink-0"
               style={{ background: 'var(--glass-bg)', boxShadow: `0 0 0 1px ${v.bd}` }}>
            <v.Icon size={22} style={{ color: v.ring }} />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <span className={`text-[11px] px-2 py-0.5 rounded-full font-medium ${v.chip}`}
                    style={{ background: 'var(--glass-bg)' }}>{verdictLabel}</span>
              <span className="text-[12px] text-text-secondary truncate">{report.source}</span>
            </div>
            <h3 className="mt-1.5 text-[16px] font-semibold text-text-primary">{report.verdictTitle}</h3>
            <p className="mt-1 text-[13px] text-text-secondary leading-relaxed">{report.verdictText}</p>
          </div>
        </div>

        {report.verdictReasons.length > 0 && (
          <ul className="mt-3 pl-1 flex flex-col gap-1">
            {report.verdictReasons.map((r, i) => (
              <li key={i} className="text-[12.5px] text-text-secondary flex gap-2">
                <span style={{ color: v.ring }}>•</span><span>{r}</span>
              </li>
            ))}
          </ul>
        )}

        <div className="mt-3 flex items-center gap-4 text-[12px] text-text-secondary flex-wrap"
             style={{ fontVariantNumeric: 'tabular-nums' }}>
          <span className="text-status-error">{t('security.report.counts.danger', { count: report.dangerCount, defaultValue: '{{count}} опасных' })}</span>
          <span className="text-status-warning">{t('security.report.counts.warning', { count: report.warningCount, defaultValue: '{{count}} подозрительных' })}</span>
          <span>{t('security.report.counts.changed', { count: report.changedCount, defaultValue: '{{count}} изменено' })}</span>
          <span>{t('security.report.counts.added', { count: report.addedCount, defaultValue: '{{count}} добавлено' })}</span>
          <span>{t('security.report.counts.deleted', { count: report.deletedCount, defaultValue: '{{count}} удалено' })}</span>
        </div>

        <div className="mt-3 pt-3 border-t border-glass-border">
          <button
            type="button"
            onClick={() => setShowDetails(o => !o)}
            className="inline-flex items-center gap-1.5 text-[12.5px] text-text-secondary hover:text-text-primary transition-colors"
          >
            <Eye size={13} />
            {t('security.report.whatCompared', 'Что мы сравнивали')}
            {showDetails ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
          </button>

          <AnimatePresence initial={false}>
            {showDetails && (
              <motion.div
                initial={{ height: 0, opacity: 0 }}
                animate={{ height: 'auto', opacity: 1 }}
                exit={{ height: 0, opacity: 0 }}
                transition={{ duration: 0.22 }}
                className="overflow-hidden"
              >
                <div className="mt-2.5 rounded-xl border border-glass-border bg-glass overflow-hidden">
                  <DetailRow icon={Package}    label={t('security.report.detail.source', 'Источник')}        value={report.source} />
                  <DetailRow icon={FileSearch} label={t('security.report.detail.checkedFiles', 'Файлов сверено')} value={report.checkedCount.toLocaleString(i18n.language)} />
                  <DetailRow icon={Clock}      label={t('security.report.detail.checkedAt', 'Проверено')}    value={formatCheckedAt(report.checkedAt)} />
                </div>
                <p className="mt-2 text-[11.5px] text-text-muted leading-relaxed">
                  {t('security.report.compareNote', 'Каждый файл выше сверен побайтово и по значениям с чистой копией update.rpf. Список ниже - только то, что реально отличается.')}
                </p>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {onShare && (
          <div className="mt-3 pt-3 border-t border-glass-border flex items-center gap-2 flex-wrap">
            {sharedCode ? (
              <button type="button" onClick={onShare}
                      className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg text-[13px]
                                 bg-glass border border-glass-border hover:bg-glass-strong text-text-primary transition-colors">
                <Copy size={14} /> {t('security.report.codeLabel', 'Код отчёта:')} <code className="font-medium">{sharedCode}</code>
              </button>
            ) : (
              <button type="button" onClick={onShare} disabled={sharing}
                      className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg text-[13px]
                                 bg-glass border border-glass-border hover:bg-glass-strong text-text-primary transition-colors disabled:opacity-60">
                <Send size={14} /> {sharing
                  ? t('security.report.sharing', 'Отправляю…')
                  : t('security.report.share', 'Сообщить администрации')}
              </button>
            )}
            <span className="text-[11.5px] text-text-muted">
              {sharedCode
                ? t('security.report.sharedHint', 'Отправь этот код администрации в Discord/Telegram.')
                : t('security.report.shareHint', 'Отправит отчёт админам и выдаст короткий код.')}
            </span>
          </div>
        )}
      </GlassPanel>

      <p className="text-[11.5px] text-text-muted leading-relaxed px-1">
        <FileCheck2 size={12} className="inline -mt-0.5 mr-1" />
        {t('security.report.integrityNote', 'Мы отвечаем за целостность файлов, которые передаём от модмейкера - мы не вносим в них изменений. Проверка сравнивает мод с чистой GTA и показывает всё изменённое.')}
      </p>

      {report.unverified.length > 0 && (
        <p className="text-[11.5px] text-status-warning px-1">
          {t('security.report.unverified', {
            count: report.unverified.length,
            defaultValue: 'Не удалось проверить по значениям: {{count}} файл(ов) - показаны только как изменённые.',
          })}
        </p>
      )}

      <Section title={t('security.report.sections.danger', 'Опасные файлы')} tone="danger" findings={danger} />
      <Section title={t('security.report.sections.warning', 'Подозрительные / необычные')} tone="warning" findings={warning} />
      <Section title={t('security.report.sections.rest', 'Остальные изменения (визуал, добавленное)')} tone="rest" findings={rest} collapsible />
    </div>
  );
}
