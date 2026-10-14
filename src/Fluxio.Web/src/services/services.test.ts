import { describe, expect, it } from 'vitest';
import { parsePairingFragment } from '../state/app';
import { jobTotals, type JobSnapshot } from '../state/jobTypes';
import { errorText, strings, translate } from '../i18n/strings';
import { describeDevice } from './device';
import { extensionOf, formatBytes, formatDuration, percent } from './format';
import { createSha256 } from './hash';
import { estimateRemaining, SpeedMeter } from './speed';

describe('pairing link', () => {
  it('reads the session id and secret from the fragment', () => {
    const s = 'A'.repeat(22);
    const k = 'b-_'.repeat(15);
    expect(parsePairingFragment(`#s=${s}&k=${k}`)).toEqual({ sessionId: s, secret: k });
  });

  it.each(['', '#', '#s=abc', '#k=' + 'a'.repeat(43), '#s=<script>&k=' + 'a'.repeat(43), '#s=' + 'a'.repeat(22) + '&k=short'])(
    'rejects malformed links: %s',
    (hash) => {
      expect(parsePairingFragment(hash)).toBeNull();
    },
  );
});

describe('sha256', () => {
  const encode = (text: string) => new TextEncoder().encode(text);

  it('matches the known test vectors', () => {
    const empty = createSha256();
    expect(empty.hex()).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');

    const abc = createSha256();
    abc.update(encode('abc'));
    expect(abc.hex()).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  });

  it('gives the same result when data arrives in chunks', () => {
    const data = new Uint8Array(1_000_003).map((_, i) => (i * 31) % 251);
    const whole = createSha256();
    whole.update(data);

    const chunked = createSha256();
    for (let offset = 0; offset < data.length; offset += 65_536) {
      chunked.update(data.subarray(offset, offset + 65_536));
    }

    expect(chunked.hex()).toBe(whole.hex());
  });
});

describe('formatting', () => {
  it('formats sizes with binary units', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
    expect(formatBytes(1536, 'tr')).toBe('1,5 KB');
    expect(formatBytes(-1)).toBe('—');
  });

  it('formats durations and percentages', () => {
    const units = { h: 'h', min: 'min', s: 's' };
    expect(formatDuration(5, units)).toBe('5 s');
    expect(formatDuration(125, units)).toBe('2 min 5 s');
    expect(formatDuration(3720, units)).toBe('1 h 2 min');
    expect(formatDuration(null, units)).toBe('—');
    expect(percent(50, 200)).toBe(25);
    expect(percent(0, 0)).toBe(0);
    expect(percent(300, 200)).toBe(100);
  });

  it('extracts short extensions', () => {
    expect(extensionOf('photo.JPG')).toBe('JPG');
    expect(extensionOf('archive.tar.gz')).toBe('GZ');
    expect(extensionOf('.bashrc')).toBe('');
    expect(extensionOf('README')).toBe('');
  });
});

describe('speed', () => {
  it('measures throughput from samples', () => {
    let now = 0;
    const meter = new SpeedMeter(3000, () => now);
    for (let i = 0; i <= 10; i++) {
      meter.sample(i * 100_000);
      now += 100;
    }

    now -= 100;
    expect(meter.bytesPerSecond).toBeCloseTo(1_000_000, -3);
    now += 10_000;
    expect(meter.bytesPerSecond).toBe(0);
    expect(estimateRemaining(1000, 100)).toBe(10);
    expect(estimateRemaining(1000, 0)).toBeNull();
  });
});

describe('job totals', () => {
  it('ignores cancelled and skipped files', () => {
    const job: JobSnapshot = {
      batchId: 'b',
      direction: 'upload',
      phase: 'running',
      bytesPerSecond: 0,
      createdAt: 0,
      files: [
        { fileId: '1', name: 'a', size: 100, done: 100, status: 'completed' },
        { fileId: '2', name: 'b', size: 100, done: 40, status: 'transferring' },
        { fileId: '3', name: 'c', size: 100, done: 10, status: 'cancelled' },
      ],
    };
    expect(jobTotals(job)).toEqual({ total: 200, done: 140 });
  });
});

describe('device label', () => {
  it('describes common phones', () => {
    expect(describeDevice('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1')).toBe('iPhone · Safari');
    expect(describeDevice('Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36')).toBe('Android · Chrome');
    expect(describeDevice('Mozilla/5.0 (Linux; Android 14; SM-S921B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/27.0 Chrome/125.0 Mobile Safari/537.36')).toBe('Android · Samsung Internet');
  });
});

describe('translations', () => {
  it('has every key in both languages', () => {
    expect(Object.keys(strings.tr).sort()).toEqual(Object.keys(strings.en).sort());
    for (const value of Object.values(strings.tr)) {
      expect(value.trim().length).toBeGreaterThan(0);
    }
  });

  it('fills parameters and falls back for unknown error codes', () => {
    expect(translate('en', 'common.files', { count: 3 })).toBe('3 file(s)');
    expect(translate('tr', 'home.subtitle', { computer: 'PC' })).toBe('PC bilgisayarına bağlı');
    expect(errorText('en', 'somethingNew')).toBe(strings.en['errors.unknown']);
    expect(errorText('tr', 'network')).toBe(strings.tr['errors.network']);
  });
});
