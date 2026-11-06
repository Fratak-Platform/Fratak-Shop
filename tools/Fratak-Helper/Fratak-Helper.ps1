<#
.SYNOPSIS
    دستیار محلی فراتک (دور نوزدهم، A31) – روی لپ‌تاپ/کامپیوتر خود مدیر اجرا می‌شود، نه روی سرور.

.DESCRIPTION
    چرا لازم است؟
      بعضی سایت‌ها (دیجی‌کالا، ترب، …) درخواست‌های هاست اشتراکی را مسدود می‌کنند یا کپچا نشان می‌دهند؛
      هاست ضعیف هم توان اجرای مدل زبانی را ندارد. این برنامه با اینترنت و پردازندهٔ خود شما همان کارها را انجام می‌دهد.

    چطور کار می‌کند؟ (فقط اتصال خروجی؛ هیچ پورتی روی کامپیوتر شما باز نمی‌شود)
      ۱. GET  {Site}/api/helper/next    ← سرور تا ۲۵ ثانیه منتظر می‌ماند؛ اگر کاری بود برمی‌گرداند (۲۰۰)، وگرنه ۲۰۴.
      ۲. کار را انجام می‌دهد:
           kind = "fetch" ← دریافت یک صفحه/JSON از اینترنت (فقط http/https، بدون آدرس داخلی شبکه، حداکثر ۳ مگابایت).
           kind = "llm"   ← بازنویسی روان یک متن با Ollama محلی (اختیاری؛ اگر Ollama نصب نباشد خطا برمی‌گرداند).
      ۳. POST {Site}/api/helper/result  ← نتیجه را می‌فرستد. سرور خروجی مدل زبانی را فقط اگر از «محافظ متن» رد شود می‌پذیرد.
      هدرهای هر درخواست: X-Helper-Key (کلید ساخته‌شده در «تنظیمات محتوای هوشمند») و X-Helper-Name (نام این دستگاه).

    امنیت:
      • کلید فقط در حافظهٔ همین پنجره است؛ در فایل یا تاریخچه ذخیره نمی‌شود (مگر خودتان متغیر محیطی FRATAK_HELPER_KEY بسازید).
      • سرور فقط آدرس می‌فرستد؛ این برنامه هیچ فرمانی اجرا نمی‌کند و فقط صفحه می‌خواند.
      • آدرس‌های localhost، 127.x، 10.x، 192.168.x، 172.16-31.x و 169.254.x رد می‌شوند (سرور نمی‌تواند شبکهٔ خانگی شما را بگردد).
      • با «ابطال کلید» در پنل، دستیار بلافاصله قطع می‌شود (۴۰۱) و برنامه پیام می‌دهد.

.EXAMPLE
    # ساده‌ترین حالت (کلید پرسیده می‌شود):
    powershell -ExecutionPolicy Bypass -File .\Fratak-Helper.ps1

.EXAMPLE
    # با Ollama محلی و نام دلخواه:
    .\Fratak-Helper.ps1 -Site https://shop.fratak.ir -Name "لپ‌تاپ دفتر" -OllamaModel "qwen2.5:7b"

.NOTES
    نیازمندی: Windows PowerShell 5.1 یا PowerShell 7 (هر دو در ویندوز ۱۰/۱۱ هست). برای قطع: Ctrl+C.
#>
[CmdletBinding()]
param(
    # نشانی فروشگاه (بدون / در انتها).
    [string] $Site = "https://shop.fratak.ir",

    # کلید دستیار؛ اگر خالی باشد از متغیر محیطی FRATAK_HELPER_KEY یا با پرسش امن خوانده می‌شود.
    [string] $Key = $env:FRATAK_HELPER_KEY,

    # نامی که در پنل نمایش داده می‌شود.
    [string] $Name = $env:COMPUTERNAME,

    # نشانی Ollama محلی (اختیاری).
    [string] $OllamaUrl = "http://127.0.0.1:11434",

    # مدل Ollama؛ خالی = قابلیت بازنویسی متن خاموش.
    [string] $OllamaModel = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
# TLS 1.2 (و اگر ویندوز پشتیبانی کند 1.3). در PowerShell قدیمی Tls13 وجود ندارد؛ آن‌جا فقط 1.2 فعال می‌شود.
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13 }
catch { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 }
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$MaxBodyChars = 3000000
$UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"

