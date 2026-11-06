using System.Net;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure.Shipping;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های رهگیری پست نسخهٔ ۲ (با کد امنیتی):
/// رمزگشایی CTC، دسته‌بندی پاسخ سایت (کپچای رد شده / بی‌اطلاعات / نتیجه)، اجرای واقعی مدل ONNX روی تصویرهای نمونه
/// (Fixtures/Captcha/{جواب}.png) و کل جریان PostTrackingClient با یک سایت پست جعلی (بدون اینترنت).
/// </summary>
public class PostCaptchaTests
{
    // ── CTC ─────────────────────────────────────────────────────────────

    /// <summary>ساخت امتیازهای ساختگی: در هر ستون کلاس داده‌شده بیشترین امتیاز را دارد.</summary>
    private static float[] Logits(params int[] best)
    {
        var data = new float[best.Length * CtcDigitDecoder.Classes];
        for (var t = 0; t < best.Length; t++)
        {
            for (var c = 0; c < CtcDigitDecoder.Classes; c++)
                data[t * CtcDigitDecoder.Classes + c] = -1f - c * 0.01f;
            data[t * CtcDigitDecoder.Classes + best[t]] = 5f;
        }
        return data;
    }

    [Fact]
    public void Ctc_collapses_repeats_and_drops_blanks()
    {
        // کلاس i = رقم i-1 → 5=«4»، 1=«0»، 4=«3»، 9=«8»
        Assert.Equal("4038", CtcDigitDecoder.Decode(Logits(0, 5, 5, 0, 1, 1, 0, 4, 0, 9, 9), 11));
    }

    [Fact]
    public void Ctc_keeps_double_digit_when_separated_by_blank()
    {
        Assert.Equal("0032", CtcDigitDecoder.Decode(Logits(1, 0, 1, 4, 4, 3, 0), 7));
        Assert.Equal("1", CtcDigitDecoder.Decode(Logits(2, 2, 2), 3)); // بدون blank تکرار یکی می‌شود
    }

    [Fact]
    public void Ctc_rejects_invalid_shapes()
    {
        Assert.Equal("", CtcDigitDecoder.Decode(new float[5], 1));
        Assert.Equal("", CtcDigitDecoder.Decode(Logits(1, 2), 0));
        Assert.Equal("", CtcDigitDecoder.Decode(new float[30], 3, 10));
    }

    [Theory]
    [InlineData("4058", true)]
    [InlineData("123456", true)]
    [InlineData("405", false)]
    [InlineData("1234567", false)]
    [InlineData("40a8", false)]
    [InlineData("۴۰۵۸", false)]
    [InlineData(null, false)]
    public void Answer_validation(string? code, bool expected) => Assert.Equal(expected, CtcDigitDecoder.LooksValid(code));

    // ── دسته‌بندی پاسخ سایت ─────────────────────────────────────────────

    private const string ResultPage = """
        <form><input name="txtCaptcha" id="txtCaptcha"><img src="search.aspx?captcha=1&t=1"></form>
        <div id="pnlResult">
          <div class="alert alert-warning">رهگیری مرسوله به شماره 123456789012345678901234</div>
          <div class="newtdheader col-lg-6">سه شنبه ۱۴۰۵/۰۸/۰۴</div>
          <div class="row newrowdata"><div class="newtddata">2</div><div class="newtddata">تحویل مرسوله به گیرنده</div><div class="newtddata">تهران</div><div class="newtddata">14:20</div></div>
          <div class="row newrowdata"><div class="newtddata">1</div><div class="newtddata">قبول مرسوله</div><div class="newtddata">شیراز</div><div class="newtddata">09:05</div></div>
          <div id="pnlVote"><div class="row"><div class="newtddata">x</div><div class="newtddata">نظرسنجی</div></div></div>
        </div>
        """;

    [Fact]
    public void Classify_result_page_with_v2_layout()
    {
        var (verdict, response) = PostTrackingParser.Classify(ResultPage);
        Assert.Equal(PostCaptchaVerdict.Result, verdict);
        Assert.Equal(PostTrackingOutcome.Found, response.Outcome);
        Assert.Equal(2, response.Events.Count);                 // ردیف نظرسنجی رویداد حساب نمی‌شود
        Assert.Equal("قبول مرسوله", response.Events[0].Status);  // قدیمی ← جدید
        Assert.NotNull(response.Events[1].OccurredAt);          // تاریخ از سرتیتر مستقیم newtdheader خوانده شد
    }

