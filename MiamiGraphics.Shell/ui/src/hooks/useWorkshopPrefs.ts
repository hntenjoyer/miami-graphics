import { useCallback, useState } from 'react';

const KEY = 'hntgraph.workshopShowWelcome';

function read(): boolean {
  try {
    const raw = window.localStorage.getItem(KEY);
    return raw === null ? true : raw === '1';
  } catch { return true; }
}

export function useWorkshopPrefs() {
  const [showWelcome, setShowWelcomeState] = useState<boolean>(read);

  const setShowWelcome = useCallback((next: boolean) => {
    setShowWelcomeState(next);
    try { window.localStorage.setItem(KEY, next ? '1' : '0'); } catch {}
  }, []);

  return { showWelcome, setShowWelcome };
}
