using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Hesap.Infrastructure;

/// <summary>
/// Hatalar her zaman { success:false, message } zarfıyla ve Türkçe döner:
///  • doğrulama hataları alan adıyla ("Unvan en fazla 200 karakter olabilir."),
///  • okunamayan JSON için ayrıştırıcının iç mesajı gösterilmez,
///  • iş kuralı hataları (AppException) kendi mesajı ve durum koduyla,
///  • veritabanı uzunluk/çakışma hataları anlaşılır mesajla, beklenmeyen hatalar yığın izi göstermeden (500).
/// </summary>
public static class ErrorHandling
{
    /// <summary>ER_CHECK_CONSTRAINT_VIOLATED (3819): MySqlConnector'da adlandırılmış karşılığı yok.</summary>
    private const MySqlErrorCode CheckViolated = (MySqlErrorCode)3819;

    private static readonly Dictionary<string, string> FieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Title"] = "Unvan", ["CompanyName"] = "Firma unvanı", ["FullName"] = "Ad soyad", ["Name"] = "Ad", ["Email"] = "E-posta",
        ["Phone"] = "Telefon", ["Password"] = "Şifre", ["NewPassword"] = "Yeni şifre", ["CurrentPassword"] = "Mevcut şifre",
        ["TaxNumber"] = "Vergi/T.C. no", ["TaxOffice"] = "Vergi dairesi", ["Address"] = "Adres", ["City"] = "İl", ["District"] = "İlçe",
        ["Code"] = "Kod", ["Unit"] = "Birim", ["VatRate"] = "KDV oranı", ["SalePrice"] = "Satış fiyatı", ["PurchasePrice"] = "Alış fiyatı",
        ["OpeningBalance"] = "Açılış bakiyesi", ["OpeningStock"] = "Açılış stoğu", ["CriticalStock"] = "Kritik stok", ["PaymentTermDays"] = "Vade günü",
        ["Notes"] = "Not", ["Description"] = "Açıklama", ["Amount"] = "Tutar", ["Date"] = "Tarih", ["DueDate"] = "Vade tarihi",
        ["Quantity"] = "Miktar", ["UnitPrice"] = "Birim fiyat", ["DiscountRate"] = "İskonto oranı", ["Lines"] = "Fatura satırları",
        ["Iban"] = "IBAN", ["BankName"] = "Banka adı", ["DocumentNo"] = "Belge no", ["Reason"] = "Neden", ["Role"] = "Rol",
        ["Type"] = "Tür", ["CustomerId"] = "Cari", ["AccountId"] = "Kasa/banka hesabı", ["TargetAccountId"] = "Karşı hesap",
        ["CategoryId"] = "Kategori", ["InvoiceId"] = "Fatura", ["InvoiceNote"] = "Fatura notu", ["Token"] = "Bağlantı"
    };

    private static string Label(string key)
    {
        var last = Regex.Replace(key.Split('.').Last().TrimStart('$'), @"\[\d+\]$", "");
        return FieldNames.TryGetValue(last, out var l) ? l : (string.IsNullOrWhiteSpace(last) ? "Gönderilen veri" : last);
    }

    private static string Translate(string key, string error)
    {
        var label = Label(key);

        if (error.Contains("field is required", StringComparison.OrdinalIgnoreCase))
            return $"{label} zorunludur.";

        var len = Regex.Match(error, @"maximum length of '?(\d+)'?");
        if (len.Success)
            return $"{label} en fazla {len.Groups[1].Value} karakter olabilir.";

        if (error.Contains("not a valid e-mail", StringComparison.OrdinalIgnoreCase))
            return "Geçerli bir e-posta adresi girin.";

        if (error.Contains("must be between", StringComparison.OrdinalIgnoreCase))
            return $"{label} izin verilen aralıkta değil.";

        return error;
    }

    public static IMvcBuilder AddApiEnvelope(this IMvcBuilder mvc) =>
        mvc.ConfigureApiBehaviorOptions(o =>
        {
            o.InvalidModelStateResponseFactory = ctx =>
            {
                // Gövde JSON olarak okunamadıysa ayrıştırıcı mesajı gösterilmez; tür uyuşmazlığı alan adıyla bildirilir.
                var jsonErrors = ctx.ModelState.Where(x => x.Key.StartsWith('$') && x.Value?.Errors.Count > 0).ToList();
                if (jsonErrors.Count > 0)
                {
                    var typed = jsonErrors.FirstOrDefault(x => x.Key.Length > 2 &&
                        x.Value!.Errors.Any(e => (e.Exception?.Message ?? e.ErrorMessage).Contains("could not be converted", StringComparison.OrdinalIgnoreCase)));
                    var msg = typed.Key is null ? "Gönderilen veri okunamadı." : $"{Label(typed.Key)} için gönderilen değer geçersiz.";
                    return new BadRequestObjectResult(new ApiResponse(false, msg, null, new Dictionary<string, string[]> { [typed.Key ?? "body"] = [msg] }));
                }

                var errors = ctx.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        x => string.IsNullOrEmpty(x.Key) ? "body" : x.Key,
                        x => x.Value!.Errors.Select(e => Translate(x.Key, string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message ?? "" : e.ErrorMessage)).Distinct().ToArray());

                var message = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Gönderilen bilgiler geçersiz.";
                return new BadRequestObjectResult(new ApiResponse(false, message, null, errors));
            };
        });

    public static string FriendlyDbMessage(MySqlException ex) => ex.ErrorCode switch
    {
        MySqlErrorCode.DuplicateKeyEntry => "Bu kayıt zaten var (kod, ad veya numara tekrar ediyor).",
        MySqlErrorCode.DataTooLong => "Girilen bilgilerden biri izin verilen uzunluğu aşıyor.",
        MySqlErrorCode.WarningDataOutOfRange => "Girilen sayılardan biri izin verilen aralığın dışında.",
        CheckViolated => "Girilen bilgiler geçerlilik kurallarına uymuyor.",
        MySqlErrorCode.NoReferencedRow2 or MySqlErrorCode.NoReferencedRow => "Seçilen kayıt bulunamadı.",
        MySqlErrorCode.RowIsReferenced2 or MySqlErrorCode.RowIsReferenced => "Bu kayıt başka kayıtlarda kullanıldığı için silinemez.",
        _ => "İşlem şu anda tamamlanamadı. Lütfen biraz sonra tekrar deneyin."
    };

    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
            {
                var (status, message) = ex switch
                {
                    AppException a => (a.Status, a.Message),
                    MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } m => (StatusCodes.Status409Conflict, FriendlyDbMessage(m)),
                    MySqlException m when m.ErrorCode is MySqlErrorCode.DataTooLong or MySqlErrorCode.WarningDataOutOfRange
                        or CheckViolated or MySqlErrorCode.NoReferencedRow2 or MySqlErrorCode.RowIsReferenced2
                        => (StatusCodes.Status400BadRequest, FriendlyDbMessage(m)),
                    BadHttpRequestException b => (b.StatusCode, "İstek okunamadı."),
                    _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.")
                };

                // Kullanıcı hataları (doğrulama, çakışma) günlüğü kirletmesin; yalnızca beklenmeyenler yazılır.
                if (status == StatusCodes.Status500InternalServerError)
                    ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Hesap.Api")
                        .LogError(ex, "API hatası: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

                ctx.Response.Clear();
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiResponse(false, message), JsonSerializerOptions.Web));
            }
        });
}
