// دور نوزدهم (A29): آزمون بار فروشگاه با k6 (رایگان و متن‌باز).
// سناریو: تا ۱۰۰۰ کاربر هم‌زمان (مهمان) در صفحه‌های اصلی می‌گردند؛ هر کاربر هر ۱ تا ۳ ثانیه یک صفحه باز می‌کند.
// شرط قبولی: هیچ پاسخ ۵xx (حتی یکی) و هیچ خطای اتصال؛ زمان پاسخ فقط گزارش می‌شود (روی سرور CI متغیر است).
// اجرا (همان کاری که GitHub Actions می‌کند – tools/load/run-load-test.sh):
//   docker run --rm --network host -v "$PWD/tools/load:/scripts" -e BASE=http://localhost:5080 grafana/k6 run /scripts/storefront.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE || 'http://localhost:5080';
const PEAK = parseInt(__ENV.PEAK || '1000', 10);

// شمارندهٔ پاسخ‌های ۵xx و خطاهای اتصال (status 0)؛ آستانهٔ هر دو صفر است.
const serverErrors = new Counter('server_errors');
const connectionErrors = new Counter('connection_errors');
const snapshots = new Counter('snapshot_responses');

export const options = {
  discardResponseBodies: true,
  scenarios: {
    shoppers: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '45s', target: Math.round(PEAK / 4) },
        { duration: '45s', target: PEAK },
        { duration: '90s', target: PEAK },
        { duration: '20s', target: 0 },
      ],
      gracefulRampDown: '15s',
    },
  },
  thresholds: {
    server_errors: ['count==0'],
    connection_errors: ['count==0'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// همان صفحه‌هایی که آزمون دود (smoke) هم باز می‌کند؛ وزن صفحه‌های پربازدید بیشتر است.
const PAGES = [
  '/', '/', '/', '/',
  '/product/xiaomi-redmi-watch-3-active', '/product/xiaomi-redmi-watch-3-active', '/product/sony-playstation-5',
  '/category/smart-watch', '/category/laptop', '/brand/apple', '/products',
  '/search?q=%D8%A7%D9%BE%D9%84', '/search?sort=BestSelling',
  '/blog', '/blog/headphone-life-tips', '/faq', '/contact-us',
  '/assets/css/output.css', '/assets/js/shop.js',
];

export default function () {
  const path = PAGES[Math.floor(Math.random() * PAGES.length)];
  const response = http.get(BASE + path, { tags: { page: path.split('?')[0] }, timeout: '60s' });
  if (response.status === 0)
    connectionErrors.add(1, { page: path });
  else if (response.status >= 500) {
    serverErrors.add(1, { page: path, status: String(response.status) });
    // چند نمونه برای عیب‌یابی (نه همه، تا خروجی شلوغ نشود).
    if (__VU % 50 === 0)
      console.error(`5xx ${response.status} ${path} retry-after=${response.headers['Retry-After'] || '-'} ${response.timings.duration.toFixed(0)}ms`);
  }
  if (response.headers['X-Fratak-Snapshot'])
    snapshots.add(1);
  check(response, { 'no 5xx': (r) => r.status > 0 && r.status < 500 });
  sleep(1 + Math.random() * 2);
}
