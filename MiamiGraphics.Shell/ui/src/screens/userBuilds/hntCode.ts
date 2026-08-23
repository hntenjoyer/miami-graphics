const ALPHA = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

const PREFIX = 'HNT-';
const LENGTH = 8;

export function generateHntCode(): string {
  let body = '';
  for (let i = 0; i < LENGTH; i++) {
    body += ALPHA[Math.floor(Math.random() * ALPHA.length)];
  }
  return PREFIX + body;
}

export function looksLikeHntCode(s: string): boolean {
  const trimmed = s.trim();
  if (!trimmed.startsWith(PREFIX)) return false;
  const body = trimmed.slice(PREFIX.length);
  if (body.length !== LENGTH) return false;
  for (const ch of body) if (!ALPHA.includes(ch)) return false;
  return true;
}
