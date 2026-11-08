using System.Reflection;
using Dapper;
using MySqlConnector;

namespace Hesap.Infrastructure;

/// <summary>
/// Veritabanı bağlantısı. Tüm sorgular Dapper ile parametreli çalışır (SQL enjeksiyonuna kapalı).
/// İşlemler READ COMMITTED ile açılır: MySQL'in varsayılanı (REPEATABLE READ) ilk okumadaki anlık görüntüyü
/// kullandığı için, kilit bekledikten sonra yapılan "kalan tutar / kasa bakiyesi" denetimi diğer işlemlerin
/// yeni kayıtlarını görmüyordu (eşzamanlı iki tahsilat faturanın kalanını aşabiliyordu).
/// </summary>
public sealed class Db(IConfiguration config)
{
    private readonly string _connStr = config.GetConnectionString("Hesap")
        ?? throw new InvalidOperationException("ConnectionStrings:Hesap tanımlı değil. Geliştirmede 'dotnet user-secrets', sunucuda ConnectionStrings__Hesap ortam değişkeni kullanın.");

    public async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_connStr);
        await conn.OpenAsync();
        return conn;
    }
}

public static class DbInit
{
    /// <summary>Tabloları gömülü db/schema.sql betiğiyle (yoksa) oluşturur ve ilk platform yöneticisini ekler.</summary>
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<Db>();
        var config = services.GetRequiredService<IConfiguration>();

        await using var conn = await db.OpenAsync();

        await using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hesap.schema.sql")!)
        using (var reader = new StreamReader(stream))
        {
            try
            {
                await conn.ExecuteAsync(await reader.ReadToEndAsync());
            }
            catch (Exception ex)
            {
                // CREATE yetkisi yoksa uygulama yine açılır; betik elle çalıştırılmalıdır (docs/VERITABANI-KURULUM.txt).
                logger.LogError(ex, "Veritabanı şeması uygulanamadı. db/schema.sql betiğini elle çalıştırın.");
            }
        }

        // İlk platform yöneticisi: yalnızca hiç yoksa ve Setup:SuperUserEmail/Password verilmişse oluşturulur.
        var email = config["Setup:SuperUserEmail"];
        var password = config["Setup:SuperUserPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM t_kullanicilar WHERE ROL = 'SU'");
        if (exists > 0)
            return;

        var policyError = PasswordPolicy.Validate(password);
        if (policyError != null)
        {
            logger.LogError("Setup:SuperUserPassword şifre kuralına uymuyor: {Error}", policyError);
            return;
        }

        await conn.ExecuteAsync(
            "INSERT INTO t_kullanicilar (FIRMA_ID, AD_SOYAD, EPOSTA, PAROLA_HASH, ROL) VALUES (NULL, @Name, @Email, @Hash, 'SU')",
            new { Name = "Platform Yöneticisi", Email = email.Trim().ToLowerInvariant(), Hash = Passwords.Hash(password) });

        logger.LogWarning("İlk platform yöneticisi oluşturuldu ({Email}). Setup:SuperUserPassword ayarını şimdi kaldırın.", email);
    }
}
