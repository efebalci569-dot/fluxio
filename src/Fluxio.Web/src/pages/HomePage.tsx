import type { AppSnapshot } from '../state/app';
import { app } from '../state/app';
import { usePrefs } from '../i18n/prefs';
import { Icon } from '../components/Icon';
import { Button } from '../components/ui';

export function HomePage({ state }: { state: AppSnapshot }) {
  const { t } = usePrefs();
  const computer = state.phone?.computerName ?? '';
  const waiting = state.pendingOffers.length;
  const active = [...state.uploads, ...state.downloads].filter((j) => j.phase === 'running' || j.phase === 'waitingApproval').length;

  return (
    <section className="home">
      <div className="home__intro">
        <h1>{t('home.title')}</h1>
        <p className="muted">
          <Icon name="monitor" size={18} className="inline-icon" /> {t('home.subtitle', { computer })}
        </p>
      </div>

      <div className="action-grid">
        <button type="button" className="action-card action-card--send" onClick={() => app.setView('send')}>
          <span className="action-card__icon">
            <Icon name="upload" size={30} />
          </span>
          <span className="action-card__text">
            <span className="action-card__title">{t('home.send.title')}</span>
            <span className="action-card__body">{t('home.send.body')}</span>
          </span>
          <Icon name="chevronRight" size={22} className="action-card__chevron" />
        </button>

        <button type="button" className="action-card action-card--receive" onClick={() => app.setView('receive')}>
          <span className="action-card__icon">
            <Icon name="download" size={30} />
          </span>
          <span className="action-card__text">
            <span className="action-card__title">
              {t('home.receive.title')}
              {waiting > 0 && <span className="badge">{t('home.receive.badge', { count: waiting })}</span>}
            </span>
            <span className="action-card__body">{t('home.receive.body')}</span>
          </span>
          <Icon name="chevronRight" size={22} className="action-card__chevron" />
        </button>
      </div>

      {active > 0 && (
        <p className="home__active" role="status">
          <span className="pulse-dot" aria-hidden="true" /> {t('home.active', { count: active })}
        </p>
      )}

      <div className="home__footer">
        <Button variant="ghost" icon="clock" onClick={() => app.setView('history')}>
          {t('home.history')}
        </Button>
        <Button variant="ghost" icon="power" onClick={() => void app.disconnect()}>
          {t('home.disconnect')}
        </Button>
      </div>
    </section>
  );
}
