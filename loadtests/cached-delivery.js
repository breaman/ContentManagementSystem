// NFR-1 — a cached public page answers in under 200 ms at the 95th percentile.
//
// The cache has no response header saying whether a request hit it, so the script establishes the
// hit by protocol rather than by assertion: `setup` fetches every URL in the hot set once, and the
// measured phase asks only for those. Anything measured here that was not a hit is a bug in the
// cache, not in the script — which is the point of pinning the failure rate to zero as well.

import http from 'k6/http';
import { check } from 'k6';

import { published, url, any, common, describe, guard } from './lib/dataset.js';

const HOT = Number(__ENV.HOT_URLS || 50);

export const options = {
  ...common,
  scenarios: {
    cached: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 200),
      timeUnit: '1s',
      duration: __ENV.DURATION || '2m',
      preAllocatedVUs: Number(__ENV.VUS || 50),
      maxVUs: Number(__ENV.MAX_VUS || 500),
    },
  },
  thresholds: {
    // http_req_waiting is time to first byte: the request has been sent and the server has not
    // answered yet. http_req_duration would include downloading the page, which NFR-1 is not about.
    http_req_waiting: ['p(95)<200'],
    http_req_failed: ['rate==0'],
    rate_limited: ['count==0'],
    checks: ['rate==1'],
  },
};

export function setup() {
  describe('cached-delivery', 'NFR-1: cached public page TTFB < 200 ms p95');

  const hot = published.slice(0, Math.min(HOT, published.length));

  for (const path of hot) {
    const warm = http.get(url(path));

    if (warm.status !== 200) {
      throw new Error(`${path} answered ${warm.status} during warm-up; the dataset is not what the manifest says.`);
    }
  }

  return { hot };
}

export default function (data) {
  const response = guard(http.get(url(any(data.hot))));

  check(response, {
    'answered 200': (r) => r.status === 200,
    'served a page': (r) => r.body.includes('class="cms-page'),
  });
}
