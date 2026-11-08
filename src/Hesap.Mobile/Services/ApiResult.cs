using System.Net;

namespace Hesap.Mobile.Services;

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = "";
    public T? Data { get; init; }
    public HttpStatusCode? StatusCode { get; init; }

    public static ApiResult<T> Ok(T? data, string message) =>
        new() { IsSuccess = true, Data = data, Message = message, StatusCode = HttpStatusCode.OK };

    public static ApiResult<T> Fail(string message, HttpStatusCode? status = null) =>
        new() { IsSuccess = false, Message = message, StatusCode = status };

    public static ApiResult<T> FromException(Exception ex) => ex switch
    {
        TaskCanceledException => Fail("Sunucu zamanında yanıt vermedi. Lütfen tekrar deneyin."),
        HttpRequestException => Fail("Sunucuya bağlanılamadı. İnternet bağlantınızı kontrol edin."),
        _ => Fail("Beklenmeyen bir hata oluştu.")
    };
}
