import type { ReactNode } from 'react';
import { usePrefs } from '../i18n/prefs';
import { errorText } from '../i18n/strings';
import { extensionOf, formatBytes, formatDuration, formatSpeed, percent } from '../services/format';
import { estimateRemaining } from '../services/speed';
import type { FileProgress, JobSnapshot } from '../state/jobTypes';
import { isFinalProgress, jobTotals } from '../state/jobTypes';
import { Icon } from './Icon';
import { Button, cx, IconButton, Notice, ProgressBar } from './ui';

export function FileBadge({ name }: { name: string }) {
  const ext = extensionOf(name);
  return (
    <span className="file-badge" aria-hidden="true">
      {ext ? <span className="file-badge__ext">{ext}</span> : <Icon name="file" size={20} />}
    </span>
  );
}

interface FileRowProps {
  name: string;
  meta: ReactNode;
  progress?: number;
  progressTone?: 'accent' | 'success' | 'danger' | 'muted';
  actions?: ReactNode;
  state?: 'ok' | 'error' | 'muted';
}

export function FileRow({ name, meta, progress, progressTone, actions, state }: FileRowProps) {
  return (
    <li className={cx('file-row', state && `file-row--${state}`)}>
      <FileBadge name={name} />
      <div className="file-row__body">
        <div className="file-row__name" title={name}>
          {name}
        </div>
        <div className="file-row__meta">{meta}</div>
        {progress !== undefined && <ProgressBar value={progress} label={name} tone={progressTone} />}
      </div>
      {actions && <div className="file-row__actions">{actions}</div>}
    </li>
  );
}

function FileStatusLine({ file, direction }: { file: FileProgress; direction: 'upload' | 'download' }) {
  const { t, lang } = usePrefs();
  const size = formatBytes(file.size, lang);
  switch (file.status) {
    case 'queued':
      return <>{`${size} · ${t('file.queued')}`}</>;
    case 'transferring':
      return <>{t('common.of', { done: formatBytes(file.done, lang), total: size })}</>;
    case 'verifying':
      return <>{`${size} · ${t('file.verifying')}`}</>;
    case 'completed':
      return (
        <span className="status-ok">
          <Icon name={file.verified ? 'shield' : 'check'} size={16} />
          {`${size} · ${file.verified ? t('file.verified') : t('file.completed')}`}
          {direction === 'upload' && file.savedName && file.savedName !== file.name ? ` · ${t('send.renamed', { name: file.savedName })}` : ''}
        </span>
      );
    case 'skipped':
      return <span className="status-warn">{`${size} · ${t('send.skipped')}`}</span>;
    case 'failed':
      return (
        <span className="status-error">
          <Icon name="alert" size={16} />
          {errorText(lang, file.error)}
        </span>
      );
    case 'cancelled':
      return <span className="subtle">{`${size} · ${t('file.cancelled')}`}</span>;
  }
}

interface JobCardProps {
  job: JobSnapshot;
  title: string;
  onCancelAll?: () => void;
  onCancelFile?: (fileId: string) => void;
  onSaveAgain?: (fileId: string) => void;
  footer?: ReactNode;
}

/** One transfer (a batch of files) with overall and per-file progress from real measurements. */
export function JobCard({ job, title, onCancelAll, onCancelFile, onSaveAgain, footer }: JobCardProps) {
  const { t, lang } = usePrefs();
  const { total, done } = jobTotals(job);
  const active = job.phase === 'running' || job.phase === 'waitingApproval';
  const overall = percent(done, total);
  const eta = estimateRemaining(total - done, job.bytesPerSecond);
  const units = { h: t('time.h'), min: t('time.min'), s: t('time.s') };

  return (
    <article className={cx('card', 'job', active && 'job--active')}>
      <header className="job__header">
        <div className="job__title">
          <h2>{title}</h2>
          <p className="muted job__meta">
            {t('common.files', { count: job.files.length })} · {formatBytes(total, lang)}
          </p>
        </div>
        {active && onCancelAll && (
          <Button variant="ghost" icon="x" onClick={onCancelAll}>
            {t('common.cancel')}
          </Button>
        )}
      </header>

      {job.phase === 'running' && (
        <div className="job__overall" aria-live="off">
          <ProgressBar value={overall} label={title} />
          <div className="job__stats">
            <span className="job__percent">{overall}%</span>
            <span className="muted">
              {t('common.of', { done: formatBytes(done, lang), total: formatBytes(total, lang) })}
              {job.bytesPerSecond > 0 && ` · ${formatSpeed(job.bytesPerSecond, lang)}`}
              {job.bytesPerSecond > 0 && eta !== null && eta > 0 && ` · ${t('common.remaining', { time: formatDuration(eta, units) })}`}
            </span>
          </div>
        </div>
      )}

      <ul className="file-list">
        {job.files.map((file) => {
          const fileActive = !isFinalProgress(file.status) && active;
          const filePercent = percent(file.status === 'completed' ? file.size : file.done, file.size);
          return (
            <FileRow
              key={file.fileId}
              name={file.name}
              meta={<FileStatusLine file={file} direction={job.direction} />}
              progress={file.status === 'transferring' || file.status === 'verifying' ? filePercent : undefined}
              state={file.status === 'failed' ? 'error' : file.status === 'cancelled' ? 'muted' : undefined}
              actions={
                <>
                  {file.canSaveAgain && onSaveAgain && (
                    <Button variant="secondary" icon="download" onClick={() => onSaveAgain(file.fileId)}>
                      {t('receive.saveAgain')}
                    </Button>
                  )}
                  {fileActive && onCancelFile && job.files.length > 1 && (
                    <IconButton icon="x" label={`${t('common.cancel')}: ${file.name}`} onClick={() => onCancelFile(file.fileId)} />
                  )}
                </>
              }
            />
          );
        })}
      </ul>

      {job.direction === 'download' && job.files.some((f) => f.mode === 'browser') && <Notice tone="info">{t('receive.browserMode')}</Notice>}
      {footer}
    </article>
  );
}
