import { useRef, useState } from 'react';
import { usePrefs } from '../i18n/prefs';
import { errorText } from '../i18n/strings';
import { formatBytes } from '../services/format';
import type { AppSnapshot } from '../state/app';
import { app } from '../state/app';
import type { JobSnapshot } from '../state/jobTypes';
import { Button, IconButton, Notice, PageHeader } from '../components/ui';
import { FileRow, JobCard } from '../components/transfer';

function jobTitle(job: JobSnapshot, t: ReturnType<typeof usePrefs>['t']) {
  switch (job.phase) {
    case 'waitingApproval':
      return t('send.waitingApproval');
    case 'running':
      return t('send.sending');
    case 'declined':
      return t('send.declined');
    case 'cancelled':
    case 'interrupted':
      return t('file.cancelled');
    case 'done': {
      const ok = job.files.filter((f) => f.status === 'completed' || f.status === 'skipped').length;
      const failed = job.files.length - ok;
      return failed === 0 ? t('send.done', { count: ok }) : t('send.doneMixed', { ok, failed });
    }
  }
}

export function SendPage({ state }: { state: AppSnapshot }) {
  const { t, lang } = usePrefs();
  const input = useRef<HTMLInputElement>(null);
  const [selected, setSelected] = useState<File[]>([]);
  const total = selected.reduce((sum, f) => sum + f.size, 0);
  const anyActive = state.uploads.some((j) => j.phase === 'running' || j.phase === 'waitingApproval');
  const orphans = app.orphanUploads();

  const addFiles = (list: FileList | null) => {
    if (!list || list.length === 0) {
      return;
    }

    const incoming = Array.from(list);
    setSelected((current) => {
      const key = (f: File) => `${f.name}|${f.size}|${f.lastModified}`;
      const known = new Set(current.map(key));
      return [...current, ...incoming.filter((f) => !known.has(key(f)))];
    });
    app.clearActionError();
  };

  const send = async () => {
    const files = selected;
    await app.sendFiles(files);
    if (!app.getSnapshot().actionError) {
      setSelected([]);
    }
  };

  return (
    <section className="send">
      <PageHeader title={t('send.title')} subtitle={t('send.subtitle')} onBack={() => history.back()} backLabel={t('common.back')} />

      {orphans.map((batch) => (
        <Notice key={batch.id} tone="warning" title={t('send.orphanTitle')} action={<Button variant="secondary" onClick={() => void app.cancelOrphan(batch.id)}>{t('send.orphanCancel')}</Button>}>
          {t('send.orphanBody')}
        </Notice>
      ))}

      {anyActive && <Notice tone="warning">{t('send.keepOpen')}</Notice>}

      {state.uploads.map((job) => (
        <JobCard
          key={job.batchId}
          job={job}
          title={jobTitle(job, t)}
          onCancelAll={() => app.cancelUpload(job.batchId)}
          onCancelFile={(fileId) => app.cancelUpload(job.batchId, fileId)}
        />
      ))}

      <article className="card picker">
        <input
          ref={input}
          type="file"
          multiple
          className="visually-hidden"
          tabIndex={-1}
          aria-hidden="true"
          onChange={(event) => {
            addFiles(event.target.files);
            event.target.value = '';
          }}
        />

        {selected.length === 0 ? (
          <button type="button" className="dropzone" onClick={() => input.current?.click()}>
            <span className="dropzone__icon" aria-hidden="true">
              +
            </span>
            <span className="dropzone__title">{t('send.pick')}</span>
            <span className="muted">{t('send.empty')}</span>
          </button>
        ) : (
          <>
            <ul className="file-list">
              {selected.map((file, index) => (
                <FileRow
                  key={`${file.name}-${file.size}-${index}`}
                  name={file.name}
                  meta={formatBytes(file.size, lang)}
                  actions={<IconButton icon="trash" label={`${t('common.remove')}: ${file.name}`} onClick={() => setSelected((list) => list.filter((_, i) => i !== index))} />}
                />
              ))}
            </ul>
            <div className="picker__summary">
              <span className="muted">
                {t('common.files', { count: selected.length })} · {t('common.total')} {formatBytes(total, lang)}
              </span>
              <Button variant="ghost" icon="plus" onClick={() => input.current?.click()}>
                {t('send.pickMore')}
              </Button>
            </div>
            {state.actionError && <Notice tone="error">{errorText(lang, state.actionError)}</Notice>}
            <Button variant="primary" size="lg" block icon="upload" loading={state.busy} onClick={() => void send()}>
              {t('send.start')}
            </Button>
          </>
        )}
      </article>
    </section>
  );
}
