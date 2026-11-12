using Fratak_Shop.Core.Services.Assistant;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۸ – فهم پرسش و فرمان کارکنان در «دستیار مدیریت». فرمان‌های تغییردهنده باید درست تشخیص داده شوند
/// (و «غیرفعال کن» هرگز «فعال کن» فهمیده نشود)؛ ورودی خالی/خیلی بلند ← Unknown.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round28")]
public class StaffCommandsRound28Tests
{
    [Theory]
    [InlineData("کارهای امروز", StaffCommandKind.Today)]
    [InlineData("امروز چیکار کنم؟", StaffCommandKind.Today)]
    [InlineData("راهنما", StaffCommandKind.Help)]
    [InlineData("سفارش‌های منتظر", StaffCommandKind.PendingOrders)]
    [InlineData("سفارشای جدید", StaffCommandKind.PendingOrders)]
    [InlineData("کالاهای ناموجود", StaffCommandKind.OutOfStock)]
    [InlineData("چی تموم شده", StaffCommandKind.OutOfStock)]
    [InlineData("موجودی کم", StaffCommandKind.LowStock)]
    [InlineData("آماده انتشار", StaffCommandKind.ReadyList)]
    [InlineData("کالاهای آماده‌ی انتشار", StaffCommandKind.ReadyList)]
    [InlineData("تغییرات تامین کننده", StaffCommandKind.SupplierPending)]
    public void Recognises_report_questions(string text, StaffCommandKind expected) =>
        Assert.Equal(expected, StaffCommands.Parse(text).Kind);

    [Theory]
    [InlineData("فروش امروز", StaffPeriod.Today)]
    [InlineData("فروش دیروز چقدر بود", StaffPeriod.Yesterday)]
    [InlineData("فروش این هفته", StaffPeriod.Week)]
    [InlineData("درآمد این ماه", StaffPeriod.Month)]
    public void Sales_questions_get_the_right_period(string text, StaffPeriod period)
    {
        var command = StaffCommands.Parse(text);
        Assert.Equal(StaffCommandKind.Sales, command.Kind);
        Assert.Equal(period, command.Period);
        Assert.False(command.Changes);
    }

    [Theory]
    [InlineData("سفارش 1405-0012 کجاست", "1405-0012")]
    [InlineData("وضعیت سفارش ۱۴۰۵۱۲۳", "1405123")]
    [InlineData("فاکتور #FS-20931", "FS-20931")]
    public void Order_numbers_are_extracted(string text, string number)
    {
        var command = StaffCommands.Parse(text);
        Assert.Equal(StaffCommandKind.OrderLookup, command.Kind);
        Assert.Equal(number, command.OrderNumber);
    }

    [Theory]
    [InlineData("منتشر کن پاوربانک انکر", StaffCommandKind.Publish, "پاوربانک انکر")]
    [InlineData("هدفون بی سیم رو فعال کن", StaffCommandKind.Publish, "هدفون بی سیم")]
    [InlineData("غیرفعال کن کابل شارژ", StaffCommandKind.Unpublish, "کابل شارژ")]
    [InlineData("کابل شارژ رو غیر فعال کن لطفا", StaffCommandKind.Unpublish, "کابل شارژ")]
    [InlineData("اسپیکر جی بی ال رو مخفی کن", StaffCommandKind.Unpublish, "اسپیکر جی بی ال")]
    public void Changing_commands_keep_the_product_term_and_need_confirmation(string text, StaffCommandKind kind, string term)
    {
        var command = StaffCommands.Parse(text);
        Assert.Equal(kind, command.Kind);
        Assert.Equal(term, command.Term);
        Assert.True(command.Changes);
    }

    [Theory]
    [InlineData("منتشر کن")]
    [InlineData("غیرفعال کن")]
    public void Changing_command_without_a_product_falls_back_to_help(string text) =>
        Assert.Equal(StaffCommandKind.Help, StaffCommands.Parse(text).Kind);

    [Theory]
    [InlineData("موجودی هدفون بی سیم", "هدفون بی سیم")]
    [InlineData("قیمت پاوربانک چنده", "پاوربانک")]
    [InlineData("چند تا ماوس داریم", "ماوس")]
    public void Product_lookups_extract_the_term(string text, string term)
    {
        var command = StaffCommands.Parse(text);
        Assert.Equal(StaffCommandKind.ProductLookup, command.Kind);
        Assert.Equal(term, command.Term);
        Assert.False(command.Changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Empty_input_is_unknown(string? text) => Assert.Equal(StaffCommandKind.Unknown, StaffCommands.Parse(text).Kind);

    [Fact]
    public void Very_long_input_is_unknown_and_never_a_change()
    {
        var command = StaffCommands.Parse("منتشر کن " + new string('ب', 300));
        Assert.Equal(StaffCommandKind.Unknown, command.Kind);
        Assert.False(command.Changes);
    }

    [Theory]
    [InlineData("سفارش ۱۲")]
    [InlineData("1405-0012")]
    public void Order_lookup_needs_a_real_number_and_the_word_order(string text) =>
        Assert.NotEqual(StaffCommandKind.OrderLookup, StaffCommands.Parse(text).Kind);

    [Fact]
    public void Injection_like_text_is_just_a_search_term()
    {
        var command = StaffCommands.Parse("موجودی '; drop table products --");
        Assert.Equal(StaffCommandKind.ProductLookup, command.Kind);
        Assert.DoesNotContain(";", command.Term);
        Assert.DoesNotContain("'", command.Term);
    }

    [Theory]
    [InlineData(120, 100, "۲۰")]
    [InlineData(80, 100, "۲۰")]
    public void Trend_reports_percentage_change(int current, int previous, string percent) =>
        Assert.Contains(percent, Fratak_Shop.Web.Areas.Admin.Pages.SmartContent.OpsModel.Trend(current, previous), StringComparison.Ordinal);

    [Fact]
    public void Trend_without_history_is_silent() =>
        Assert.Equal(".", Fratak_Shop.Web.Areas.Admin.Pages.SmartContent.OpsModel.Trend(100, 0));
}
