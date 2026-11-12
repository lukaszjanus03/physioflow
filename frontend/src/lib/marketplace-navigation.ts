const RETURN_KEY = 'physioflow-marketplace-return';
const INTERNAL_ORIGIN = 'https://physioflow.invalid';

export function safeMarketplaceReturn(
  value: string | null,
): string | null {
  if (
    !value ||
    value.length > 2000 ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.includes('\\')
  ) {
    return null;
  }

  try {
    const url = new URL(value, INTERNAL_ORIGIN);

    if (
      url.origin !== INTERNAL_ORIGIN ||
      !/^\/terapeuci(?:\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})?\/?$/i.test(
        url.pathname,
      )
    ) {
      return null;
    }

    return `${url.pathname}${url.search}`;
  } catch {
    return null;
  }
}

export function safeCatalogBack(
  value: string | null,
) {
  const publicPath = safeMarketplaceReturn(value);

  if (
    publicPath &&
    new URL(publicPath, INTERNAL_ORIGIN).pathname.replace(
      /\/$/,
      '',
    ) === '/terapeuci'
  ) {
    return publicPath;
  }

  if (
    value &&
    value.length <= 2000 &&
    value.startsWith('/portal?') &&
    !value.includes('\\')
  ) {
    const url = new URL(value, INTERNAL_ORIGIN);

    if (
      url.origin === INTERNAL_ORIGIN &&
      url.pathname === '/portal' &&
      ['catalog', 'favorites'].includes(
        url.searchParams.get('view') ?? '',
      )
    ) {
      return `${url.pathname}${url.search}`;
    }
  }

  return '/terapeuci';
}

export function rememberMarketplaceReturn(
  path: string,
) {
  const safe = safeMarketplaceReturn(path);
  if (!safe) return;

  try {
    window.localStorage.setItem(
      RETURN_KEY,
      JSON.stringify({
        path: safe,
        expires: Date.now() + 60 * 60_000,
      }),
    );
  } catch {
    // Sam adres logowania pozostaje zapasowym powrotem.
  }
}

export function consumeMarketplaceReturn(
  value: string | null,
) {
  const explicit = safeMarketplaceReturn(value);
  let stored: string | null = null;

  try {
    const raw =
      window.localStorage.getItem(RETURN_KEY);

    window.localStorage.removeItem(RETURN_KEY);

    if (raw) {
      const entry = JSON.parse(raw) as {
        path?: unknown;
        expires?: unknown;
      };

      if (
        typeof entry.path === 'string' &&
        typeof entry.expires === 'number' &&
        entry.expires > Date.now()
      ) {
        stored = safeMarketplaceReturn(entry.path);
      }
    }
  } catch {
    // Brak storage nie blokuje logowania.
  }

  return explicit ?? stored;
}