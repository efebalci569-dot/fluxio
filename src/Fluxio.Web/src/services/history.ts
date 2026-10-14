/**
 * Transfer history kept only in this browser (localStorage). No account, no server copy.
 * It disappears when browser data is cleared and is not kept in private/incognito tabs.
 */
export interface LocalHistoryEntry {
  id: string;
  at: number;
  direction: 'sent' | 'received';
  name: string;
  size: number;
  result: 'completed' | 'failed' | 'cancelled' | 'declined';
  verified: boolean;
  computer: string;
}

const KEY = 'fluxio.history.v1';
const MAX = 200;

export function readHistory(): LocalHistoryEntry[] {
  try {
    const raw = localStorage.getItem(KEY);
    const parsed = raw ? (JSON.parse(raw) as unknown) : [];
    return Array.isArray(parsed) ? (parsed as LocalHistoryEntry[]) : [];
  } catch {
    return [];
  }
}

export function addHistory(entry: Omit<LocalHistoryEntry, 'id' | 'at'>): void {
  try {
    const next = [{ ...entry, id: `${Date.now()}-${Math.random().toString(36).slice(2)}`, at: Date.now() }, ...readHistory()].slice(0, MAX);
    localStorage.setItem(KEY, JSON.stringify(next));
  } catch {
    // Storage unavailable (private mode, quota): history is optional.
  }
}

export function clearHistory(): void {
  try {
    localStorage.removeItem(KEY);
  } catch {
    // ignore
  }
}

export function isHistoryAvailable(): boolean {
  try {
    const probe = '__fluxio_probe__';
    localStorage.setItem(probe, '1');
    localStorage.removeItem(probe);
    return true;
  } catch {
    return false;
  }
}
