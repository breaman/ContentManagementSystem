// The seeded dataset, as the load tests see it.
//
// Every script reads its URLs from the manifest `cms seed load` writes (task P9-12) rather than
// crawling for them or carrying them in the file: a crawler spends the first minutes of a run
// crawling, and hard-coded URLs go stale the first time the dataset is reseeded.
//
// The lists are `SharedArray`s, which k6 parses once and shares across every VU. A plain array is
// copied into each VU's memory, and at a thousand VUs a few thousand URLs stop being free.

import { SharedArray } from 'k6/data';
import { Counter } from 'k6/metrics';

const MANIFEST = __ENV.MANIFEST || './manifest.json';

// `open` is init-context only, which is exactly where a SharedArray's callback runs.
function read() {
  return JSON.parse(open(MANIFEST));
}

/** Published pages, sampled across the tree. */
export const published = new SharedArray('published', () => read().publishedUrls);

/** Published pages carrying the shared footer — the reusable-content fan-out. */
export const landing = new SharedArray('landing', () => read().landingUrls);

/** The one branch that runs to depth ten. */
export const deep = new SharedArray('deep', () => read().deepUrls);

/** URLs that answer 301. */
export const redirects = new SharedArray('redirects', () => read().redirectUrls);

/** URLs that answer 404. */
export const missing = new SharedArray('missing', () => read().notFoundUrls);

/** Row counts, so a run can say what it was pointed at. */
export const counts = new SharedArray('counts', () => [read().counts])[0];

const base = (__ENV.BASE_URL || 'http://localhost:5000').replace(/\/+$/, '');

/** Turns a manifest path into an absolute URL. */
export function url(path) {
  return base + path;
}

/** Picks one at random. */
export function any(list) {
  return list[Math.floor(Math.random() * list.length)];
}

/**
 * Options every script shares.
 *
 * `insecureSkipTLSVerify` is on because a load-test environment is routinely fronted by the ASP.NET
 * development certificate, and a run that fails on the certificate measures nothing at all. It is a
 * load generator, not a client: there is no secret in these requests to protect.
 */
export const common = {
  insecureSkipTLSVerify: true,
  noConnectionReuse: false,
  discardResponseBodies: false,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

/** Prints what the run was pointed at, so a result and its dataset stay together. */
export function describe(name, requirement) {
  console.log(
    `${name} — ${requirement}\n` +
      `  target:  ${base}\n` +
      `  dataset: ${counts.pages} pages (${counts.publishedPages} published), ` +
      `${counts.mediaItems} media items\n` +
      `  sampled: ${published.length} published URLs`,
  );
}

/**
 * Requests the public rate limiter refused.
 *
 * Every script carries a `rate_limited: ['count==0']` threshold on this, because the failure it
 * catches is invisible otherwise: the public budget is 600 requests a minute per address, which is
 * ten a second, and a load generator is one address. A run against the default budget answers 429
 * to nine requests in ten and reports superb latencies for the tenth.
 */
export const throttled = new Counter('rate_limited');

let warned = false;

/** Counts a refusal, and says once what to do about it. */
export function guard(response) {
  if (response.status !== 429) return response;

  throttled.add(1);

  if (!warned) {
    warned = true;
    console.error(
      'The public rate limiter refused a request (429). This run is measuring the rejection path. ' +
        'Raise Cms:RateLimits:PublicPagesPerMinute on the environment under test — see ' +
        'loadtests/README.md.',
    );
  }

  return response;
}
