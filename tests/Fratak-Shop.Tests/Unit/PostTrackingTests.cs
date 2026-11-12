using System.Globalization;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد رهگیری پست (مرحلهٔ هفتم – فاز ۶): تمیز کردن کد رهگیری، تشخیص نوع رویداد از متن فارسی،
/// وضعیت کلی مرسوله، تبدیل تاریخ شمسی و تجزیهٔ HTML سامانهٔ tracking.post.ir (هم HTML کامل و هم پاسخ Delta ای‌جکس).
/// </summary>
public class PostTrackingTests
{
    // ── کد رهگیری ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹۰۱۲۳۴", "123456789012345678901234")]
    [InlineData(" 1234-5678 9012 3456 7890 1234 ", "123456789012345678901234")]
    [InlineData("1234567890", "1234567890")]
    public void IranPost_code_is_normalized_to_ascii_digits(string raw, string expected)
    {
        Assert.True(ShippingCarriers.TryNormalizeCode(ShippingCarrier.IranPost, raw, out var code, out var error));
        Assert.Null(error);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC123")]
    [InlineData("123456789")]                          // کوتاه
    [InlineData("1234567890123456789012345678901")]    // ۳۱ رقم
    public void IranPost_code_rejects_invalid_values(string raw)
    {
        Assert.False(ShippingCarriers.TryNormalizeCode(ShippingCarrier.IranPost, raw, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Other_carriers_accept_free_text_codes()
    {
        Assert.True(ShippingCarriers.TryNormalizeCode(ShippingCarrier.Tipax, "  TPX  ۱۲۳۴ ", out var code, out _));
        Assert.Equal("TPX 1234", code);
        Assert.False(ShippingCarriers.TryNormalizeCode(ShippingCarrier.Courier, "ab", out _, out _));
    }

    [Fact]
    public void Only_post_has_auto_tracking_and_public_url()
    {
        Assert.True(ShippingCarriers.SupportsAutoTracking(ShippingCarrier.IranPost));
        Assert.All(ShippingCarriers.All.Where(c => c != ShippingCarrier.IranPost), c => Assert.False(ShippingCarriers.SupportsAutoTracking(c)));
        Assert.Equal("https://tracking.post.ir/search.aspx?id=123", ShippingCarriers.PublicTrackingUrl(ShippingCarrier.IranPost, "123"));
        Assert.Null(ShippingCarriers.PublicTrackingUrl(ShippingCarrier.Tipax, "123"));
        Assert.All(ShippingCarriers.All, c => Assert.False(string.IsNullOrWhiteSpace(ShippingCarriers.Title(c))));
    }

    // ── نوع رویداد ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("قبول مرسوله", PostEventKind.Accepted)]
    [InlineData("ورود به مرکز مبادلات تهران", PostEventKind.InTransit)]
    [InlineData("خروج از مرکز مبادلات", PostEventKind.InTransit)]
    [InlineData("تحویل به نامه‌رسان جهت توزیع", PostEventKind.OutForDelivery)]
    [InlineData("تحويل مرسوله به گيرنده", PostEventKind.Delivered)]        // ی/ک عربی
    [InlineData("تحویل  مرسوله\u200Cبه گیرنده", PostEventKind.Delivered)]
    [InlineData("عدم حضور گیرنده", PostEventKind.DeliveryFailed)]
    [InlineData("عدم تحویل به گیرنده - نقص آدرس", PostEventKind.DeliveryFailed)]
    [InlineData("برگشت به فرستنده", PostEventKind.Returned)]
    [InlineData("تحویل مرسوله برگشتی به فرستنده", PostEventKind.Returned)]
    [InlineData("", PostEventKind.Unknown)]
    public void Classifier_understands_post_statuses(string status, PostEventKind expected) =>
        Assert.Equal(expected, PostStatusClassifier.Classify(status));

    [Fact]
    public void Delivery_to_postman_or_center_is_not_delivery()
    {
        Assert.NotEqual(PostEventKind.Delivered, PostStatusClassifier.Classify("تحویل به مرکز مبادلات"));
        Assert.NotEqual(PostEventKind.Delivered, PostStatusClassifier.Classify("تحویل به موزع"));
    }

    [Fact]
    public void Overall_state_prefers_returned_then_delivered_then_last_event()
    {
        Assert.Equal(ShipmentTrackingState.NotFound, PostStatusClassifier.Overall([]));
        Assert.Equal(ShipmentTrackingState.InTransit, PostStatusClassifier.Overall([PostEventKind.Accepted, PostEventKind.InTransit]));
        Assert.Equal(ShipmentTrackingState.OutForDelivery, PostStatusClassifier.Overall([PostEventKind.Accepted, PostEventKind.OutForDelivery]));
        Assert.Equal(ShipmentTrackingState.InTransit, PostStatusClassifier.Overall([PostEventKind.OutForDelivery, PostEventKind.DeliveryFailed]));
        Assert.Equal(ShipmentTrackingState.Delivered, PostStatusClassifier.Overall([PostEventKind.Accepted, PostEventKind.Delivered]));
        // «تحویل به گیرنده» بعد از برگشت یعنی تحویل به خود فرستنده.
        Assert.Equal(ShipmentTrackingState.Returned, PostStatusClassifier.Overall([PostEventKind.Returned, PostEventKind.Delivered]));
    }

    // ── تاریخ ───────────────────────────────────────────────────────────

    [Fact]
    public void Jalali_date_and_time_are_converted_in_iran_time()
    {
        var moment = PostTrackingParser.ParseMoment("سه شنبه ۱۴۰۵/۰۸/۰۴", "۰۹:۰۵");
        Assert.NotNull(moment);
        var local = IranClock.ToLocal(moment!.Value);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 5, 0), local.DateTime);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("بدون تاریخ", "10:00")]
    [InlineData("1405/13/40", "10:00")]
    public void Invalid_dates_return_null(string? date, string? time) =>
        Assert.Null(PostTrackingParser.ParseMoment(date, time));

    [Fact]
    public void Missing_time_means_start_of_day()
    {
        var moment = PostTrackingParser.ParseMoment("1405/01/01", "ساعت نامعلوم");
        Assert.NotNull(moment);
        Assert.Equal(new DateTime(2026, 3, 21, 0, 0, 0), IranClock.ToLocal(moment!.Value).DateTime);
    }

    // ── HTML سامانهٔ پست ────────────────────────────────────────────────

    private const string SearchPage = """
        <html><body><form id="form1">
          <input type="hidden" name="__VIEWSTATE" id="__VIEWSTATE" value="VS123" />
          <input type="hidden" name="__VIEWSTATEGENERATOR" id="__VIEWSTATEGENERATOR" value="BBBC20B8" />
          <input type="hidden" name="__EVENTVALIDATION" id="__EVENTVALIDATION" value="EV456" />
        </form></body></html>
        """;

    private const string ResultHtml = """
        <div id="pnlMain">
          <div id="pParcelInfo">
            <div class="newrowdatacol"><div class="newcolheader">مبدأ</div><div class="newcoldata">شیراز</div></div>
            <div class="newrowdatacol"><div class="newcolheader">مقصد</div><div class="newcoldata">  تهران  </div></div>
          </div>
          <div id="pnlResult">
            <div class="row"><div class="newtdheader">سه شنبه ۱۴۰۵/۰۸/۰۴</div></div>
            <div class="row">
              <div class="newtddata">3</div><div class="newtddata">تحویل مرسوله به گیرنده</div>
              <div class="newtddata">تهران</div><div class="newtddata">14:20</div>
            </div>
            <div class="row"><div class="newtdheader">دوشنبه ۱۴۰۵/۰۸/۰۳</div></div>
            <div class="row">
              <div class="newtddata">2</div><div class="newtddata">ورود به مرکز مبادلات</div>
              <div class="newtddata">مرکز مبادلات تهران</div><div class="newtddata">۲۲:۴۰</div>
            </div>
            <div class="row">
              <div class="newtddata">1</div><div class="newtddata">قبول مرسوله <a href="/vote">نظرسنجی</a></div>
              <div class="newtddata">باجه معالی آباد شیراز</div><div class="newtddata">09:05</div>
            </div>
          </div>
        </div>
        """;

    [Fact]
    public void Form_state_is_read_from_search_page()
    {
        var form = PostTrackingParser.ReadFormState(SearchPage);
        Assert.NotNull(form);
        Assert.Equal("VS123", form!.Value.ViewState);
        Assert.Equal("EV456", form.Value.EventValidation);
        Assert.Equal("BBBC20B8", form.Value.Generator);
        Assert.Null(PostTrackingParser.ReadFormState("<html><body>خطا</body></html>"));
        Assert.Null(PostTrackingParser.ReadFormState(null));
    }

    [Fact]
    public void Result_is_parsed_oldest_first_with_dates_links_removed_and_info()
    {
        var result = PostTrackingParser.ParseResult(ResultHtml);
        Assert.Equal(PostTrackingOutcome.Found, result.Outcome);
        Assert.Equal(3, result.Events.Count);

        var first = result.Events[0];
        Assert.Equal(0, first.Sequence);
        Assert.Equal("قبول مرسوله", first.Status);
        Assert.Equal("باجه معالی آباد شیراز", first.Location);
        Assert.Equal(PostEventKind.Accepted, first.Kind);
        Assert.Equal(new DateTime(2026, 10, 25, 9, 5, 0), IranClock.ToLocal(first.OccurredAt!.Value).DateTime);

        Assert.Equal(new DateTime(2026, 10, 25, 22, 40, 0), IranClock.ToLocal(result.Events[1].OccurredAt!.Value).DateTime);

        var last = result.Events[2];
        Assert.Equal(PostEventKind.Delivered, last.Kind);
        Assert.Equal(new DateTime(2026, 10, 26, 14, 20, 0), IranClock.ToLocal(last.OccurredAt!.Value).DateTime);
        Assert.Contains("۱۴۰۵/۰۸/۰۴", last.RawTime);

        Assert.Equal(2, result.Info.Count);
        Assert.Equal("مقصد", result.Info[1].Key);
        Assert.Equal("تهران", result.Info[1].Value);
        Assert.Equal(ShipmentTrackingState.Delivered, PostStatusClassifier.Overall(result.Events.Select(e => e.Kind).ToList()));
    }

    [Fact]
    public void Ajax_delta_response_is_parsed_too()
    {
        var delta = "1|#||4|" + ResultHtml.Length.ToString(CultureInfo.InvariantCulture) + "|updatePanel|pnlMain|" + ResultHtml
                    + "|0|hiddenField|__EVENTTARGET||0|hiddenField|__VIEWSTATE|abc|";
        var result = PostTrackingParser.ParseResult(delta);
        Assert.Equal(PostTrackingOutcome.Found, result.Outcome);
        Assert.Equal(3, result.Events.Count);
    }

    [Fact]
    public void Missing_result_panel_means_not_found_and_empty_means_failure()
    {
        Assert.Equal(PostTrackingOutcome.NotFound, PostTrackingParser.ParseResult("<div id='pnlMain'>مرسوله‌ای یافت نشد</div>").Outcome);
        Assert.Equal(PostTrackingOutcome.Failed, PostTrackingParser.ParseResult("").Outcome);
        var empty = PostTrackingParser.ParseResult("<div id='pnlResult'></div>");
        Assert.Equal(PostTrackingOutcome.Found, empty.Outcome);
        Assert.Empty(empty.Events);
    }

    [Fact]
    public void Fingerprint_ignores_sequence_and_spacing_but_not_content()
    {
        var a = new PostTrackingEvent(0, "قبول مرسوله", "شیراز", "1405/08/04 09:05", null);
        var b = new PostTrackingEvent(5, "قبول  مرسوله", "شیراز", "1405/08/04  09:05", DateTimeOffset.UtcNow);
        var c = new PostTrackingEvent(0, "قبول مرسوله", "تهران", "1405/08/04 09:05", null);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.Fingerprint, c.Fingerprint);
        Assert.Equal(32, a.Fingerprint.Length);
    }

    [Fact]
    public void State_titles_exist_and_active_states_are_correct()
    {
        foreach (var state in Enum.GetValues<ShipmentTrackingState>())
            Assert.False(string.IsNullOrWhiteSpace(ShippingCarriers.StateTitle(state)));
        Assert.True(ShippingCarriers.IsActive(ShipmentTrackingState.Pending));
        Assert.True(ShippingCarriers.IsActive(ShipmentTrackingState.NotFound));
        Assert.False(ShippingCarriers.IsActive(ShipmentTrackingState.Delivered));
        Assert.False(ShippingCarriers.IsActive(ShipmentTrackingState.Returned));
        Assert.False(ShippingCarriers.IsActive(ShipmentTrackingState.Stopped));
    }
}
