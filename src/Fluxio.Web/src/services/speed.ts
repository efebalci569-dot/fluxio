/** Sliding-window throughput (bytes/second) from real progress samples. */
export class SpeedMeter {
  private samples: { t: number; total: number }[] = [];

  constructor(
    private readonly windowMs = 3000,
    private readonly now: () => number = () => performance.now(),
  ) {}

  /** Record the total number of bytes moved so far. */
  sample(total: number) {
    const t = this.now();
    this.samples.push({ t, total });
    while (this.samples.length > 2 && t - this.samples[0].t > this.windowMs) {
      this.samples.shift();
    }
  }

  get bytesPerSecond(): number {
    const t = this.now();
    const recent = this.samples.filter((s) => t - s.t <= this.windowMs);
    if (recent.length < 2) {
      return 0;
    }

    const first = recent[0];
    const last = recent[recent.length - 1];
    const seconds = Math.max((t - first.t) / 1000, 0.5);
    return Math.max(0, (last.total - first.total) / seconds);
  }

  reset() {
    this.samples = [];
  }
}

export function estimateRemaining(remainingBytes: number, bytesPerSecond: number): number | null {
  if (remainingBytes <= 0) {
    return 0;
  }

  if (bytesPerSecond < 1) {
    return null;
  }

  return remainingBytes / bytesPerSecond;
}
