using System.Text;
using System.Threading.RateLimiting;
using Hesapla.Features;
using Hesapla.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

// Yanıtlarda "Server: Kestrel" başlığı gönderilmez.
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Jwt:Key tanımlı değil veya 32 bayttan kısa. Geliştirmede 'dotnet user-secrets', sunucuda Jwt__Key ortam değişkeni kullanın.");

builder.Services
    .AddControllers(o => o.Filters.Add<SubscriptionFilter>())
    .AddApiEnvelope();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<Db>();
builder.Services.AddScoped<Audit>();
builder.Services.AddScoped<EmailSender>();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddScoped<SubscriptionFilter>();
builder.Services.AddFeatures();

// Web (React) çerezle, mobil uygulama "Authorization: Bearer" ile kimlik doğrular.
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = "Smart";
        o.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme("Smart", "Çerez veya JWT", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.Cookie.Name = "hesapla.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        // API isteklerinde giriş sayfasına yönlendirme yerine 401/403 döner.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.RequireHttpsMetadata = !isDevelopment;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "hesapla",
            ValidAudience = "hesapla-mobile",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.FirmRead, p => p.RequireRole(Roles.Admin, Roles.Accountant, Roles.Viewer));
    o.AddPolicy(Policies.FirmWrite, p => p.RequireRole(Roles.Admin, Roles.Accountant));
    o.AddPolicy(Policies.FirmAdmin, p => p.RequireRole(Roles.Admin));
    o.AddPolicy(Policies.Platform, p => p.RequireRole(Roles.Platform));
});

// Giriş, kayıt ve şifre sıfırlama: IP başına dakikada sınırlı istek (kaba kuvvet ve kayıt spamına karşı).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permit = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 30);
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ApiResponse(false, "Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin."), ct);
    };
});

var app = builder.Build();

await DbInit.InitializeAsync(app.Services, app.Logger);

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });

// Güvenlik başlıkları: tüm betik/yazı tipi/resim aynı sunucudan; satır içi betik yok (Mantine satır içi stil kullanır).
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
                                   "font-src 'self' data:; connect-src 'self'; frame-src 'self' blob:; object-src 'none'; base-uri 'self'; " +
                                   "form-action 'self'; frame-ancestors 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    if (ctx.Request.Path.StartsWithSegments("/api"))
        h["Cache-Control"] = "no-store";
    await next();
});

if (!isDevelopment)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseApiErrors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Çerezle gelen durum değiştiren API isteklerinde özel başlık zorunlu (CSRF): başka bir site bu başlığı CORS izni
// olmadan gönderemez. Mobil uygulama Bearer belirteciyle geldiği için muaftır.
app.Use(async (ctx, next) =>
{
    var req = ctx.Request;
    if (req.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method) && !HttpMethods.IsOptions(req.Method) &&
        !req.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
        req.Headers["X-Requested-With"] != "XMLHttpRequest")
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new ApiResponse(false, "Geçersiz istek."));
        return;
    }

    await next();
});

app.MapControllers();

// Bilinmeyen API adresleri 404 (zarfla); diğer her şey React uygulamasına düşer.
app.MapFallback("/api/{**path}", (HttpContext ctx) =>
    Results.Json(new ApiResponse(false, "Adres bulunamadı."), statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("app/index.html");

app.Run();
