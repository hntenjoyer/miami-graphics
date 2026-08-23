import type { NoTracerCategory } from '@/bridge/types';
import i18n from '@/i18n';

export function formatNoTracerScope(categories: NoTracerCategory[], keepSnipers: boolean): string {
  const has = (c: NoTracerCategory) => categories.includes(c);
  if (keepSnipers) return i18n.t('notracerScope.exceptSnipers', 'Кроме снайперок');
  const all3 = has('normal') && has('vehicle') && has('mk2ammo');
  if (all3) return i18n.t('notracerScope.allWeapons', 'Всё оружие');
  const labels: string[] = [];
  if (has('normal'))  labels.push(i18n.t('notracerScope.normal', 'Обычное'));
  if (has('mk2ammo')) labels.push(i18n.t('notracerScope.mk2', 'Mk II'));
  if (has('vehicle')) labels.push(i18n.t('notracerScope.vehicle', 'Транспорт'));
  return labels.join(', ') || '-';
}
