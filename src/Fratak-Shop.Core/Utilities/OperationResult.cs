namespace Fratak_Shop.Core.Utilities;

public class OperationResult<TData>
{
    private static readonly string SuccessMessage ="عملیات با موفقیت انجام شد" ;
    private static readonly string ErrorMessage = "عملیات با شکست مواجه شد";
    private static readonly string NotFoundMessage = "اطلاعات مورد نظر یافت نشد";

    public string Message { get; private init; } = null!;
    public string? Title { get; set; }
    public EnOperationResultStatus Status { get; private init; }
    public TData Data { get; private init; } = default!;

    /// <summary>کد اختیاری خطا (مثلاً «feature:Projects» از دروازهٔ امکانات) — لایه وب برای لینک ارتقا استفاده می‌کند</summary>
    public string? Code { get; set; }

    public static OperationResult<TData> Success(TData data) =>
        new()
        {
            Status = EnOperationResultStatus.Success,
            Message = SuccessMessage,
            Data = data,
        };

    public static OperationResult<TData> Success(TData data, string message) =>
        new()
        {
            Status = EnOperationResultStatus.Success,
            Message = message,
            Data = data,
        };

    public static OperationResult<TData?> NotFound() =>
        new()
        {
            Status = EnOperationResultStatus.NotFound,
            Data = default(TData),
            Message = NotFoundMessage,
        };

    public static OperationResult<TData?> NotFound(string? message) =>
        new()
        {
            Status = EnOperationResultStatus.NotFound,
            Data = default(TData),
            Message = message ?? NotFoundMessage,
        };

    public static OperationResult<TData?> Error(string? message = null) =>
        new()
        {
            Status = EnOperationResultStatus.Error,
            Data = default(TData),
            Message = message ?? ErrorMessage
        };

    public static OperationResult<TData?> Error(TData? data, string? message = null) =>
        new()
        {
            Status = EnOperationResultStatus.Error,
            Data = data,
            Message = message ?? ErrorMessage
        };
}
public class OperationResult
{
    private static readonly string SuccessMessage = "عملیات با موفقیت انجام شد";
    private static readonly string ErrorMessage = "عملیات با شکست مواجه شد";
    private static readonly string NotFoundMessage = "اطلاعات مورد نظر یافت نشد";

    public string Message { get; set; } = null!;
    public string? Title { get; set; }
    public EnOperationResultStatus Status { get; set; }

    /// <summary>کد اختیاری خطا (مثلاً «feature:Projects» از دروازهٔ امکانات) — لایه وب برای لینک ارتقا استفاده می‌کند</summary>
    public string? Code { get; set; }

    public static OperationResult Error() =>
        new()
        {
            Status = EnOperationResultStatus.Error,
            Message = ErrorMessage,
        };

    public static OperationResult NotFound(string message) =>
        new()
        {
            Status = EnOperationResultStatus.NotFound,
            Message = message,
        };

    public static OperationResult NotFound() =>
        new()
        {
            Status = EnOperationResultStatus.NotFound,
            Message = NotFoundMessage,
        };

    public static OperationResult Error(string message) =>
        new()
        {
            Status = EnOperationResultStatus.Error,
            Message = message,
        };

    public static OperationResult Error(string message, EnOperationResultStatus status) =>
        new()
        {
            Status = status,
            Message = message,
        };
    public static OperationResult Success() =>
        new()
        {
            Status = EnOperationResultStatus.Success,
            Message = SuccessMessage,
        };

    public static OperationResult Success(string message) =>
        new()
        {
            Status = EnOperationResultStatus.Success,
            Message = message,
        };
}

/// <summary>کمکی‌های زنجیره‌ای OperationResult</summary>
public static class OperationResultExtensions
{
    /// <summary>ست‌کردن کد خطا (مثلاً انتقال کد دروازهٔ امکانات به نتیجهٔ ژنریک)</summary>
    public static OperationResult<TData> WithCode<TData>(this OperationResult<TData> result, string? code)
    {
        result.Code = code;
        return result;
    }

    public static OperationResult WithCode(this OperationResult result, string? code)
    {
        result.Code = code;
        return result;
    }

    public static bool IsSuccess(this OperationResult result) => result.Status == EnOperationResultStatus.Success;
    public static bool IsSuccess<TData>(this OperationResult<TData> result) => result.Status == EnOperationResultStatus.Success;
}

public enum EnOperationResultStatus
{
    Error = 10,
    Success = 200,
    NotFound = 404
}