// NFR-2 — an uncached public page answers in under 800 ms at the 95th percentile.
//
// Every URL is requested exactly once, which is what makes the measurement uncached: the second
// request for a page is a cache hit and belongs to NFR-1. The iteration index is global across VUs,
// so no two of them collide on a URL — a shuffled random pick would repeat itself long before the
// list ran out and would quietly turn a third of the run into hits.

import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';

import { published, url, common, describe, guard } from './lib/dataset.js';

export const options = {
  ...common,
  scenarios: {
    uncached: {
      executor: 'shared-iterations',
      vus: Number(__ENV.VUS || 20),
      iterations: Number(__ENV.ITERATIONS || published.length),
      maxDuration: __ENV.DURATION || '10m',
    },
  },
  thresholds: {
    http_req_waiting: ['p(95)<800'],
    http_req_failed: ['rate==0'],
    rate_limited: ['count==0'],
    checks: ['rate==1'],
  },
};

export function setup() {
  describe('uncached-delivery', 'NFR-2: uncached public page TTFB < 800 ms p95');

  if (published.length < 200) {
    throw new Error(
      `The manifest carries only ${published.length} URLs. Reseed with a larger --manifest-sample; ` +
        'a few dozen pages cannot fill a run without repeating, and a repeat is a cache hit.',
    );
  }
}

export default function () {
  const path = published[exec.scenario.iterationInTest % published.length];
  const response = guard(http.get(url(path)));

  check(response, {
    'answered 200': (r) => r.status === 200,
    'served a page': (r) => r.body.includes('class="cms-page'),
  });
}
