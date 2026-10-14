const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];

/** 1536 → "1.5 KB" (binary multiples, locale-aware decimal separator). */
export function formatBytes(bytes: number, locale = 'en'): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }

  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit++;
  }

  const digits = unit === 0 ? 0 : value < 10 ? 1 : 0;
  return `${value.toLocaleString(locale, { maximumFractionDigits: digits, minimumFractionDigits: digits })} ${UNITS[unit]}`;
}

export function formatSpeed(bytesPerSecond: number, locale = 'en'): string {
  return `${formatBytes(bytesPerSecond, locale)}/s`;
}

/** Seconds → "1 h 2 min", "3 min 5 s", "12 s". */
export function formatDuration(seconds: number | null, units: { h: string; min: string; s: string }): string {
  if (seconds === null || !Number.isFinite(seconds)) {
    return '—';
  }

  const total = Math.max(0, Math.round(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  if (h > 0) {
    return `${h} ${units.h} ${m} ${units.min}`;
  }

  if (m > 0) {
    return `${m} ${units.min} ${s} ${units.s}`;
  }

  return `${s} ${units.s}`;
}

export function percent(done: number, total: number): number {
  if (total <= 0) {
    return done > 0 ? 100 : 0;
  }

  return Math.min(100, Math.max(0, Math.floor((done / total) * 100)));
}

/** Short extension label for a file icon, e.g. "PDF". */
export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  if (dot <= 0 || dot === name.length - 1) {
    return '';
  }

  return name.slice(dot + 1, dot + 5).toUpperCase();
}