    [Fact]
    public void Classify_wrong_captcha_message_is_retry()
    {
        const string html = """<form><input id="txtCaptcha"></form><div class="alert alert-danger">کد امنیتی را صحیح وارد نمایید</div>""";
        var (verdict, response) = PostTrackingParser.Classify(html);
        Assert.Equal(PostCaptchaVerdict.CaptchaRejected, verdict);
        Assert.Equal(PostTrackingOutcome.Failed, response.Outcome);
    }

    [Fact]
    public void Classify_form_again_without_result_is_captcha_rejected()
    {
        var (verdict, _) = PostTrackingParser.Classify("""<form><input id="txtCaptcha"><img src="search.aspx?captcha=1&t=5"></form>""");
        Assert.Equal(PostCaptchaVerdict.CaptchaRejected, verdict);
    }

    [Fact]
    public void Classify_explicit_not_found_message()
    {
        const string html = """<form><input id="txtCaptcha"></form><div class='alert alert-danger'>مرسوله‌ای با این شماره یافت نشد</div>""";
        var (verdict, response) = PostTrackingParser.Classify(html);
        Assert.Equal(PostCaptchaVerdict.NoData, verdict);
        Assert.Equal(PostTrackingOutcome.NotFound, response.Outcome);
        Assert.Contains("یافت نشد", response.Error);
    }

    [Fact]
    public void Classify_empty_result_panel_is_ambiguous()
    {
        var (verdict, response) = PostTrackingParser.Classify("""<div id="pnlResult"></div>""");
        Assert.Equal(PostCaptchaVerdict.Ambiguous, verdict);
        Assert.Equal(PostTrackingOutcome.Failed, response.Outcome);
        Assert.False(PostTrackingParser.HasCaptcha("<div id='pnlResult'></div>"));
        Assert.True(PostTrackingParser.HasCaptcha("<input name='txtCaptcha'>"));
    }

    // ── مدل واقعی روی تصویرهای نمونه ────────────────────────────────────

    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Captcha");

    [Fact]
    public void Model_file_is_deployed_next_to_the_app()
    {
        using var solver = new OnnxCaptchaSolver(NullLogger<OnnxCaptchaSolver>.Instance);
        Assert.NotNull(solver.ModelPath);
        Assert.True(solver.IsAvailable);
    }

    [Fact]
    public void Model_reads_sample_captchas()
    {
        using var solver = new OnnxCaptchaSolver(NullLogger<OnnxCaptchaSolver>.Instance);
        var files = Directory.GetFiles(FixtureDir, "*.png");
        Assert.True(files.Length >= 4, "تصویرهای نمونهٔ کپچا کپی نشده‌اند.");
        foreach (var file in files)
        {
            var expected = Path.GetFileNameWithoutExtension(file);
            Assert.Equal(expected, solver.Solve(File.ReadAllBytes(file)));
        }
    }

    [Fact]
    public void Model_returns_null_for_garbage()
    {
        using var solver = new OnnxCaptchaSolver(NullLogger<OnnxCaptchaSolver>.Instance);
        Assert.Null(solver.Solve([1, 2, 3, 4, 5]));
        Assert.Null(solver.Solve([]));
        Assert.Null(OnnxCaptchaSolver.Prepare([0x89, 0x50, 0x4E, 0x47]));
    }

    [Fact]
    public void Prepare_scales_to_height_64_and_unit_range()
    {
        var bytes = File.ReadAllBytes(Directory.GetFiles(FixtureDir, "*.png")[0]);
        var prepared = OnnxCaptchaSolver.Prepare(bytes);
        Assert.NotNull(prepared);
        Assert.Equal(192, prepared!.Value.Width);              // 120×40 → 192×64
        Assert.Equal(192 * 64, prepared.Value.Pixels.Length);
        Assert.All(prepared.Value.Pixels, p => Assert.InRange(p, 0f, 1f));
    }

    // ── کل جریان کلاینت با سایت جعلی ────────────────────────────────────

