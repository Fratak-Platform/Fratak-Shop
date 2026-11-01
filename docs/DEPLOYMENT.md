# راهنمای انتشار روی سرور (faratek.ir)

این راهنما همهٔ کارهای لازم برای بالا آوردن فروشگاه روی هاست/سرور واقعی با دامنهٔ **faratek.ir** را به ترتیب توضیح می‌دهد.
خلاصهٔ کار: انتشار (publish) ← تنظیم متغیرهای محیطی ← وب‌سرور (Nginx یا IIS) و گواهی SSL ← اولین اجرا ← ورود مدیر پیش‌فرض با رمز اولیه و تعویض فوری آن.

---

## ۱. پیش‌نیازها

| مورد | توضیح |
|------|-------|
| .NET 10 | روی سرور: **ASP.NET Core Runtime 10** (لینوکس) یا **Hosting Bundle 10** (ویندوز/IIS). روی سیستم توسعه: .NET SDK 10 |
| SQL Server | نسخهٔ 2019 به بعد (Express هم کافی است). یک دیتابیس خالی و یک کاربر SQL با دسترسی `db_owner` روی همان دیتابیس |
| دامنه و SSL | رکورد A برای `faratek.ir` و `www.faratek.ir` به IP سرور؛ گواهی SSL (Let's Encrypt رایگان یا گواهی هاست) |
| پیامک | کلید API پنل **SMS.ir** و یک قالب «کد تأیید» با پارامتر `Code` (کد ورود، فراموشی رمز و ورود دومرحله‌ای پیامکی) |

---

## ۲. ساخت خروجی انتشار

```bash
# (فقط اگر کلاس Tailwind تازه اضافه کرده‌اید؛ output.css در مخزن هست)
cd src/Fratak-Shop.Web && npm ci && npm run build:css && cd ../..

# لینوکس
dotnet publish src/Fratak-Shop.Web -c Release -r linux-x64 --self-contained false -o ./publish
# ویندوز / IIS
dotnet publish src/Fratak-Shop.Web -c Release -r win-x64 --self-contained false -o ./publish
```

* حتماً `-r linux-x64` یا `-r win-x64` بدهید: بدون آن، کتابخانه‌های بومی همهٔ سیستم‌عامل‌ها (OnnxRuntime برای کپچای پست، SqlClient و …) در خروجی کپی می‌شوند
  و حجم چند برابر می‌شود.
* محتوای پوشهٔ `publish` را روی سرور (مثلاً `/var/www/faratek` یا `C:\inetpub\faratek`) کپی کنید.
* پوشه‌های `wwwroot/uploads` و `App_Data` روی سرور **دادهٔ واقعی** هستند (تصاویر، کلیدها، پشتیبان‌ها، گزارش‌ها).
  هنگام به‌روزرسانی آن‌ها را پاک یا جایگزین نکنید و در پشتیبان‌گیری سرور بگذارید.

---

## ۳. تنظیمات و متغیرهای محیطی

رمزها و کلیدها **هرگز** در `appsettings.json` نوشته نشوند؛ روی سرور با متغیر محیطی تنظیم شوند
(در نام متغیر محیطی، `:` به `__` تبدیل می‌شود).

| متغیر | لازم؟ | توضیح |
|-------|-------|-------|
| `ASPNETCORE_ENVIRONMENT` | بله | `Production` (فایل `appsettings.Production.json` خوانده می‌شود: دامنهٔ اصلی faratek.ir، دادهٔ نمایشی خاموش) |
| `ConnectionDefault` | بله | رشتهٔ اتصال SQL Server. مثال: `Server=localhost;Database=FaratekShop;User Id=faratek;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=true` |
| `SmsIr__ApiKey` | بله (تا وقتی در پنل ثبت نشده) | کلید SMS.ir. بدون آن کد ورود/فراموشی رمز فرستاده نمی‌شود و فقط ورود با رمز ممکن است (در لاگ هشدار Critical ثبت می‌شود). بعد از ورود، کلید را می‌توان در «سیستم ← پیامک» پنل ثبت کرد |
| `Security__BehindProxy` | پشت Nginx: بله | `true` وقتی برنامه پشت Nginx/Cloudflare است (بخش ۴). **بدون آن، پشت Nginx حلقهٔ بی‌پایان تغییر مسیر به https رخ می‌دهد** چون برنامه درخواست را http می‌بیند |
| `Security__KnownProxies__0` | اختیاری | IP پراکسی اگر روی همان سرور نیست (loopback به‌طور پیش‌فرض مورد اعتماد است) |
| `Jwt__SigningKey` | توصیه‌شده | رشتهٔ تصادفی حداقل ۳۲ کاراکتری. اگر خالی باشد، کلید موقت ساخته می‌شود و با هر ری‌استارت توکن‌های دسترسی باطل و بی‌صدا با Refresh Token تمدید می‌شوند (کاربر خارج نمی‌شود) |
| `Email__Password` | اختیاری | فقط اگر رمز SMTP را نخواهید در پنل («سیستم ← ایمیل») ذخیره کنید |
| `Shop__DefaultAdmins__0__Password` | توصیه‌شده | رمز اولیهٔ مدیر پیش‌فرض اول (به‌جای نوشتن در فایل؛ بخش «مدیران پیش‌فرض» پایین‌تر) |
| `Shop__CanonicalHost` | اختیاری | پیش‌فرض Production: `faratek.ir`. درخواست‌های `www.faratek.ir` و دامنه/زیر دامنه‌های دیگر با 301 به `https://faratek.ir` می‌روند (localhost، IP، `/health*` و `/.well-known` استثنا هستند). برای غیرفعال‌کردن `off` بدهید (مقدار بدون نقطه یعنی خاموش؛ در ویندوز متغیر محیطی خالی اصلاً وجود ندارد) |

### دو پرچم داده (در `appsettings*.json`، بخش `Shop`)

| کلید | پیش‌فرض Production | رفتار |
|------|-------------------|-------|
| `SeedDefaultData` | `true` | **فقط وقتی دیتابیس کاملاً تازه است** (هیچ تنظیمات، محصول یا دسته‌ای حتی حذف‌شده وجود ندارد) دادهٔ پیش‌فرض سیستم ساخته می‌شود: تنظیمات و نام فروشگاه، همهٔ صفحات (درباره ما، قوانین، حریم خصوصی، بازگشت کالا، گارانتی، ارسال، پرداخت، راهنمای خرید، قوانین باشگاه)، سؤالات متداول، منوها، صفحهٔ اصلی، مترادف‌ها و جست‌وجوهای پیشنهادی، دلایل مرجوعی، دپارتمان‌های پشتیبانی، قالب اعلان‌ها و پیامک، نقش‌ها و راهنمای پنل. در اجراهای بعدی **هرگز** دوباره ساخته نمی‌شود، حتی اگر مدیر چیزی را حذف کرده باشد |
| `SeedDemoData` | `false` | `true` = دادهٔ نمایشی (محصول، دسته، برند، بنر، مقاله، کد تخفیف، کاربران نمونه با رمز 123456). روی سایت واقعی `false` بماند؛ صفحهٔ «سلامت سیستم» اگر حساب نمایشی پیدا کند هشدار می‌دهد |

### مدیران پیش‌فرض (`Shop:DefaultAdmins`)

هر تعداد مدیر را می‌توانید تعریف کنید؛ هر کدام **فقط یک بار** (اولین اجرایی که آن شماره را می‌بیند) با رمز اولیه‌اش ساخته می‌شود.
پیش‌فرض `appsettings.json`: **محمدامین ناظری – 09013181090 – رمز `12345678`** با همهٔ دسترسی‌ها.

```json
"Shop": {
  "DefaultAdmins": [
    { "PhoneNumber": "09013181090", "FirstName": "محمدامین", "LastName": "ناظری", "Password": "12345678", "FullAccess": true },
    { "PhoneNumber": "09121111111", "FirstName": "سارا", "LastName": "احمدی", "Password": "رمز-اولیهٔ-قوی", "FullAccess": false, "Roles": [ "پشتیبانی" ] }
  ]
}
```

| فیلد | توضیح |
|------|-------|
| `PhoneNumber` | موبایل (۰۹…، ‎+98… یا رقم فارسی). نامعتبر ← آن مورد نادیده گرفته و در لاگ گزارش می‌شود |
| `FirstName`, `LastName` | نام (فقط هنگام ساخت حساب؛ نام حساب موجود عوض نمی‌شود) |
| `Password` | رمز اولیه، **۸ تا ۱۰۰ نویسه**؛ خالی = بدون رمز (ورود با کد پیامکی). رمز کوتاه‌تر ← آن مورد ساخته نمی‌شود |
| `FullAccess` | `true` (پیش‌فرض) = مالک با همهٔ دسترسی‌ها؛ `false` = فقط نقش‌های `Roles` |
| `Roles` | نام نقش‌های پنل (مثل «پشتیبانی»، «حسابداری و مالی»)؛ نقش ناشناخته در لاگ گزارش می‌شود |

قانون‌های امنیتی:

* رمز با **Argon2id** ذخیره می‌شود و هرگز در لاگ نمی‌آید (لاگ فقط `Default admin: ساخته شد 0901***1090 …`).
* **رمز موجود هرگز بازنویسی نمی‌شود.** فقط حسابی که اصلاً رمز ندارد (مثلاً قبلاً با کد پیامکی ثبت‌نام کرده) یک بار رمز اولیه می‌گیرد.
* حسابی که از پنل **حذف** شده دوباره ساخته نمی‌شود؛ حساب **مسدود** دست نمی‌خورد.
* بعد از ورود با رمز اولیه، کاربر مستقیم به «پروفایل ← رمز عبور» فرستاده می‌شود و تا وقتی رمز اولیه عوض نشده،
  «سلامت سیستم» هشدار «رمز مدیران پیش‌فرض» نشان می‌دهد.
* روی سرور واقعی رمز را در فایل ننویسید؛ با متغیر محیطی بدهید (عدد = جایگاه در فهرست، از ۰):
  `Shop__DefaultAdmins__0__Password=...`، مدیر دوم: `Shop__DefaultAdmins__1__PhoneNumber=...` و `Shop__DefaultAdmins__1__Password=...`.
* فهرست در `appsettings.Production.json` جایگاه‌به‌جایگاه با `appsettings.json` ادغام می‌شود (جایگزین کامل نمی‌شود).
  برای حذف یک مدیر پیش‌فرض، او را از `appsettings.json` بردارید و حسابش را از پنل مسدود/حذف کنید.

---

## ۴. لینوکس (Ubuntu) با Nginx و systemd

### سرویس systemd — `/etc/systemd/system/faratek.service`

```ini
[Unit]
Description=Faratek Shop
After=network.target

[Service]
WorkingDirectory=/var/www/faratek
ExecStart=/usr/bin/dotnet /var/www/faratek/Fratak-Shop.Web.dll --urls http://127.0.0.1:5000
Restart=always
RestartSec=5
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Security__BehindProxy=true
EnvironmentFile=/etc/faratek.env

[Install]
WantedBy=multi-user.target
```

`/etc/faratek.env` (دسترسی `chmod 600`، مالک root):

```ini
ConnectionDefault=Server=localhost;Database=FaratekShop;User Id=faratek;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=true
SmsIr__ApiKey=...
Jwt__SigningKey=...
```

```bash
sudo chown -R www-data:www-data /var/www/faratek      # App_Data و wwwroot/uploads باید قابل نوشتن باشند
sudo systemctl daemon-reload && sudo systemctl enable --now faratek
journalctl -u faratek -f                                # لاگ زنده
```

### Nginx — `/etc/nginx/sites-available/faratek`

```nginx
server {
    listen 80;
    server_name faratek.ir www.faratek.ir;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://faratek.ir$request_uri; }
}

server {
    listen 443 ssl http2;
    server_name faratek.ir www.faratek.ir;
    ssl_certificate     /etc/letsencrypt/live/faratek.ir/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/faratek.ir/privkey.pem;

    client_max_body_size 50m;          # بزرگ‌ترین آپلود پنل (کتابخانهٔ رسانه) ۴۲ مگابایت است

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_set_header   X-Forwarded-Host $host;
        proxy_read_timeout 120s;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/faratek /etc/nginx/sites-enabled/
sudo certbot certonly --webroot -w /var/www/certbot -d faratek.ir -d www.faratek.ir
sudo nginx -t && sudo systemctl reload nginx
```

> اگر Cloudflare یا CDN جلوی Nginx است، IP های آن را در `Security__KnownProxies__N` بگذارید و `ForwardLimit` را در نظر بگیرید.

---

## ۵. ویندوز / IIS (هاست‌های Plesk)

1. **ASP.NET Core Hosting Bundle 10** را نصب کنید (یا از پشتیبانی هاست بخواهید).
2. محتوای `publish` (ساخته‌شده با `-r win-x64`) را در ریشهٔ سایت کپی کنید؛ `web.config` خودکار ساخته شده است.
3. Application Pool: «No Managed Code»، Identity باید روی `App_Data` و `wwwroot\uploads` اجازهٔ نوشتن داشته باشد.
4. متغیرهای محیطی را در `web.config` (بخش `<aspNetCore><environmentVariables>`) یا تنظیمات Plesk بگذارید:

```xml
<aspNetCore processPath="dotnet" arguments=".\Fratak-Shop.Web.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="ConnectionDefault" value="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=true" />
    <environmentVariable name="SmsIr__ApiKey" value="..." />
  </environmentVariables>
</aspNetCore>
```

* روی IIS **`Security__BehindProxy` لازم نیست** (ماژول ASP.NET Core خودش آدرس و https را درست می‌دهد).
* SSL را در پنل هاست برای هر دو `faratek.ir` و `www.faratek.ir` فعال کنید؛ تغییر مسیر www را برنامه انجام می‌دهد.
* `web.config` را در مخزن نگه ندارید؛ اگر رمز در آن است، فقط روی سرور باشد.

---

## ۶. اولین اجرا

1. برنامه را اجرا کنید. Migration ها خودکار اعمال می‌شوند، دیکشنری استان/شهر و (چون دیتابیس تازه است) دادهٔ پیش‌فرض ساخته می‌شود.
   در لاگ: `New database: the default store data was created`.
   در لاگ برای هر مدیر پیش‌فرض: `Default admin: ساخته شد 0901***1090 (مالک، همهٔ دسترسی‌ها) با رمز اولیه.`
2. به `https://faratek.ir/login` بروید، «ورود با رمز عبور»: شمارهٔ **09013181090** و رمز **12345678** (یا رمزی که با متغیر محیطی دادید).
3. بلافاصله به «پروفایل ← رمز عبور» فرستاده می‌شوید: **رمز تازه و قوی بگذارید** (تا عوض نشود، «سلامت سیستم» هشدار می‌دهد).
   پیشنهاد جدی: در «پنل ← امنیت حساب من» (`/admin/account`) ورود دومرحله‌ای با برنامه (TOTP) را روشن کنید.
4. در پنل `/admin`:
   * **سیستم ← سلامت سیستم**: همهٔ موارد را سبز کنید (اتصال دیتابیس، پوشه‌های قابل نوشتن، پیامک، ایمیل، اطلاعات تماس، دامنهٔ اصلی، منابع قیمت و سئو).
   * **تنظیمات ← پرداخت**: مرچنت زرین‌پال را وارد و **حالت آزمایشی (Sandbox) را خاموش** کنید (پیش‌فرض برای امنیت روشن است).
   * **تنظیمات ← عمومی/تماس**: تلفن، ایمیل، آدرس و نماد اعتماد (اینماد) واقعی فروشگاه.
   * **سیستم ← پیامک** و **سیستم ← ایمیل**: اتصال و ارسال آزمایشی.
   * **تیم**: کارمندان را با نقش مناسب اضافه کنید.

---

## ۷. پیشنهاد قیمت و قیمت‌گذاری بازار

* دکمهٔ «پیشنهاد قیمت فروش» (فرم محصول) و «درون‌ریزی از سایت» با گزینهٔ پیش‌فرض **«بر اساس قیمت بازار»** قیمت همان کالا را از
  **ترب، دیجی‌کالا و باسلام** می‌خوانند و قیمت فروش را کمی ارزان‌تر از ارزان‌ترین رقیب معتبر، ولی **هرگز کمتر از «کمترین سود»** تنظیمات می‌گذارند.
* درون‌ریزی: محصول اول با قیمت «سود هدف» ساخته می‌شود (هیچ‌وقت بی‌قیمت نمی‌ماند) و کار پس‌زمینهٔ «قیمت‌گذاری بازار درون‌ریزی»
  آن را از روی بازار اصلاح می‌کند؛ نتیجه در جدول پایین همان صفحه. اگر کارمند قیمت را دستی عوض کرده باشد، دست نمی‌خورد.
* **محدودیت منابع:** این سایت‌ها درخواست زیاد را با کپچا (ترب: کد ۴۹۰)، ۴۲۹ یا ۴۰۳ رد می‌کنند. برنامه کپچا را دور نمی‌زند (غیرقانونی/ناپایدار و باعث مسدود شدن IP سرور)؛
  در عوض: یک درخواست هم‌زمان برای هر منبع با فاصلهٔ تصادفی، استراحت دوبرابرشونده پس از محدودیت (۱۰ دقیقه تا ۲ ساعت، رعایت Retry-After)،
  کش ۳۰ دقیقه‌ای در حافظه + کش ماندگار در دیتابیس (تازه تا ۶ ساعت، پشتیبان تا ۳ روز)، منبع سوم (باسلام) و صف با تلاش دوباره (۱۵ دقیقه تا ۴ ساعت، حداکثر ۶ بار).
  وضعیت هر منبع در «سلامت سیستم ← منابع قیمت و سئو» دیده می‌شود. موتور سئو (پیشنهادهای گوگل/بینگ/داک‌داک‌گو و جست‌وجوی رقبا) از همین دروازه استفاده می‌کند.
* سرورهای خارج از ایران ممکن است از ترب/دیجی‌کالا پاسخ نگیرند؛ هاست داخل ایران بهترین نتیجه را می‌دهد.

---

## ۸. به‌روزرسانی

```bash
sudo systemctl stop faratek
rsync -a --delete --exclude 'App_Data/' --exclude 'wwwroot/uploads/' --exclude 'appsettings.*.local.json' ./publish/ /var/www/faratek/
sudo systemctl start faratek       # Migration های تازه خودکار اعمال می‌شوند
```

پیش از هر به‌روزرسانی از دیتابیس پشتیبان بگیرید («سیستم ← پشتیبان‌گیری» یا ابزار SQL Server).

---

## ۹. حجم پروژه و پاک‌سازی

خود مخزن کد حدود **۳۰ مگابایت** است (بزرگ‌ترین فایل‌ها: مدل کپچای پست ۵ مگ، کتابخانه‌های JS، تصاویر نمونه و Migration ها).
حجم ~۱ گیگابایتی پوشهٔ پروژه روی سیستم توسعه از فایل‌های ساختنی است، نه از کد:

| پوشه | چرا بزرگ است | در Git؟ |
|------|---------------|--------|
| `bin/`, `obj/` هر پروژه | خروجی Debug/Release هر ۴ پروژه. پیش از مرحلهٔ ۱۴ کتابخانه‌های بومی OnnxRuntime/SkiaSharp برای **همهٔ** سیستم‌عامل‌ها و Roslyn ابزار EF در bin برنامه و bin تست‌ها کپی می‌شد | خیر (ignore) |
| `src/Fratak-Shop.Web/node_modules` | Tailwind و وابستگی‌هایش (فقط برای ساخت CSS) | خیر |
| `.vs/` | کش IntelliSense ویژوال استودیو (گاهی چند صد مگ) | خیر |
| `TestResults/`, `publish/` | خروجی تست و انتشار | خیر |
| `wwwroot/uploads/` | تصاویر آپلود/درون‌ریزی‌شده (دادهٔ کاربر) | خیر (از این مرحله کل پوشه ignore شد) |

پاک‌سازی امن (فقط فایل‌های ساختنی؛ کد و `uploads` دست نمی‌خورند):

```bash
tools/clean.sh               # لینوکس/مک (‎--keep-node برای نگه داشتن node_modules، ‎--report فقط گزارش حجم)
pwsh tools/clean.ps1         # ویندوز (‎-KeepNode، ‎-Report فقط گزارش حجم)
```

کنترل حجم از مرحلهٔ ۱۴ (در `Directory.Build.props` و csproj ها):

* **فقط کتابخانه‌های بومی همین دستگاه:** build/test/run برنامه و تست‌ها با RID همین سیستم (`win-x64` روی ویندوز، `linux-x64` روی CI) انجام می‌شود؛
  خروجی در `bin/<Configuration>/net10.0/<rid>/`. فایل‌های بومی ده‌ها مگابایتی لینوکس/مک/ARM دیگر روی ویندوز کپی نمی‌شوند.
  `dotnet publish` دست نمی‌خورد (بدون ‎`-r` قابل‌حمل، با ‎`-r linux-x64` فقط لینوکس). build قابل‌حمل: ‎`-p:FratakPortableBuild=true`.
* **ابزار EF (`Microsoft.EntityFrameworkCore.Tools`) به پروژهٔ Data منتقل شد**؛ Roslyn و BuildHost دیگر در bin برنامه و تست‌ها نیستند.
  ساخت Migration: `dotnet ef migrations add Name --project src/Fratak-Shop.Data --startup-project src/Fratak-Shop.Data`
  (در Package Manager Console ویژوال استودیو: `Add-Migration Name -Project Fratak-Shop.Data -StartupProject Fratak-Shop.Data`).
* فقط فایل‌های زبان انگلیسی و فارسی در خروجی (`SatelliteResourceLanguages`).
* پشتیبان دیتابیس روی خود SQL Server ذخیره می‌شود (نه در پوشهٔ پروژه)؛ پشتیبان zip تصاویر پیش‌فرض خاموش است.

---

## ۱۰. چک‌لیست نهایی پیش از اعلام عمومی

- [ ] `https://faratek.ir` باز می‌شود؛ `http://` و `www` با یک 301 به `https://faratek.ir` می‌روند.
- [ ] ورود مدیر پیش‌فرض با رمز اولیه، **تعویض رمز** و روشن کردن TOTP؛ هشدار «رمز مدیران پیش‌فرض» در «سلامت سیستم» نیست.
- [ ] زرین‌پال: Sandbox خاموش، یک پرداخت واقعی کوچک و بازگشت موفق.
- [ ] `Shop:SeedDemoData = false` و هیچ حساب نمایشی در «سلامت سیستم» گزارش نمی‌شود.
- [ ] پشتیبان‌گیری خودکار روشن و یک بازگردانی آزمایشی انجام شده.
- [ ] `App_Data` و `wwwroot/uploads` در پشتیبان سرور هستند.
- [ ] `/robots.txt` و `/sitemap.xml` آدرس‌های `https://faratek.ir` را نشان می‌دهند (اگر `http://` دیدید، `Security__BehindProxy` پشت Nginx تنظیم نشده است).
