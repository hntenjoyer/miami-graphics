import i18n from '@/i18n';

export interface DevlogEntry {
  id: string;
  title: string;
  date: string;
}

function entry(id: string, titleKey: string, titleRu: string, dateKey: string, dateRu: string): DevlogEntry {
  return {
    id,
    get title() { return i18n.t(titleKey, titleRu); },
    get date()  { return i18n.t(dateKey, dateRu); },
  };
}

export const DEVLOG: DevlogEntry[] = [
  entry('d1', 'devlog.d1.title', 'Переписали систему бекапов под async + прогресс-бар', 'devlog.d1.date', '26 апр'),
  entry('d2', 'devlog.d2.title', 'Добавили мульти-аккаунты и контекстную кнопку Войти', 'devlog.d2.date', '24 апр'),
  entry('d3', 'devlog.d3.title', 'Главное окно без скролла - упор на главное', 'devlog.d3.date', '23 апр'),
  entry('d4', 'devlog.d4.title', 'WebView2 + WPF, идём к 1.0', 'devlog.d4.date', '20 апр'),
];
