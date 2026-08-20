// NFR-9 — 5,000 requests per second of cached public traffic, against the 50,000-page dataset.
//
// The mix is not all pages. A real site's traffic includes requests for URLs that no longer exist
// and requests for URLs that moved, and both take a different path through the application than a
// cache hit does: a 404 is decided after the route lookup misses and the redirect table is
// consulted, and neither answer is served from the output cache. A run of nothing but hits would
// report a number the site never actually achieves.

import http from 'k6/http';
import { check } from 'k6';

import { published, redirects, missing, url, any, common, describe, guard } from './lib/dataset.js';

const TARGET = Number(__ENV.RATE || 5000);
const HOT = Number(__ENV.HOT_URLS || 500);

export const options = {
  ...common,
  scenarios: {
    ramp: {
      executor: 'ramping-arrival-rate',
      startRate: Math.max(1, Math.floor(TARGET / 10)),
      timeUnit: '1s',
      preAllocatedVUs: Number(__ENV.VUS || 500),
      maxVUs: Number(__ENV.MAX_VUS || 4000),
      stages: [
        { target: Math.floor(TARGET / 2), duration: __ENV.RAMP || '1m' },
        { target: TARGET, duration: __ENV.RAMP || '1m' },
        { target: TARGET, duration: __ENV.HOLD || '5m' },
      ],
    },
  },
  thresholds: {
    'http_req_waiting{kind:page}': ['p(95)<200'],
    'http_req_waiting{kind:redirect}': ['p(95)<200'],
    'http_req_waiting{kind:missing}': ['p(95)<800'],
    // Scoped by tag rather than global: k6 counts anything outside 200–399 as a failed request,
    // and this script asks for 404s on purpose. The 404s are checked for being 404s instead.
    'http_req_failed{kind:page}': ['rate==0'],
    'http_req_failed{kind:redirect}': ['rate==0'],
    checks: ['rate==1'],
    rate_limited: ['count==0'],

    // The arrival-rate executors report what they could not start. A run that hit its latency
    // targets by quietly failing to generate the load is the failure this catches.
    dropped_iterations: ['count==0'],
  },
};

export function setup() {
  describe('scale', `NFR-9: ${TARGET} rps cached public traffic`);

  const hot = published.slice(0, Math.min(HOT, published.length));

  for (const path of hot) {
    http.get(url(path));
  }

  return { hot };
}

export default function (data) {
  const roll = Math.random();

  if (roll < 0.04) {
    // A moved URL. Redirects are not cached, so this is a database round trip every time.
    const response = guard(http.get(url(any(redirects)), {
      redirects: 0,
      tags: { kind: 'redirect' },
    }));

    check(response, { 'answered 301': (r) => r.status === 301 });

    return;
  }

  if (roll < 0.08) {
    const response = guard(http.get(url(any(missing)), { tags: { kind: 'missing' } }));

    check(response, { 'answered 404': (r) => r.status === 404 });

    return;
  }

  const response = guard(http.get(url(any(data.hot)), { tags: { kind: 'page' } }));

  check(response, { 'answered 200': (r) => r.status === 200 });
}
