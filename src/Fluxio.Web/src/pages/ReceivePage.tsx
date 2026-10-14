import { usePrefs } from '../i18n/prefs';
import { errorText } from '../i18n/strings';
import { formatBytes } from '../services/format';
import type { AppSnapshot } from '../state/app';
import { app } from '../state/app';
import type { JobSnapshot } from '../state/jobTypes';
import { Icon } from '../components/Icon';
import { Button, Notice, PageHeader, StatePanel } from '../components/ui';
import { FileRow, JobCard } from '../components/transfer';

function jobTitle(job: JobSnapshot, t: ReturnType<typeof usePrefs>['t']) {
  switch (job.phase) {
    case 'running':
    case 'waitingApproval':
      return t('file.transferring');
    case 'done':
      return t('receive.done', { count: job.files.filter((f) => f.status === 'completed').length });
    default:
      return t('file.cancelled');
  }
}

export function ReceivePage({ state }: { state: AppSnapshot }) {
  const { t, lang } = usePrefs();
  const computer = state.phone?.computerName ?? '';
  const nothing = state.pendingOffers.length === 0 && state.downloads.length === 0;
  const anyActive = state.downloads.some((j) => j.phase === 'running');

  return (
    <section className="receive">
      <PageHeader title={t('receive.title')} subtitle={t('receive.subtitle')} onBack={() => history.back()} backLabel={t('common.back')} />

      {state.actionError && <Notice tone="error">{errorText(lang, state.actionError)}</Notice>}
      {anyActive && <Notice tone="warning">{t('send.keepOpen')}</Notice>}

      {state.pendingOffers.map((offer) => (
        <article key={offer.id} className="card offer" aria-labelledby={`offer-${offer.id}`}>
          <header className="offer__header">
            <span className="offer__icon" aria-hidden="true">
              <Icon name="download" size={24} />
            </span>
            <div>
              <h2 id={`offer-${offer.id}`}>{t('receive.offerTitle')}</h2>
              <p className="muted">
                {t('receive.offerFrom', { computer })} · {t('common.files', { count: offer.files.length })} · {formatBytes(offer.totalBytes, lang)}
              </p>
            </div>
          </header>
          <ul className="file-list">
            {offer.files.map((file) => (
              <FileRow key={file.id} name={file.name} meta={formatBytes(file.size, lang)} />
            ))}
          </ul>
          <div className="offer__actions">
            <Button variant="secondary" size="lg" icon="x" disabled={state.busy} onClick={() => void app.respondToOffer(offer.id, false)}>
              {t('receive.decline')}
            </Button>
            <Button variant="primary" size="lg" icon="check" loading={state.busy} onClick={() => void app.respondToOffer(offer.id, true)}>
              {t('receive.accept')}
            </Button>
          </div>
        </article>
      ))}

      {state.downloads.map((job) => (
        <JobCard
          key={job.batchId}
          job={job}
          title={jobTitle(job, t)}
          onCancelAll={() => app.cancelDownload(job.batchId)}
          onCancelFile={(fileId) => app.cancelDownload(job.batchId, fileId)}
          onSaveAgain={(fileId) => app.saveAgain(job.batchId, fileId)}
          footer={job.files.some((f) => f.status === 'completed' && f.mode === 'page') ? <p className="subtle small">{t('receive.savedHint')}</p> : null}
        />
      ))}

      {nothing && (
        <StatePanel icon="monitor" tone="receive" title={t('receive.empty')}>
          <p className="muted">{t('receive.emptyBody')}</p>
        </StatePanel>
      )}
    </section>
  );
}
