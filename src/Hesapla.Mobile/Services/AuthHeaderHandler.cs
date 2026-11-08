using System.Net;
using System.Net.Http.Headers;

namespace Hesapla.Mobile.Services;

/// <summary>
/// Her isteğe Bearer token ekler. Sunucu 401 dönerse token'ı bir kez yenileyip isteği tekrarlar.
/// </summary>
public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly AuthService _auth;

    public AuthHeaderHandler(AuthService auth)
    {
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _auth.GetAccessTokenAsync(ct: ct);

        if (token == null)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // Token sunucuda geçersiz sayıldı (ör. anahtar değişti): bir kez yenileyip tekrar dene.
        var refreshed = await _auth.GetAccessTokenAsync(forceRefresh: true, ct: ct);

        if (refreshed == null)
            return response;

        response.Dispose();

        using var retry = await CloneAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);

        return await base.SendAsync(retry, ct);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var h in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        foreach (var h in request.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

        return clone;
    }
}