function Write-Log([string] $Message, [string] $Color = "Gray") {
    Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message) -ForegroundColor $Color
}

# کلید را اگر داده نشده، به‌صورت امن (بدون نمایش روی صفحه) می‌پرسد.
if ([string]::IsNullOrWhiteSpace($Key)) {
    $secure = Read-Host "کلید دستیار (از پنل ← تنظیمات محتوای هوشمند)" -AsSecureString
    $Key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if ([string]::IsNullOrWhiteSpace($Key)) { Write-Log "کلید وارد نشد." "Red"; exit 1 }
$Site = $Site.TrimEnd("/")
if ($Name.Length -gt 60) { $Name = $Name.Substring(0, 60) }

# نام فارسی در هدر HTTP مجاز نیست؛ به‌صورت درصدی (URL-encoded) فرستاده می‌شود.
$Headers = @{ "X-Helper-Key" = $Key; "X-Helper-Name" = [Uri]::EscapeDataString($Name) }

<#
  آیا این آدرس عمومی و امن است؟ فقط http/https و میزبان غیرداخلی.
  مثال: https://www.digikala.com/... ← درست؛ http://192.168.1.1/ ← نادرست.
#>
function Test-PublicUrl([string] $Url) {
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref] $uri)) { return $false }
    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") { return $false }
    # فقط پورت‌های معمول وب (80/443 و 8080/8443)؛ مثال: http://site.ir:22 رد می‌شود.
    if (-not $uri.IsDefaultPort -and $uri.Port -ne 8080 -and $uri.Port -ne 8443) { return $false }
    $hostName = $uri.DnsSafeHost
    if ($hostName -eq "localhost" -or $hostName.EndsWith(".local") -or $hostName.EndsWith(".lan")) { return $false }
    try { $addresses = [Net.Dns]::GetHostAddresses($hostName) } catch { return $false }
    foreach ($ip in $addresses) {
        if ([Net.IPAddress]::IsLoopback($ip)) { return $false }
        $b = $ip.GetAddressBytes()
        if ($ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) {
            if ($b[0] -eq 10 -or $b[0] -eq 0 -or $b[0] -eq 127) { return $false }
            if ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) { return $false }
            if ($b[0] -eq 192 -and $b[1] -eq 168) { return $false }
            if ($b[0] -eq 169 -and $b[1] -eq 254) { return $false }
            if ($b[0] -eq 100 -and $b[1] -ge 64 -and $b[1] -le 127) { return $false }
        }
        elseif ($ip.IsIPv6LinkLocal -or $ip.IsIPv6SiteLocal -or ($b[0] -band 0xFE) -eq 0xFC) { return $false }
    }
    return $true
}

