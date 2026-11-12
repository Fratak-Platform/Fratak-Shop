using System.IO.Compression;
using System.Text;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۸ – «مغز» دستیار (جدول دانش): نرمال‌سازی، تطبیق واژهٔ کامل، تشخیص نوع پرسش، هم‌معنی‌ها، انتخاب جواب و بذر داخلی.
/// ترتیب: حالت‌های درست ← مرزی ← منفی/حمله‌ای (پیام خالی، خیلی بلند، پرسش خرید که نباید واژه‌نامه شود).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round28")]
public class AssistantBrainRound28Tests
{
    private static BrainFact Fact(AssistantFactKind kind, string term, string? aliases, string answer, double confidence = 1) =>
        new(Guid.NewGuid(), kind, term, AssistantBrain.Forms(term, aliases), answer, null, null, confidence);

    private static readonly IReadOnlyList<BrainFact> Facts =
    [
        Fact(AssistantFactKind.Glossary, "ANC", "نویز کنسلینگ|حذف نویز فعال", "حذف فعال نویز محیط."),
        Fact(AssistantFactKind.Glossary, "پاوربانک", null, "باتری همراه برای شارژ گوشی."),
        Fact(AssistantFactKind.Guide, "هدفون", "هندزفری|ایرباد", "نوع اتصال؛ عمر باتری؛ کیفیت میکروفون"),
        Fact(AssistantFactKind.SmallTalk, "خسته نباشی", "خدا قوت", "ممنونم، شما هم خسته نباشید!||لطف دارید 🌷"),
        Fact(AssistantFactKind.Synonym, "لپ تاپ", "لبتاپ|لپتاپ", "لپ تاپ"),
        Fact(AssistantFactKind.Learned, "ارسال به کیش", "کیش", "بله، به کیش هم ارسال داریم."),
    ];

    // ── نرمال‌سازی و واژهٔ کامل ─────────────────────────────────

