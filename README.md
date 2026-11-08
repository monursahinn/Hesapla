# Hesapla — KOBİ ön muhasebe (SaaS)

Küçük ve orta ölçekli işletmeler için abonelikle satılan çok firmalı ön muhasebe yazılımı:
cari hesaplar, satış/alış faturaları, kasa-banka, tahsilat/ödeme, gelir/gider, raporlar.
Web paneli (React) + mobil uygulama (.NET MAUI, Android/iOS) aynı API'yi kullanır.

## Özellikler

| Alan | İçerik |
|---|---|
| Cariler | Müşteri / tedarikçi / ikisi; vergi no – T.C. doğrulama, vade günü, ekstre (yürüyen bakiye, dönem devri), Excel |
| Ürün / hizmet | Kod, birim, satış/alış fiyatı, KDV (%0/1/10/20), stok takibi ve kritik stok |
| Faturalar | Satış (SAT) ve alış (ALS) faturaları, satır iskontosu, KDV dökümü, vade, PDF, iptal (silinmez) |
| Kasa / banka | Tahsilat, ödeme, gider, gelir (KDV'li), virman; faturaya bağlama; kasa eksiye düşemez |
| Raporlar | Pano, kâr/zarar, KDV özeti (hesaplanan/indirilecek), alacak yaşlandırma, Excel |
| Firma | Firma bilgileri ve logo (PDF'e basılır), kullanıcılar ve roller, gelir/gider kategorileri |
| Güvenlik | Firma verisi tam izole, denetim kaydı, giriş kilidi, hız sınırı, CSRF, güvenlik başlıkları |
| Abonelik | 14 gün deneme, paket bazlı kullanıcı sınırı, süre dolunca salt okunur, askıya alma (platform paneli) |
| Mobil | Pano, cariler + ekstre, faturalar + PDF paylaşımı, kasa-banka + yeni kayıt, tema, şifre değiştirme |

Roller: **Yönetici** (her şey), **Muhasebe** (kayıt girer, firma/kullanıcı ayarı yapamaz), **İzleyici** (salt okunur).
Platform yöneticisi (siz) firmaları, abonelik bitiş tarihlerini ve paketleri `/platform` ekranından yönetir.

## Klasör yapısı

```
Hesapla.slnx                 API çözümü (mobil proje ayrı derlenir, aşağıya bakın)
db/schema.sql              Veritabanı şeması (API'ye gömülüdür; açılışta uygulanır)
db/make-doc.py             docs/VERITABANI-KURULUM.txt dosyasını şemadan üretir
docs/VERITABANI-KURULUM.txt  Yerel ve sunucu kurulum betikleri (MySQL)
src/Hesapla.Api/             ASP.NET Core 10 API + web panelinin barındırılması
src/Hesapla.Api/ClientApp/   React + Vite + Mantine web paneli (derlenince wwwroot/app)
src/Hesapla.Mobile/          .NET MAUI mobil uygulama
```

## Geliştirme ortamı

Gereksinimler: .NET 10 SDK, Node.js 20+, MySQL 8.0.16+ (MariaDB desteklenmez).
Mobil için ek olarak .NET MAUI iş yükü ve Android SDK (iOS için Mac + Xcode).

1. Veritabanını oluşturun: `docs/VERITABANI-KURULUM.txt` → BÖLÜM A.
2. Gizli ayarları user-secrets ile girin (dosyaya yazmayın):

   ```bash
   cd src/Hesapla.Api
   dotnet user-secrets set "ConnectionStrings:Hesapla" "Server=127.0.0.1;Port=3306;Database=hesapla;User ID=hesapla_app;Password=...;"
   dotnet user-secrets set "Jwt:Key" "<en az 32 karakterlik rastgele anahtar>"
   dotnet user-secrets set "Setup:SuperUserEmail" "siz@alanadiniz.com"
   dotnet user-secrets set "Setup:SuperUserPassword" "<güçlü şifre>"
   ```

3. API: `dotnet run --project src/Hesapla.Api` → http://localhost:5300
4. Web paneli (sıcak yenileme): `cd src/Hesapla.Api/ClientApp && npm install && npm run dev` → http://localhost:5174
   (API isteklerini 5300'e yönlendirir). `npm run build` çıktısı `wwwroot/app`'e yazılır ve API tarafından sunulur.
5. Mobil (Android emülatörü): `cd src/Hesapla.Mobile && dotnet build -f net10.0-android -t:Install`
   Debug sürüm emülatörde `http://10.0.2.2:5300/` adresine bağlanır.

İlk açılışta platform yöneticisi hesabı `Setup:*` değerleriyle oluşturulur. Firmalar `/register`
sayfasından kendileri kaydolur (14 gün deneme).

## Sunucuya kurulum

1. Veritabanı: `docs/VERITABANI-KURULUM.txt` → BÖLÜM B (uygulama kullanıcısının yalnızca veri yetkileri olması önerilir;
   bu durumda şemayı BÖLÜM C ile elle kurun).
2. Yayın paketi (web paneli otomatik derlenir):

   ```bash
   dotnet publish src/Hesapla.Api -c Release -o ./publish
   ```

3. Sunucuda ayarları **ortam değişkeni** olarak verin:

   | Değişken | Açıklama |
   |---|---|
   | `ConnectionStrings__Hesapla` | MySQL bağlantı cümlesi |
   | `Jwt__Key` | En az 32 karakter rastgele anahtar (mobil oturumlar) |
   | `App__PublicBaseUrl` | `https://alanadiniz.com` — şifre sıfırlama e-postalarındaki bağlantı |
   | `Setup__SuperUserEmail` / `Setup__SuperUserPassword` | İlk platform yöneticisi (oluştuktan sonra kaldırılabilir) |
   | `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromEmail`, `Smtp__FromName` | Şifre sıfırlama e-postası |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |

4. Uygulamayı IIS veya Linux'ta (systemd + Nginx/Caddy ters vekil) çalıştırın; **HTTPS zorunludur**
   (oturum çerezi `Secure`). Ters vekil `X-Forwarded-*` başlıklarını iletmelidir.
5. Firma logoları `App_Data/logos` klasöründe tutulur; yedekleme ve yazma izni bu klasörü kapsamalıdır.
6. Yedekleme: veritabanının günlük `mysqldump` yedeği + `App_Data` klasörü.

## Mobil uygulamanın yayını

- Canlı API adresi derleme sırasında verilir:
  `dotnet publish src/Hesapla.Mobile -f net10.0-android -c Release -p:HesaplaApiBaseUrl=https://alanadiniz.com/`
- Mağazaya göndermeden önce `Hesapla.Mobile.csproj` içindeki `ApplicationId` (`com.hesapla.mobile`) ve
  `HesaplaApiBaseUrl` varsayılanı (`https://hesapla.example.com/`) kendi değerlerinizle değiştirilmelidir.
- Android imzalama anahtarı (keystore) ve iOS sertifikaları sizin hesaplarınızla oluşturulur.
- Mobil uygulama firma kullanıcıları içindir; fatura kesimi, cari/ürün tanımı ve raporlar web panelindedir.

## Satış öncesi kontrol listesi

- **Alan adı ve marka**: ürün adı "Hesapla"dır; alan adını aldıktan sonra `App:PublicBaseUrl`, mobil
  `HesaplaApiBaseUrl` ve `ApplicationId` buna göre güncellenmelidir. Logo: `ClientApp/public/brand`, mobil `Resources`.
- **QuestPDF lisansı**: Community lisansı yıllık cirosu 1 milyon USD'nin altındaki şirketler için ücretsizdir.
  Yazılımı satan şirketin cirosu bu sınırı aşarsa Professional/Enterprise lisans gerekir.
- **Abonelik ödemesi**: aboneliğin kartla tahsili henüz yok; süre uzatma platform panelinden elle yapılır.
- **KVKK**: aydınlatma metni, veri işleme sözleşmesi ve sunucu konumu (yurt içi) satış öncesi hazırlanmalıdır.

## Bilinen kapsam dışı (sonraki sürümler)

e-Fatura / e-Arşiv entegrasyonu, çek-senet takibi, aboneliğin kartla otomatik tahsili, mobilde fatura kesimi,
döviz cinsinden işlemler, çoklu depo.

## Testler

API davranışı (izolasyon, güvenlik, fatura matematiği, eşzamanlılık, abonelik) uçtan uca bir senaryo setiyle
doğrulanmıştır (135 kontrol); web paneli ve Android uygulaması emülatörde uçtan uca denenmiştir.
