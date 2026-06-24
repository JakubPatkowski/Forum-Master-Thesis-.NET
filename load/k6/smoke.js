import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

export const options = {
  scenarios: {
    smoke: { executor: 'constant-vus', vus: 5, duration: '30s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/health/ready`);
  check(res, { 'status 200': (r) => r.status === 200 });
  sleep(1);
}