    [Theory]
    [InlineData("ANC يعني چی؟", "anc یعنی چی")]
    [InlineData("  كيف   پول  ", "کیف پول")]
    [InlineData("۲۰۰۰۰ میلی‌آمپر", "20000 میلی امپر")]
    [InlineData("usb-c چیه", "usb-c چیه")]
    [InlineData("آموزش!!!", "اموزش")]
    public void Normalize_unifies_letters_digits_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, AssistantBrain.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("؟؟!!")]
    public void Normalize_of_empty_or_punctuation_is_empty(string? input) =>
        Assert.Equal("", AssistantBrain.Normalize(input));

    [Theory]
    [InlineData("anc یعنی چی", "anc", true)]
    [InlineData("حذف نویز فعال چیه", "حذف نویز فعال", true)]
    [InlineData("بالانس", "لانس", false)]
    [InlineData("pdf", "pd", false)]
    public void ContainsWord_matches_whole_words_only(string text, string form, bool expected) =>
        Assert.Equal(expected, AssistantBrain.ContainsWord(text, form));

    // ── تطبیق هر نوع ────────────────────────────────────────────

    [Fact]
    public void Guide_question_returns_the_buying_guide()
    {
        var hit = AssistantBrain.Match("برای خرید هندزفری به چی دقت کنم", Facts);
        Assert.NotNull(hit);
        Assert.Equal(AssistantFactKind.Guide, hit.Fact.Kind);
        Assert.Equal("هدفون", hit.Fact.Term);
    }

    [Theory]
    [InlineData("ANC یعنی چی")]
    [InlineData("منظور از نویز کنسلینگ چیه")]
    [InlineData("anc?")]
    public void Meaning_question_returns_the_glossary_entry(string text)
    {
        var hit = AssistantBrain.Match(text, Facts);
        Assert.NotNull(hit);
        Assert.Equal(AssistantFactKind.Glossary, hit.Fact.Kind);
        Assert.Equal("ANC", hit.Fact.Term);
    }

    [Theory]
    [InlineData("قیمت پاوربانک چیه")]
    [InlineData("پاوربانک ۲۰ هزار موجود دارید؟")]
    [InlineData("پاوربانک")]
    [InlineData("یه پاوربانک خوب میخوام برای سفر که سبک باشه و دو خروجی داشته باشه چیه نظرت")]
    public void Shopping_messages_are_not_hijacked_by_the_glossary(string text)
    {
        var hit = AssistantBrain.Match(text, Facts);
        Assert.True(hit is null || hit.Fact.Kind != AssistantFactKind.Glossary, hit?.Fact.Term);
    }

    [Theory]
    [InlineData("خسته نباشی")]
    [InlineData("خدا قوت رفیق")]
    public void Small_talk_is_recognised_in_short_messages(string text)
    {
        var hit = AssistantBrain.Match(text, Facts);
        Assert.NotNull(hit);
        Assert.Equal(AssistantFactKind.SmallTalk, hit.Fact.Kind);
    }

    [Fact]
    public void Small_talk_inside_a_long_message_is_ignored() =>
        Assert.Null(AssistantBrain.Match("خسته نباشی میخواستم بپرسم هدفون بلوتوثی با باتری قوی چی دارید", Facts) is { Fact.Kind: AssistantFactKind.SmallTalk } hit ? hit : null);

    [Fact]
    public void Learned_fact_matches_when_its_words_appear()
    {
        var hit = AssistantBrain.Match("سلام ارسال به کیش هم دارید", Facts);
        Assert.NotNull(hit);
        Assert.Equal(AssistantFactKind.Learned, hit.Fact.Kind);
    }

    [Fact]
    public void Low_confidence_facts_are_never_used()
    {
        var weak = new[] { Fact(AssistantFactKind.SmallTalk, "خسته نباشی", null, "x", confidence: 0.1) };
        Assert.Null(AssistantBrain.Match("خسته نباشی", weak));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    public void Empty_or_tiny_messages_match_nothing(string? text) => Assert.Null(AssistantBrain.Match(text, Facts));

    [Fact]
    public void Very_long_messages_match_nothing() =>
        Assert.Null(AssistantBrain.Match(string.Concat(Enumerable.Repeat("خسته نباشی ", 40)), Facts));

    [Fact]
    public void Longer_glossary_form_wins_over_shorter()
    {
        var facts = new[]
        {
            Fact(AssistantFactKind.Glossary, "هارد", null, "کوتاه"),
            Fact(AssistantFactKind.Glossary, "هارد اکسترنال", null, "بلند"),
        };
        Assert.Equal("هارد اکسترنال", AssistantBrain.Match("هارد اکسترنال یعنی چی", facts)?.Fact.Term);
    }

    // ── هم‌معنی، انتخاب جواب، نکته‌ها، اطمینان ──────────────────

    [Theory]
    [InlineData("لبتاپ ارزان", "لپ تاپ ارزان")]
    [InlineData("لپتاپ", "لپ تاپ")]
    public void Canonical_replaces_synonyms(string query, string expected) =>
        Assert.Equal(expected, AssistantBrain.Canonical(query, Facts));

    [Theory]
    [InlineData("لپ تاپ")]
    [InlineData("گوشی")]
    [InlineData("")]
    public void Canonical_returns_null_when_nothing_changes(string query) => Assert.Null(AssistantBrain.Canonical(query, Facts));

    [Fact]
    public void Pick_rotates_replies_and_is_stable_per_turn()
    {
        const string Answer = "الف||ب||ج";
        Assert.Equal("الف", AssistantBrain.Pick(Answer, 0));
        Assert.Equal("ب", AssistantBrain.Pick(Answer, 1));
        Assert.Equal("ج", AssistantBrain.Pick(Answer, 5));
        Assert.Equal("ب", AssistantBrain.Pick(Answer, -1));
        Assert.Equal("تنها", AssistantBrain.Pick("تنها", 7));
    }

    [Fact]
    public void Tips_split_on_persian_semicolon_and_drop_empties()
    {
        var tips = AssistantBrain.Tips("نکته اول؛ نکته دوم ؛؛ نکته سوم؛");
        Assert.Equal(3, tips.Count);
        Assert.Equal("نکته دوم", tips[1]);
    }

    [Fact]
    public void Confidence_grows_with_helpful_and_falls_with_unhelpful_votes()
    {
        var start = AssistantBrain.Confidence(1, 0, 0);
        Assert.Equal(1d, start, 3);
        Assert.True(AssistantBrain.Confidence(1, 0, 8) < AssistantBrain.MinConfidence);
        Assert.True(AssistantBrain.Confidence(1, 10, 2) > AssistantBrain.Confidence(1, 0, 2));
        Assert.Equal(0d, AssistantBrain.Confidence(-5, 0, 0), 3);
        Assert.True(AssistantBrain.Confidence(9, -3, -3) <= 1);
    }

    [Fact]
    public void ShouldRetire_needs_enough_votes()
    {
        Assert.True(AssistantFactService.ShouldRetire(1, 0, 8));
        Assert.False(AssistantFactService.ShouldRetire(1, 0, 3));
        Assert.False(AssistantFactService.ShouldRetire(1, 8, 4));
    }

    [Fact]
    public void Aliases_normaliser_splits_on_commas_and_pipes_and_dedupes() =>
        Assert.Equal("کیش|جزیره کیش", AssistantFactService.Aliases(" کیش، جزیره کیش | کیش ,, "));

    // ── بذر داخلی ───────────────────────────────────────────────

    [Fact]
    public void Embedded_seed_is_loaded_and_covers_every_kind()
    {
        var seed = AssistantBrain.Seed;
        Assert.True(seed.Count >= 150, seed.Count.ToString());
        foreach (var kind in new[] { AssistantFactKind.Glossary, AssistantFactKind.Guide, AssistantFactKind.SmallTalk, AssistantFactKind.Synonym })
            Assert.Contains(seed, r => r.Kind == kind);
        Assert.Equal(seed.Count, seed.Select(r => (r.Kind, AssistantBrain.Normalize(r.Term))).Distinct().Count());
        Assert.All(seed, r => Assert.InRange(r.Term.Length, 2, 120));
        Assert.All(seed, r => Assert.InRange(r.Answer.Length, 2, 1500));
        Assert.Equal(seed.Count, AssistantBrain.FromSeedOnly.Count);
    }

    [Fact]
    public void Embedded_seed_answers_real_questions()
    {
        var facts = AssistantBrain.FromSeedOnly;
        Assert.Equal(AssistantFactKind.Glossary, AssistantBrain.Match("anc یعنی چی", facts)?.Fact.Kind);
        Assert.Equal(AssistantFactKind.Guide, AssistantBrain.Match("برای خرید پاوربانک به چی دقت کنم", facts)?.Fact.Kind);
        Assert.Equal(AssistantFactKind.SmallTalk, AssistantBrain.Match("خسته نباشید", facts)?.Fact.Kind);
        Assert.Equal(AssistantFactKind.SmallTalk, AssistantBrain.Match("ربات هستی؟", facts)?.Fact.Kind);
        Assert.Null(AssistantBrain.Match("پاوربانک ۲۰ هزار", facts));
    }

    [Fact]
    public void ParseSeed_skips_comments_bad_kinds_and_short_rows()
    {
        const string Tsv = "# comment\nglossary\tANC\tنویز||\tحذف نویز\tصدا\t1\nunknown\tx\t\ty\t\t1\nguide\tکوتاه\n\nsmalltalk\tسلام\t\tسلام!||درود\t\t0.8\n";
        using var raw = new MemoryStream(Encoding.UTF8.GetBytes(Tsv));
        var rows = AssistantBrain.ParseSeed(raw, gzip: false).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(AssistantFactKind.Glossary, rows[0].Kind);
        Assert.Equal(0.8, rows[1].Weight, 3);

        // نسخهٔ gzip هم همان را می‌دهد.
        using var zipped = new MemoryStream();
        using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(Tsv));
        zipped.Position = 0;
        Assert.Equal(2, AssistantBrain.ParseSeed(zipped).Count());
    }

    [Theory]
    [InlineData("glossary", AssistantFactKind.Glossary)]
    [InlineData(" guide ", AssistantFactKind.Guide)]
    [InlineData("smalltalk", AssistantFactKind.SmallTalk)]
    [InlineData("synonym", AssistantFactKind.Synonym)]
    [InlineData("learned", AssistantFactKind.Learned)]
    public void KindOf_maps_seed_codes(string code, AssistantFactKind expected) => Assert.Equal(expected, AssistantBrain.KindOf(code));

    [Fact]
    public void KindOf_rejects_unknown_codes() => Assert.Null(AssistantBrain.KindOf("drop table"));
}
