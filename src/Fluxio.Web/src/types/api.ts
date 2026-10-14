// Mirrors src/Fluxio.Transfer/Protocol/Dtos.cs. See docs/api.md.

export type SessionStatus = 'preparing' | 'awaitingScan' | 'awaitingApproval' | 'connected' | 'closed' | 'expired' | 'failed';

export type CloseReason =
  | 'closedByUser'
  | 'replaced'
  | 'pairingExpired'
  | 'approvalExpired'
  | 'idleTimeout'
  | 'deviceRejected'
  | 'deviceLeft'
  | 'networkLost'
  | 'appShutdown';

export type BatchStatus = 'awaitingApproval' | 'inProgress' | 'finished' | 'declined' | 'cancelled';

export type FileState = 'pending' | 'inProgress' | 'verifying' | 'completed' | 'failed' | 'cancelled' | 'skipped';

export type FileFailure =
  | 'integrityMismatch'
  | 'insufficientStorage'
  | 'writeFailed'
  | 'readFailed'
  | 'permissionDenied'
  | 'sourceChanged'
  | 'interrupted'
  | 'declined'
  | 'sessionEnded';

export interface FileDto {
  id: string;
  name: string;
  size: number;
  state: FileState;
  transferred: number;
  failure: FileFailure | null;
  verified: boolean;
  savedName: string | null;
}

export interface BatchDto {
  id: string;
  status: BatchStatus;
  totalBytes: number;
  transferredBytes: number;
  files: FileDto[];
}

export interface Limits {
  maxFileSize: number;
  maxFilesPerBatch: number;
  chunkSize: number;
  inPageDownloadLimit: number;
}

export interface PhoneState {
  version: number;
  status: SessionStatus;
  closeReason: CloseReason | null;
  computerName: string;
  matchCode: string;
  approved: boolean;
  limits: Limits;
  offers: BatchDto[];
  uploads: BatchDto[];
}

export interface PairResponse {
  token: string;
  state: PhoneState;
}

export interface ChunkResult {
  received: number;
  state: FileState;
}

export interface HashResponse {
  ready: boolean;
  sha256: string | null;
}

export interface ErrorInfo {
  code: string;
  message: string;
  received?: number | null;
  reason?: CloseReason | null;
}

export const isFinalFileState = (state: FileState) =>
  state === 'completed' || state === 'failed' || state === 'cancelled' || state === 'skipped';

export const isFinalBatch = (status: BatchStatus) =>
  status === 'finished' || status === 'declined' || status === 'cancelled';
