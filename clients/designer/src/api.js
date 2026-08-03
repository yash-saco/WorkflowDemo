export async function api(url, opts) {
  const res = await fetch(
    url,
    opts
      ? {
          headers: { 'Content-Type': 'application/json' },
          ...opts,
          body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
        }
      : undefined,
  );
  if (res.status === 204) return null;
  const json = await res.json().catch(() => null);
  if (!res.ok) throw new Error(json?.error || res.statusText);
  return json;
}