    /// <summary>سایت پست جعلی: صفحهٔ فرم، تصویر کپچا و پاسخ POST؛ اولین جواب کپچا را رد می‌کند.</summary>
    private sealed class FakePostSite : HttpMessageHandler
    {
        public List<string> Log { get; } = [];
        public List<string> PostedCaptchas { get; } = [];
        public string? LastCookie;
        public string Expected { get; init; } = "2152";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Log.Add(request.Method + " " + url);
            LastCookie = request.Headers.TryGetValues("Cookie", out var c) ? string.Join(";", c) : null;
            if (request.Method == HttpMethod.Get && url.Contains("captcha=1"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[400]) };
            if (request.Method == HttpMethod.Get)
            {
                var page = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <form><input type="hidden" id="__VIEWSTATE" value="VS"><input type="hidden" id="__EVENTVALIDATION" value="EV">
                        <input type="hidden" id="__VIEWSTATEGENERATOR" value="GEN"><input id="txtCaptcha" name="txtCaptcha"></form>
                        """),
                };
                page.Headers.Add("Set-Cookie", "ASP.NET_SessionId=abc; path=/; HttpOnly");
                return page;
            }

            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var answer = form.Split('&').Select(WebUtility.UrlDecode).First(p => p!.StartsWith("txtCaptcha="))!["txtCaptcha=".Length..];
            PostedCaptchas.Add(answer);
            var body = answer == Expected
                ? ResultPage
                : """<form><input id="txtCaptcha"></form><div class="alert alert-danger">کد امنیتی اشتباه است</div>""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>حل‌کنندهٔ جعلی: جواب‌ها را به ترتیب برمی‌گرداند.</summary>
    private sealed class QueueSolver(params string?[] answers) : IPostCaptchaSolver
    {
        private int _next;
        public bool IsAvailable { get; init; } = true;
        public string? Solve(byte[] image) => answers[Math.Min(_next++, answers.Length - 1)];
    }

    [Fact]
    public async Task Client_retries_with_fresh_captcha_until_accepted()
    {
        var site = new FakePostSite();
        var client = new PostTrackingClient(new Factory(site), new QueueSolver("9999", "2152"), NullLogger<PostTrackingClient>.Instance);
        var response = await client.TrackAsync("123456789012345678901234", CancellationToken.None);

        Assert.Equal(PostTrackingOutcome.Found, response.Outcome);
        Assert.Equal(2, response.Events.Count);
        Assert.Equal(new[] { "9999", "2152" }, site.PostedCaptchas.ToArray());
        Assert.Equal(6, site.Log.Count);                                   // دو تلاش × (صفحه + تصویر + ارسال)
        Assert.Contains("ASP.NET_SessionId=abc", site.LastCookie);         // کوکی جلسه با فرم فرستاده شد
    }

    [Fact]
    public async Task Client_gives_failed_not_notfound_when_captcha_never_accepted()
    {
        var site = new FakePostSite();
        var client = new PostTrackingClient(new Factory(site), new QueueSolver("1111"), NullLogger<PostTrackingClient>.Instance);
        var response = await client.TrackAsync("123456789012345678901234", CancellationToken.None);

        Assert.Equal(PostTrackingOutcome.Failed, response.Outcome);
        Assert.Equal(PostTrackingClient.MaxAttempts, site.PostedCaptchas.Count);
        Assert.Contains("کد امنیتی", response.Error);
    }

    [Fact]
    public async Task Client_skips_post_when_answer_is_unreadable()
    {
        var site = new FakePostSite();
        var client = new PostTrackingClient(new Factory(site), new QueueSolver(null, "12", "2152"), NullLogger<PostTrackingClient>.Instance);
        var response = await client.TrackAsync("123456789012345678901234", CancellationToken.None);

        Assert.Equal(PostTrackingOutcome.Found, response.Outcome);
        Assert.Equal(new[] { "2152" }, site.PostedCaptchas.ToArray());              // جواب‌های مشکوک فرستاده نشدند
    }

    [Fact]
    public async Task Client_reports_missing_model_clearly()
    {
        var site = new FakePostSite();
        var client = new PostTrackingClient(new Factory(site), new QueueSolver("2152") { IsAvailable = false }, NullLogger<PostTrackingClient>.Instance);
        var response = await client.TrackAsync("123456789012345678901234", CancellationToken.None);
        Assert.Equal(PostTrackingOutcome.Failed, response.Outcome);
        Assert.Contains("digits.onnx", response.Error);
        Assert.Empty(site.PostedCaptchas);
    }
}
