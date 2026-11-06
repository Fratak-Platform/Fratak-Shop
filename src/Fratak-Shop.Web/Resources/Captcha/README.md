# مدل خواندن کد امنیتی رهگیری پست (`digits.onnx`)

این پوشه مدل کوچکی دارد که کد امنیتیِ عددیِ سایت رهگیری پست (`tracking.post.ir`) را **آفلاین و رایگان** می‌خواند.
در این کار هیچ سرویس بیرونی، API پولی یا هوش مصنوعی آنلاینی استفاده نمی‌شود.

| مورد | مقدار |
|---|---|
| استفاده در کد | `Infrastructure/Shipping/OnnxCaptchaSolver.cs` ← `PostTrackingClient` |
| ورودی | `input1`: float32 `[1, 1, 64, W]` (تصویر خاکستری، ارتفاع ۶۴، مقدار ۰ تا ۱) |
| خروجی | float32 `[T, 1, 11]` (امتیاز هر ستون زمانی؛ کلاس ۰ = خالی و کلاس i = رقم i−1) |
| رمزگشایی | CTC (`Core/Services/Orders/PostCaptcha.cs` ← `CtcDigitDecoder`) |
| حجم | 5,177,455 بایت |
| SHA-256 | `00e9cfceda2e88eb6061e1a7bb5847e2fdc7912434fbebf601a4b6a37fd0be84` |

## از کجا آمده؟

این فایل از مدل `common_old.onnx` در بستهٔ متن‌باز [ddddocr](https://github.com/sml2h3/ddddocr) نسخهٔ 1.6.1 (مجوز MIT) ساخته شده است.
مدل اصلی برای هر ستون، امتیاز ۸۲۱۰ نویسه را می‌دهد (چینی، لاتین، رقم و غیره). برای کپچای پست فقط رقم لازم است.
پس از لایهٔ آخر (`MatMulInteger` با وزن `135_quantized` و بایاس `136`) فقط ۱۱ ستون نگه داشته شد:
ستون «خالی» (۰) و ستون‌های ارقام `0..9`. این ستون‌ها در فهرست نویسه‌های مدل اصلی به ترتیب این اندیس‌ها را دارند:
`6749, 4410, 78, 7721, 5806, 6977, 5961, 409, 6979, 2879`.

بقیهٔ شبکه دست نخورده است. به همین خاطر خروجی دقیقاً برابر است با «مدل اصلی + محدود کردن پاسخ به رقم».

## دقت (آزمون آفلاین با تصویرهای ساختگی نویزدار)

- کپچای ۴ رقمی: ۱۰۰ از ۱۰۰ درست.
- با روش‌های مختلف تغییر اندازه (bilinear، bicubic و lanczos): ۸۰ از ۸۰ درست.
- مخلوط سخت‌تر با کدهای ۵ رقمی: ۸۶ از ۱۰۰ درست.

`PostTrackingClient` هر بار تا ۳ تلاش با تصویر تازه انجام می‌دهد. جواب مشکوک (کمتر از ۴ یا بیشتر از ۶ رقم) اصلاً فرستاده نمی‌شود.
چهار تصویر نمونه با جواب معلوم در `tests/Fratak-Shop.Tests/Fixtures/Captcha/` هست و تست‌ها مدل را روی آن‌ها اجرا می‌کنند.

## اگر روزی سایت پست کپچا را عوض کرد

1. چند تصویر واقعی را با جوابشان در `tests/Fratak-Shop.Tests/Fixtures/Captcha/` بگذارید. نام هر فایل همان جواب باشد، مثلاً `4058.png`.
2. اگر تست‌ها رد شدند، یکی از دو راه زیر را بروید:
   - یک مدل ONNX دیگر با همین ورودی و خروجی جایگزین این فایل کنید.
   - پیش‌پردازش `OnnxCaptchaSolver.Prepare` را تنظیم کنید.

## مجوز مدل اصلی (MIT)

```
The MIT License (MIT)
Copyright © 2022 sml2h3 (ddddocr)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the “Software”), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
