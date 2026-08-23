import { useEffect, useState } from 'react';
import { bridge } from '@/bridge';

const FALLBACK = 'dev';

let _cache: Promise<string> | null = null;
let _value = FALLBACK;

function fetchVersion(): Promise<string> {
  if (_cache) return _cache;
  _cache = bridge
    .getAppVersion()
    .then((v) => {
      _value = v && v.trim() ? v.trim() : FALLBACK;
      return _value;
    })
    .catch(() => FALLBACK);
  return _cache;
}

fetchVersion();

export function useAppVersion(): string {
  const [v, setV] = useState(_value);
  useEffect(() => {
    let alive = true;
    fetchVersion().then((val) => { if (alive) setV(val); });
    return () => { alive = false; };
  }, []);
  return v;
}
