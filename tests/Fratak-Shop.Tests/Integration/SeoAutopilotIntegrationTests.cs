using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// سئوی خودکار روی SQL Server واقعی:
/// • کارهای سئو: فیلتر وضعیت/شدت/جست‌وجو، تغییر وضعیت (انجام‌شده/نادیده/بازگشایی)، وضعیت نامعتبر و شناسهٔ ناموجود.
/// • پردازش یک محصول: هرگز خطا نمی‌دهد، نامک انگلیسی می‌ماند و هر تغییر در تاریخچه ثبت و قابل بازگشت است.
/// • گزارش ربات‌ها/سرعت با پایگاه خالی هم ساخته می‌شود.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "SeoAutopilot")]
public class SeoAutopilotIntegrationTests(SqlServerFixture db)
{
    private async Task<Guid> AddTaskAsync(string code, SeoTaskSeverity severity, SeoTaskStatus status)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var task = new SeoTask
        {
            Code = code,
            Severity = severity,
            Status = status,
            Title = "عنوان " + code,
            Detail = "توضیح",
            Path = "/product/" + code,
            Kind = SeoEntityKind.Product,
            Impact = 50,
            Fingerprint = Guid.NewGuid().ToString("N"),
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        context.SeoTasks.Add(task);
        await context.SaveChangesAsync();
        return task.Id;
    }

    [SqlFact]
    public async Task Tasks_are_filtered_and_their_status_changes_are_recorded()
    {
        var key = "t" + Guid.NewGuid().ToString("N")[..10];
        var open = await AddTaskAsync(key + "-a", SeoTaskSeverity.Critical, SeoTaskStatus.Open);
        await AddTaskAsync(key + "-b", SeoTaskSeverity.Info, SeoTaskStatus.Open);
        await AddTaskAsync(key + "-c", SeoTaskSeverity.Warning, SeoTaskStatus.AutoFixed);

        await using var scope = db.Scope();
        var reports = scope.ServiceProvider.GetRequiredService<ISeoReportService>();

        var all = await reports.TasksAsync(new SeoTaskQuery(null, null, key, 1));
        Assert.Equal(3, all.Total);
        Assert.Equal(SeoTaskSeverity.Critical, all.Items[0].Severity);

        var openOnly = await reports.TasksAsync(new SeoTaskQuery([SeoTaskStatus.Open], null, key, 1));
        Assert.Equal(2, openOnly.Total);
        var critical = await reports.TasksAsync(new SeoTaskQuery([SeoTaskStatus.Open], SeoTaskSeverity.Critical, key, 1));
        Assert.Equal(open, Assert.Single(critical.Items).Id);

        var done = await reports.SetTaskStatusAsync(open, SeoTaskStatus.Fixed, "مدیر آزمایشی");
        Assert.Equal(EnOperationResultStatus.Success, done.Status);
        var resolved = await reports.TasksAsync(new SeoTaskQuery([SeoTaskStatus.Fixed, SeoTaskStatus.AutoFixed], null, key, 1));
        Assert.Equal(2, resolved.Total);
        var fixedTask = Assert.Single(resolved.Items, t => t.Id == open);
        Assert.Equal("مدیر آزمایشی", fixedTask.ResolvedBy);
        Assert.NotNull(fixedTask.ResolvedAt);

        var reopened = await reports.SetTaskStatusAsync(open, SeoTaskStatus.Open, "مدیر آزمایشی");
        Assert.Equal(EnOperationResultStatus.Success, reopened.Status);
        var again = await reports.TasksAsync(new SeoTaskQuery([SeoTaskStatus.Open], null, key + "-a", 1));
        Assert.Null(Assert.Single(again.Items).ResolvedBy);
    }

    [SqlFact]
    public async Task Task_status_rejects_system_only_values_and_unknown_ids()
    {
        var id = await AddTaskAsync("t" + Guid.NewGuid().ToString("N")[..10], SeoTaskSeverity.Warning, SeoTaskStatus.Open);
        await using var scope = db.Scope();
        var reports = scope.ServiceProvider.GetRequiredService<ISeoReportService>();

        Assert.Equal(EnOperationResultStatus.Error, (await reports.SetTaskStatusAsync(id, SeoTaskStatus.AutoFixed, "x")).Status);
        Assert.Equal(EnOperationResultStatus.Error, (await reports.SetTaskStatusAsync(id, (SeoTaskStatus)99, "x")).Status);
        Assert.Equal(EnOperationResultStatus.NotFound, (await reports.SetTaskStatusAsync(Guid.NewGuid(), SeoTaskStatus.Fixed, "x")).Status);
    }

    [SqlFact]
    public async Task Task_search_is_safe_against_sql_wildcards_and_injection()
    {
        await using var scope = db.Scope();
        var reports = scope.ServiceProvider.GetRequiredService<ISeoReportService>();

        var injection = await reports.TasksAsync(new SeoTaskQuery(null, null, "'; DELETE FROM SeoTasks; --", 1));
        Assert.Equal(0, injection.Total);
        var huge = await reports.TasksAsync(new SeoTaskQuery(null, null, null, int.MaxValue, Take: int.MaxValue));
        Assert.True(huge.Items.Count <= 100);
    }

    [SqlFact]
    public async Task Processing_a_product_keeps_an_english_slug_and_every_change_can_be_rolled_back()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var autopilot = scope.ServiceProvider.GetRequiredService<ISeoAutopilotService>();
        var reports = scope.ServiceProvider.GetRequiredService<ISeoReportService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var result = await autopilot.ProcessAsync(SeoEntityKind.Product, productId);
        var again = await autopilot.ProcessAsync(SeoEntityKind.Product, productId);

        var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(product.Slug), product.Slug);
        Assert.False(again.Changed && again.Summary.SequenceEqual(result.Summary) && result.Changed, "Second pass repeated the same change.");

        var revision = await context.SeoRevisions.AsNoTracking().Where(r => r.EntityId == productId && r.RevertedAt == null)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        if (result.Changed)
        {
            Assert.NotNull(revision);
            var history = await reports.RevisionsAsync(revision!.Path, 1);
            Assert.Contains(history.Items, r => r.Id == revision.Id);

            var rollback = await autopilot.RollbackAsync(revision.Id, "مدیر آزمایشی");
            Assert.Equal(EnOperationResultStatus.Success, rollback.Status);
            var reverted = await context.SeoRevisions.AsNoTracking().SingleAsync(r => r.Id == revision.Id);
            Assert.NotNull(reverted.RevertedAt);
            Assert.Equal(EnOperationResultStatus.Error, (await autopilot.RollbackAsync(revision.Id, "مدیر آزمایشی")).Status); // دو بار برنمی‌گردد
        }

        Assert.NotEqual(EnOperationResultStatus.Success, (await autopilot.RollbackAsync(Guid.NewGuid(), "x")).Status);
    }

    [SqlFact]
    public async Task Signals_report_builds_on_an_empty_window()
    {
        await using var scope = db.Scope();
        var reports = scope.ServiceProvider.GetRequiredService<ISeoReportService>();

        var view = await reports.SignalsAsync(-5);

        Assert.Equal(1, view.Days);
        Assert.NotNull(view.TopPaths);
        Assert.NotNull(view.ErrorPaths);
    }
}