# kind = fetch: دریافت صفحه. خروجی: @{ status; body; error }.
function Invoke-Fetch($Job) {
    if (-not (Test-PublicUrl $Job.url)) { return @{ status = 0; body = $null; error = "آدرس غیرمجاز (داخلی یا نامعتبر)." } }
    $accept = if ([string]::IsNullOrWhiteSpace($Job.accept)) { "text/html,application/json;q=0.9,*/*;q=0.8" } else { $Job.accept }
    try {
        $response = Invoke-WebRequest -Uri $Job.url -UseBasicParsing -TimeoutSec 25 -MaximumRedirection 3 `
            -Headers @{ "Accept" = $accept; "Accept-Language" = "fa-IR,fa;q=0.9,en;q=0.5" } -UserAgent $UserAgent
        $body = [string] $response.Content
        if ($body.Length -gt $MaxBodyChars) { return @{ status = 0; body = $null; error = "صفحه بیش از حد بزرگ بود." } }
        return @{ status = [int] $response.StatusCode; body = $body; error = $null }
    }
    catch {
        $code = 0
        if ($_.Exception.Response -ne $null) { try { $code = [int] $_.Exception.Response.StatusCode } catch { } }
        return @{ status = $code; body = $null; error = $_.Exception.Message }
    }
}

# kind = llm: بازنویسی با Ollama محلی. سرور خروجی را دوباره با محافظ متن می‌سنجد (عدد عوض نشود، لینک/HTML نداشته باشد، …).
function Invoke-Llm($Job) {
    if ([string]::IsNullOrWhiteSpace($OllamaModel)) { return @{ status = 0; body = $null; error = "Ollama تنظیم نشده است." } }
    try {
        $payload = @{ model = $OllamaModel; prompt = [string] $Job.prompt; stream = $false; options = @{ temperature = 0.4 } } | ConvertTo-Json -Depth 5
        $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
        $result = Invoke-RestMethod -Uri "$OllamaUrl/api/generate" -Method Post -Body $bytes -ContentType "application/json; charset=utf-8" -TimeoutSec 120
        return @{ status = 200; body = [string] $result.response; error = $null }
    }
    catch { return @{ status = 0; body = $null; error = "Ollama: " + $_.Exception.Message } }
}

function Send-Result($Id, $Result) {
    $json = @{ id = $Id; status = $Result.status; body = $Result.body; error = $Result.error } | ConvertTo-Json -Depth 4 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    Invoke-RestMethod -Uri "$Site/api/helper/result" -Method Post -Headers $Headers -Body $bytes -ContentType "application/json; charset=utf-8" -TimeoutSec 30 | Out-Null
}

# آزمایش اتصال و کلید.
try {
    Invoke-RestMethod -Uri "$Site/api/helper/ping" -Headers $Headers -TimeoutSec 20 | Out-Null
    Write-Log "به $Site وصل شد. دستیار آماده است (برای توقف Ctrl+C)." "Green"
    if ($OllamaModel) { Write-Log "بازنویسی متن با Ollama ($OllamaModel) روشن است." "Cyan" }
}
catch {
    Write-Log ("اتصال ناموفق: " + $_.Exception.Message + " – کلید و نشانی سایت را بررسی کنید.") "Red"
    exit 1
}

# حلقهٔ اصلی: انتظار طولانی ← اجرا ← ارسال نتیجه. خطای شبکه ← مکث فزاینده (۵ تا ۶۰ ثانیه)؛ ۴۰۱ ← توقف.
$backoff = 5
while ($true) {
    try {
        $response = Invoke-WebRequest -Uri "$Site/api/helper/next" -Headers $Headers -UseBasicParsing -TimeoutSec 40
        $backoff = 5
        if ([int] $response.StatusCode -eq 204 -or [string]::IsNullOrWhiteSpace($response.Content)) { continue }
        $job = $response.Content | ConvertFrom-Json
        $started = Get-Date
        switch ([string] $job.kind) {
            "fetch" { $result = Invoke-Fetch $job }
            "llm"   { $result = Invoke-Llm $job }
            default { $result = @{ status = 0; body = $null; error = "نوع کار ناشناخته." } }
        }
        Send-Result $job.id $result
        $seconds = [Math]::Round(((Get-Date) - $started).TotalSeconds, 1)
        $target = if ($job.url) { ([Uri] $job.url).Host } else { "متن" }
        Write-Log ("{0} {1} ← {2} ({3} ثانیه)" -f $job.kind, $target, $result.status, $seconds) $(if ($result.status -ge 200 -and $result.status -lt 300) { "Gray" } else { "Yellow" })
    }
    catch {
        $code = 0
        if ($_.Exception.Response -ne $null) { try { $code = [int] $_.Exception.Response.StatusCode } catch { } }
        if ($code -eq 401) { Write-Log "کلید باطل شده یا اشتباه است؛ از پنل کلید تازه بسازید." "Red"; exit 2 }
        Write-Log ("خطای ارتباط ({0}): {1} – {2} ثانیه بعد دوباره." -f $code, $_.Exception.Message, $backoff) "Yellow"
        Start-Sleep -Seconds $backoff
        $backoff = [Math]::Min(60, $backoff * 2)
    }
}
