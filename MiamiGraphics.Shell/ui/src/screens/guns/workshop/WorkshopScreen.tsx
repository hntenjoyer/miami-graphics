import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowLeft, Hammer, Loader2 } from 'lucide-react';
import { bridge } from '@/bridge';
import type { WorkshopOpenRequest } from '@/bridge/types';
import { useCustomGunStore, currentUserIdentity } from '@/store/customGunStore';
import { useWorkshopPrefs } from '@/hooks/useWorkshopPrefs';
import { WorkshopWelcome } from './WorkshopWelcome';

interface Props {
  req: WorkshopOpenRequest;
  visible?: boolean;
}

export const CREATE_WORKSHOP_REQ: WorkshopOpenRequest = {};

export function WorkshopScreen({ req, visible = true }: Props) {
  const { t, i18n } = useTranslation();
  const close = useCustomGunStore(s => s.closeWorkshop);
  const reloadCatalog = useCustomGunStore(s => s.load);
  const bumpOwnPackTick = useCustomGunStore(s => s.bumpOwnPackTick);
  const storeReq = useCustomGunStore(s => s.workshopReq);
  const { showWelcome, setShowWelcome } = useWorkshopPrefs();
  const [welcomeOpen, setWelcomeOpen] = useState(showWelcome);
  useEffect(() => {
    if (visible) setWelcomeOpen(showWelcome);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  useEffect(() => { void bridge.windowSetFullscreen(visible); }, [visible]);
  useEffect(() => () => { void bridge.windowSetFullscreen(false); }, []);

  const [editorReady, setEditorReady] = useState(false);
  const revealTimer = useRef<number | null>(null);
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const reveal = () => {
    if (revealTimer.current) { clearTimeout(revealTimer.current); revealTimer.current = null; }
    setEditorReady(true);
  };
  const armRevealFallback = () => {
    if (revealTimer.current || editorReady) return;
    revealTimer.current = window.setTimeout(reveal, 1400);
  };
  useEffect(() => () => { if (revealTimer.current) clearTimeout(revealTimer.current); }, []);

  const src = useMemo(() => {
    const u = currentUserIdentity();
    const p = new URLSearchParams();
    if (u.id) p.set('owner', u.id);
    if (u.name) p.set('ownerName', u.name);
    p.set('lang', i18n.language);
    if (req.customGunId) { p.set('pack', '_custom'); p.set('gun', req.customGunId); }
    return `https://gunsmith.huntergraphics.local/index.html?${p.toString()}`;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [req.customGunId]);

  useEffect(() => {
    if (!editorReady) return;
    iframeRef.current?.contentWindow?.postMessage(
      { source: 'hg-host', type: 'lang', lang: i18n.language }, '*');
  }, [editorReady, i18n.language]);

  const sentOpenKey = useRef<string | null>(null);
  useEffect(() => {
    if (!visible || !editorReady) return;
    const w = iframeRef.current?.contentWindow;
    if (!w) return;
    const r = storeReq;
    if (r?.flow && r.pack && r.gun) {
      const key = `${r.flow}|${r.pack}|${r.gun}|${r.session ?? ''}`;
      if (sentOpenKey.current === key) return;
      sentOpenKey.current = key;
      w.postMessage({
        source: 'hg-host', type: 'open',
        lang: i18n.language,
        flow: r.flow, session: r.session ?? '',
        pack: r.pack, gun: r.gun,
        packName: r.packName ?? '', gunName: r.gunName ?? '',
        ownPackId: r.ownPackId ?? '', ownPackName: r.ownPackName ?? '',
      }, '*');
    } else if (sentOpenKey.current !== null) {
      sentOpenKey.current = null;
      w.postMessage({ source: 'hg-host', type: 'open', lang: i18n.language, flow: '' }, '*');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible, editorReady, storeReq]);

  useEffect(() => {
    const onMsg = (e: MessageEvent) => {
      const d = e.data;
      if (!d || d.source !== 'gunsmith') return;
      if (d.type === 'ready') reveal();
      if (d.type === 'published') { void reloadCatalog(); close(); }
      if (d.type === 'close') close();
      if (d.type === 'flow-cancel') close();
      if (d.type === 'ownpack-saved') {
        void useCustomGunStore.getState().refreshMyGunpacks().finally(() => {
          bumpOwnPackTick();
          void reloadCatalog();
          iframeRef.current?.contentWindow?.postMessage({ source: 'hg-host', type: 'ownpack-finish' }, '*');
          close();
        });
      }
    };
    window.addEventListener('message', onMsg);
    return () => window.removeEventListener('message', onMsg);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reloadCatalog, close]);

  const flowSubtitle =
    storeReq?.flow === 'ownpack'  ? t('workshop.screen.subtitleOwnpack', 'собери свой ганпак - до 3 ганов') :
    storeReq?.flow === 'standard' ? t('workshop.screen.subtitleStandard', {
        defaultValue: 'персонализация: {{gun}}',
        gun: storeReq.gunName || t('workshop.screen.fallbackStdGun', 'стандартный ган'),
      }) :
    storeReq?.flow === 'packbase' ? t('workshop.screen.subtitlePackbase', {
        defaultValue: 'база: {{gun}} из «{{pack}}»',
        gun: storeReq.gunName || t('workshop.screen.fallbackGun', 'ган'),
        pack: storeReq.packName || t('workshop.screen.fallbackPack', 'ганпака'),
      }) :
    req.customGunId ? t('workshop.screen.subtitleEdit', 'редактирование скина')
                    : t('workshop.screen.subtitleDefault', 'выбери пушку из ганпака и покрась её');

  return (
    <div className="h-full flex flex-col bg-bg-base">
      <header className="shrink-0 flex items-center gap-3 px-4 h-14 border-b border-white/[0.06] bg-bg-surface">
        <button onClick={close} className="w-9 h-9 rounded-lg flex items-center justify-center
                                            text-text-secondary hover:text-text-primary hover:bg-white/[0.06] transition-colors"
                aria-label={t('common.back', 'Назад')} style={{ outline: 'none' }}>
          <ArrowLeft size={18} />
        </button>
        <Hammer size={16} className="text-accent" />
        <div className="min-w-0">
          <div className="text-sm font-semibold text-text-primary truncate">{t('nav.workshop', 'Мастерская')}</div>
          <div className="text-[11px] text-text-muted truncate">{flowSubtitle}</div>
        </div>
      </header>

      <div className="flex-1 min-h-0 relative">
        <iframe
          ref={iframeRef}
          title={t('workshop.screen.iframeTitle', 'Ганпак-мастерская')}
          src={src}
          onLoad={armRevealFallback}
          className={
            'absolute inset-0 w-full h-full border-0 transition-opacity duration-300 ease-smooth ' +
            (editorReady ? 'opacity-100' : 'opacity-0')
          }
          allow="fullscreen"
        />
        {!editorReady && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-3
                          bg-bg-base text-text-muted pointer-events-none">
            <Loader2 size={20} className="animate-spin text-accent" />
            <span className="text-sm">{t('workshop.screen.loading', 'Загружаем мастерскую…')}</span>
          </div>
        )}
      </div>

      {welcomeOpen && (
        <WorkshopWelcome
          showAgain={showWelcome}
          onToggleShowAgain={setShowWelcome}
          onStart={() => setWelcomeOpen(false)}
        />
      )}
    </div>
  );
}
