/** A human description of this phone for the computer screen, e.g. "iPhone · Safari". Not a secret, not an identifier. */
export function describeDevice(userAgent = navigator.userAgent): string {
  const ua = userAgent;
  let device = 'Phone';
  if (/iPad/.test(ua) || (/Macintosh/.test(ua) && typeof navigator !== 'undefined' && navigator.maxTouchPoints > 1)) {
    device = 'iPad';
  } else if (/iPhone/.test(ua)) {
    device = 'iPhone';
  } else if (/Android/.test(ua)) {
    device = /Mobile/.test(ua) ? 'Android' : 'Android tablet';
  } else if (/Windows/.test(ua)) {
    device = 'Windows';
  } else if (/Macintosh|Mac OS X/.test(ua)) {
    device = 'Mac';
  } else if (/Linux/.test(ua)) {
    device = 'Linux';
  }

  let browser = 'Browser';
  if (/SamsungBrowser/.test(ua)) {
    browser = 'Samsung Internet';
  } else if (/EdgA?\//.test(ua)) {
    browser = 'Edge';
  } else if (/OPR\/|Opera/.test(ua)) {
    browser = 'Opera';
  } else if (/Firefox|FxiOS/.test(ua)) {
    browser = 'Firefox';
  } else if (/CriOS|Chrome/.test(ua)) {
    browser = 'Chrome';
  } else if (/Safari/.test(ua)) {
    browser = 'Safari';
  }

  return `${device} · ${browser}`;
}

export const isIOS = (userAgent = navigator.userAgent) =>
  /iPhone|iPad|iPod/.test(userAgent) || (/Macintosh/.test(userAgent) && navigator.maxTouchPoints > 1);
