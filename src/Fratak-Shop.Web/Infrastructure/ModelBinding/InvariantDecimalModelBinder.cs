using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Fratak_Shop.Web.Infrastructure.ModelBinding;

/// <summary>
/// Binds machine-written decimals (map coordinates from JavaScript: "35.6892") independent of the
/// server culture – with a Persian server culture the default binder expects "٫" and would reject them.
/// </summary>
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);

        if (TryParse(value.FirstValue, out var number))
            bindingContext.Result = ModelBindingResult.Success(number);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "مقدار عددی معتبر نیست.");

        return Task.CompletedTask;
    }

    /// <summary>Empty → null; accepts Latin or Persian digits and "." or "٫" as decimal separator.</summary>
    public static bool TryParse(string? text, out decimal? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = new string(text.Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            '٫' => '.',
            _ => c,
        }).ToArray());

        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return false;

        number = parsed;
        return true;
    }
}
