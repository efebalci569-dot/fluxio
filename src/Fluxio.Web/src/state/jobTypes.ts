export type FileProgressStatus =
  | 'queued'
  | 'transferring'
  | 'verifying'
  | 'completed'
  | 'skipped'
  | 'failed'
  | 'cancelled';

export interface FileProgress {
  fileId: string;
  name: string;
  size: number;
  /** Bytes confirmed so far (uploads: acknowledged by the computer; downloads: received). */
  done: number;
  status: FileProgressStatus;
  /** Error code (see i18n "errors.*"), when status is failed. */
  error?: string;
  /** SHA-256 matched on both sides. */
  verified?: boolean;
  /** Name used on the computer (uploads) when it had to be renamed. */
  savedName?: string | null;
  /** Downloads: "page" = fetched and verified by this page, "browser" = browser download manager. */
  mode?: 'page' | 'browser';
  /** Downloads in "page" mode: the file can be saved again from memory. */
  canSaveAgain?: boolean;
}

export type JobPhase = 'waitingApproval' | 'running' | 'done' | 'declined' | 'cancelled' | 'interrupted';

export interface JobSnapshot {
  batchId: string;
  direction: 'upload' | 'download';
  phase: JobPhase;
  files: FileProgress[];
  bytesPerSecond: number;
  createdAt: number;
}

export const isFinalProgress = (status: FileProgressStatus) =>
  status === 'completed' || status === 'skipped' || status === 'failed' || status === 'cancelled';

export function jobTotals(job: JobSnapshot) {
  let total = 0;
  let done = 0;
  for (const file of job.files) {
    if (file.status === 'cancelled' || file.status === 'skipped') {
      continue;
    }

    total += file.size;
    done += file.status === 'completed' ? file.size : Math.min(file.done, file.size);
  }

  return { total, done };
}
