import { usePrefs } from '../i18n/prefs';
import { errorText, type StringKey } from '../i18n/strings';
import type { EndInfo } from '../state/app';
import { Spinner, StatePanel } from '../components/ui';

export function PairingPage() {
  const { t } = usePrefs();
  return (
    <StatePanel icon="phone" title={t('pairing.title')} busy>
      <p className="muted">{t('pairing.body')}</p>
    </StatePanel>
  );
}

export function NoLinkPage() {
  const { t } = usePrefs();
  return (
    <StatePanel icon="qr" title={t('noLink.title')}>
      <p className="muted">{t('noLink.body')}</p>
    </StatePanel>
  );
}

export function PairFailedPage({ code }: { code: string | null }) {
  const { t, lang } = usePrefs();
  const icon = code === 'network' ? 'wifiOff' : code === 'sessionExpired' ? 'clock' : 'alert';
  return (
    <StatePanel icon={icon} tone="danger" title={t('errors.title')}>
      <p>{errorText(lang, code)}</p>
    </StatePanel>
  );
}

export function ApprovalPage({ computer, code }: { computer: string; code: string }) {
  const { t } = usePrefs();
  return (
    <section className="approval" aria-live="polite">
      <div className="state-panel__icon state-panel__icon--accent">
        <Spinner />
      </div>
      <h1>{t('approval.title')}</h1>
      <p className="muted">{t('approval.body', { computer })}</p>
      <div className="match-code" aria-label={`${t('approval.codeLabel')}: ${code.split('').join(' ')}`}>
        <span className="match-code__label">{t('approval.codeLabel')}</span>
        <span className="match-code__digits" aria-hidden="true">
          {code.split('').map((digit, i) => (
            <span key={i} className="match-code__digit">
              {digit}
            </span>
          ))}
        </span>
      </div>
      <p className="subtle">{t('approval.waiting')}</p>
    </section>
  );
}

export function EndedPage({ info }: { info: EndInfo }) {
  const { t } = usePrefs();
  const reasonKey = (info.kind === 'unauthorized' || !info.reason ? 'ended.reason.unknown' : `ended.reason.${info.reason}`) as StringKey;
  const tone = info.reason === 'deviceLeft' || info.reason === 'closedByUser' ? 'accent' : 'warning';
  const icon = info.kind === 'expired' ? 'clock' : info.reason === 'networkLost' ? 'wifiOff' : 'power';
  return (
    <StatePanel icon={icon} tone={tone} title={t('ended.title')}>
      <p>{t(reasonKey)}</p>
      <p className="muted">{t('ended.again')}</p>
    </StatePanel>
  );
}
