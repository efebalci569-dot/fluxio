/**
 * Hands a downloaded file to the browser. Where it ends up (Downloads, Files app, …) is decided
 * by the browser and the operating system; a web page cannot choose a folder.
 */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  link.remove();
  // Give the browser time to start reading the blob before releasing the URL.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Starts a download through the browser's own download manager (used for very large files). */
export function startBrowserDownload(url: string, fileName: string) {
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  link.remove();
}
