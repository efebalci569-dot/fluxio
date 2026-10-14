import { sha256 } from '@noble/hashes/sha2.js';
import { bytesToHex } from '@noble/hashes/utils.js';

/**
 * Incremental SHA-256. Web Crypto (crypto.subtle) is only available on HTTPS pages, and the
 * Fluxio page is served over plain HTTP on the local network, so a pure JS implementation is
 * used. Data is fed in chunks so files never need to be fully in memory.
 */
export interface Sha256Hasher {
  update(data: Uint8Array): void;
  hex(): string;
}

export function createSha256(): Sha256Hasher {
  const state = sha256.create();
  return {
    update: (data) => {
      state.update(data);
    },
    hex: () => bytesToHex(state.digest()),
  };
}
