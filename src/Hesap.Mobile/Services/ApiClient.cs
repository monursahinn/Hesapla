using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hesap.Mobile.Models.Api;

namespace Hesap.Mobile.Services;

/// <summary>Hesap API'si için tipli istemci. Belirteç ekleme/yenileme AuthHeaderHandler'dadır.</summary>
public sealed class ApiClient
{
    public const string AuthenticatedClient = "hesap-api";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpFactory;

    public ApiClient(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public Task<ApiResult<Me>> GetMeAsync() => GetAsync<Me>("api/auth/me");

    public Task<ApiResult<Dashboard>> GetDashboardAsync() => GetAsync<Dashboard>("api/reports/dashboard");

    // ---------------- Cariler ----------------
    public Task<ApiResult<Paged<Customer>>> GetCustomersAsync(string? q, string? type, int page, int pageSize = 30) =>
        GetAsync<Paged<Customer>>("api/customers" + Query(("q", q), ("type", type), ("page", page.ToString()), ("pageSize", pageSize.ToString())));

    public Task<ApiResult<Statement>> GetStatementAsync(long id, DateTime from, DateTime to) =>
        GetAsync<Statement>($"api/customers/{id}/statement" + Query(("from", from.ToString("yyyy-MM-dd")), ("to", to.ToString("yyyy-MM-dd"))));

    // ---------------- Faturalar ----------------
    public Task<ApiResult<InvoiceListResult>> GetInvoicesAsync(string? type, string? status, string? q, long? customerId, int page, int pageSize = 30) =>
        GetAsync<InvoiceListResult>("api/invoices" + Query(("type", type), ("status", status), ("q", q), ("customerId", customerId?.ToString()),
            ("page", page.ToString()), ("pageSize", pageSize.ToString())));

    public Task<ApiResult<Invoice>> GetInvoiceAsync(long id) => GetAsync<Invoice>($"api/invoices/{id}");

    /// <summary>Fatura PDF'ini indirir (oturum belirteciyle).</summary>
    public async Task<ApiResult<byte[]>> GetInvoicePdfAsync(long id)
    {
        try
        {
            using var response = await _httpFactory.CreateClient(AuthenticatedClient).GetAsync($"api/invoices/{id}/pdf");
            if (!response.IsSuccessStatusCode)
                return await ReadAsync<byte[]>(response);
            return ApiResult<byte[]>.Ok(await response.Content.ReadAsByteArrayAsync(), "");
        }
        catch (Exception ex)
        {
            return ApiResult<byte[]>.FromException(ex);
        }
    }

    // ---------------- Kasa / banka ----------------
    public Task<ApiResult<List<Account>>> GetAccountsAsync() => GetAsync<List<Account>>("api/accounts");

    public Task<ApiResult<List<Category>>> GetCategoriesAsync() => GetAsync<List<Category>>("api/categories");

    public Task<ApiResult<Paged<Movement>>> GetMovementsAsync(string? type, long? accountId, int page) =>
        GetAsync<Paged<Movement>>("api/movements" + Query(("type", type), ("accountId", accountId?.ToString()), ("page", page.ToString()), ("pageSize", "30")));

    public Task<ApiResult<object>> CreateMovementAsync(object request) => PostAsync<object>("api/movements", request);

    // ---------------- Hesap ----------------
    public Task<ApiResult<object>> ChangePasswordAsync(string currentPassword, string newPassword) =>
        PostAsync<object>("api/auth/change-password", new { currentPassword, newPassword });

    // ---------------- Altyapı ----------------
    private async Task<ApiResult<T>> GetAsync<T>(string path)
    {
        try
        {
            using var response = await _httpFactory.CreateClient(AuthenticatedClient).GetAsync(path);
            return await ReadAsync<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.FromException(ex);
        }
    }

    private async Task<ApiResult<T>> DeleteAsync<T>(string path)
    {
        try
        {
            using var response = await _httpFactory.CreateClient(AuthenticatedClient).DeleteAsync(path);
            return await ReadAsync<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.FromException(ex);
        }
    }

    private async Task<ApiResult<T>> PostAsync<T>(string path, object? body = null)
    {
        try
        {
            using var response = body == null
                ? await _httpFactory.CreateClient(AuthenticatedClient).PostAsync(path, null)
                : await _httpFactory.CreateClient(AuthenticatedClient).PostAsJsonAsync(path, body, Json);

            return await ReadAsync<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.FromException(ex);
        }
    }

    private async Task<ApiResult<T>> PutAsync<T>(string path, object body)
    {
        try
        {
            using var response = await _httpFactory.CreateClient(AuthenticatedClient).PutAsJsonAsync(path, body, Json);
            return await ReadAsync<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.FromException(ex);
        }
    }

    /// <summary>Sunucu yanıtını çözer: ApiResponse&lt;T&gt; zarfı ({ success, message, data }).</summary>
    internal static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var status = response.StatusCode;

        if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart()[0] is not ('{' or '['))
        {
            return response.IsSuccessStatusCode
                ? ApiResult<T>.Ok(default, "")
                : ApiResult<T>.Fail(DefaultMessage(status), status);
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var successProp))
            {
                var envelope = root.Deserialize<ApiResponse<T>>(Json);
                var message = string.IsNullOrWhiteSpace(envelope?.Message) ? DefaultMessage(status) : envelope!.Message;

                return successProp.GetBoolean() && response.IsSuccessStatusCode
                    ? ApiResult<T>.Ok(envelope!.Data, envelope.Message)
                    : ApiResult<T>.Fail(message, status);
            }

            if (!response.IsSuccessStatusCode)
            {
                var msg = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m)
                    ? m.GetString()
                    : null;

                return ApiResult<T>.Fail(string.IsNullOrWhiteSpace(msg) ? DefaultMessage(status) : msg!, status);
            }

            return ApiResult<T>.Ok(root.Deserialize<T>(Json), "");
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail("Sunucu yanıtı okunamadı.", status);
        }
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Oturumunuz doğrulanamadı. Lütfen tekrar giriş yapın.",
        HttpStatusCode.Forbidden => "Bu işlem için yetkiniz yok.",
        HttpStatusCode.NotFound => "Kayıt bulunamadı.",
        HttpStatusCode.PaymentRequired => "Abonelik süreniz doldu. Yeni kayıt için aboneliğinizi yenileyin.",
        HttpStatusCode.Locked => "Hesabınız kilitlendi. Web panelinden \"Şifremi unuttum\" ile yeni şifre belirleyin.",
        HttpStatusCode.TooManyRequests => "Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin.",
        >= HttpStatusCode.InternalServerError => "Sunucuda bir hata oluştu. Lütfen daha sonra tekrar deneyin.",
        _ => "İşlem tamamlanamadı."
    };

    private static string Query(params (string key, string? value)[] items)
    {
        var parts = items
            .Where(i => !string.IsNullOrWhiteSpace(i.value))
            .Select(i => $"{Uri.EscapeDataString(i.key)}={Uri.EscapeDataString(i.value!)}")
            .ToList();

        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }
}
