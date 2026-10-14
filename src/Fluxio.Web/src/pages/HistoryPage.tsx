import { useState } from 'react';
import { usePrefs } from '../i18n/prefs';
import type { StringKey } from '../i18n/strings';
import { formatBytes } from '../services/format';
import { clearHistory, isHistoryAvailable, readHistory } from '../services/history';
import { Icon } from '../components/Icon';
import { Button, Notice, PageHeader, StatePanel } from '../components/ui';

export function HistoryPage() {
  const { t, lang } = usePrefs();
  const [entries, setEntries] = useState(readHistory);
  const available = isHistoryAvailable();
  const dateFormat = new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' });

  return (
    <section className="history">
      <PageHeader title={t('history.title')} onBack={() => history.back()} backLabel={t('common.back')} />
      <Notice tone="info" icon="shield">
        {available ? t('history.note') : t('history.unavailable')}
      </Notice>

      {entries.length === 0 ? (
        <StatePanel icon="clock" title={t('history.empty')} />
      ) : (
        <>
          <ul className="card history-list">
            {entries.map((entry) => (
              <li key={entry.id} className="history-item">
                <span className={`history-item__dir history-item__dir--${entry.direction}`} aria-hidden="true">
                  <Icon name={entry.direction === 'sent' ? 'upload' : 'download'} size={18} />
                </span>
                <div className="history-item__body">
                  <div className="file-row__name" title={entry.name}>
                    {entry.name}
                  </div>
                  <div className="file-row__meta">
                    {t(entry.direction === 'sent' ? 'history.sent' : 'history.received')} · {formatBytes(entry.size, lang)} · {dateFormat.format(entry.at)}
                  </div>
                </div>
                <span className={`tag tag--${entry.result}`}>{t(`history.${entry.result}` as StringKey)}</span>
              </li>
            ))}
          </ul>
          <Button
            variant="ghost"
            icon="trash"
            onClick={() => {
              clearHistory();
              setEntries([]);
            }}
          >
            {t('history.clear')}
          </Button>
        </>
      )}
    </section>
  );
}
