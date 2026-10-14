import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { applyTheme, initialLang, initialTheme, PrefsContext, saveLang, type Prefs, type ThemeChoice } from './i18n/prefs';
import { translate, type Lang } from './i18n/strings';
import { app, type AppSnapshot } from './state/app';
import { Button, IconButton, Logo, Notice, Pill } from './components/ui';
import { ApprovalPage, EndedPage, NoLinkPage, PairFailedPage, PairingPage } from './pages/StatusPages';
import { HomePage } from './pages/HomePage';
import { SendPage } from './pages/SendPage';
import { ReceivePage } from './pages/ReceivePage';
import { HistoryPage } from './pages/HistoryPage';

let started = false;

function ConnectionPill({ state, t }: { state: AppSnapshot; t: Prefs['t'] }) {
  if (state.phase === 'connected' || state.phase === 'awaitingApproval') {
    if (state.connection === 'lost') {
      return <Pill tone="error">{t('status.lost')}</Pill>;
    }

    if (state.connection === 'reconnecting') {
      return <Pill tone="warn">{t('status.reconnecting')}</Pill>;
    }

    return state.phase === 'connected' ? <Pill tone="ok">{t('status.connected')}</Pill> : <Pill tone="busy">{t('status.waiting')}</Pill>;
  }

  if (state.phase === 'pairing' || state.phase === 'starting') {
    return <Pill tone="busy">{t('status.connecting')}</Pill>;
  }

  return null;
}

function Body({ state }: { state: AppSnapshot }) {
  switch (state.phase) {
    case 'starting':
    case 'pairing':
      return <PairingPage />;
    case 'noLink':
      return <NoLinkPage />;
    case 'pairFailed':
      return <PairFailedPage code={state.pairError} />;
    case 'awaitingApproval':
      return <ApprovalPage computer={state.phone?.computerName ?? ''} code={state.phone?.matchCode ?? ''} />;
    case 'ended':
      return <EndedPage info={state.ended ?? { kind: 'closed', reason: null }} />;
    case 'connected':
      switch (state.view) {
        case 'send':
          return <SendPage state={state} />;
        case 'receive':
          return <ReceivePage state={state} />;
        case 'history':
          return <HistoryPage />;
        default:
          return <HomePage state={state} />;
      }
  }
}

export function App() {
  const state = useSyncExternalStore(app.subscribe, app.getSnapshot);
  const [lang, setLang] = useState<Lang>(initialLang);
  const [theme, setTheme] = useState<ThemeChoice>(initialTheme);

  useEffect(() => {
    document.documentElement.lang = lang;
    saveLang(lang);
  }, [lang]);

  useEffect(() => applyTheme(theme), [theme]);

  useEffect(() => {
    if (!started) {
      started = true;
      void app.start();
    }

    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      if (app.hasActiveTransfers) {
        event.preventDefault();
        event.returnValue = '';
      }
    };
    window.addEventListener('popstate', app.onPopState);
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => {
      window.removeEventListener('popstate', app.onPopState);
      window.removeEventListener('beforeunload', onBeforeUnload);
    };
  }, []);

  const cycleTheme = useCallback(() => {
    setTheme((current) => (current === 'system' ? 'dark' : current === 'dark' ? 'light' : 'system'));
  }, []);

  const prefs = useMemo<Prefs>(
    () => ({ lang, setLang, theme, cycleTheme, t: (key, params) => translate(lang, key, params) }),
    [lang, theme, cycleTheme],
  );
  const t = prefs.t;
  const showLost = state.connection === 'lost' && (state.phase === 'connected' || state.phase === 'awaitingApproval');

  return (
    <PrefsContext.Provider value={prefs}>
      <div className="shell">
        <header className="topbar">
          <Logo />
          <div className="topbar__right">
            <ConnectionPill state={state} t={t} />
            <button type="button" className="icon-btn icon-btn--text" onClick={() => setLang(lang === 'tr' ? 'en' : 'tr')} aria-label={t('lang.switch')} title={t('lang.switch')}>
              {lang === 'tr' ? 'EN' : 'TR'}
            </button>
            <IconButton icon={theme === 'light' ? 'sun' : theme === 'dark' ? 'moon' : 'contrast'} label={t('theme.toggle')} onClick={cycleTheme} />
          </div>
        </header>

        <main className="page" id="main">
          {showLost && (
            <Notice tone="error" icon="wifiOff" title={t('connection.lostTitle')} action={<Button variant="secondary" icon="refresh" onClick={() => app.retryNow()}>{t('common.retry')}</Button>}>
              {t('connection.lostBody')}
            </Notice>
          )}
          <Body state={state} />
        </main>
      </div>
    </PrefsContext.Provider>
  );
}
