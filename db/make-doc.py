# docs/VERITABANI-KURULUM.txt dosyasını db/schema.sql'den üretir (şema değişince yeniden çalıştırın):
#   python db/make-doc.py
import io
import os

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
schema = io.open(os.path.join(root, "db", "schema.sql"), encoding="utf-8").read().strip()

doc = f"""HESAP — VERİTABANI KURULUM REHBERİ VE BETİKLERİ
==================================================================================================

Gereksinim: MySQL 8.0.16 veya üzeri (CHECK kısıtları için). MariaDB desteklenmez.
Karakter seti: utf8mb4, sıralama: utf8mb4_0900_ai_ci (Türkçe karakterler ve emoji için).

Not: Uygulama açılışta tabloları kendisi de oluşturur (CREATE TABLE IF NOT EXISTS). Veritabanı kullanıcısının
CREATE yetkisi yoksa (çoğu paylaşımlı hosting) aşağıdaki BÖLÜM C betiğini elle çalıştırın. Betik tekrar
çalıştırılabilir; var olan tablolara ve verilere dokunmaz.


==================================================================================================
BÖLÜM A — YEREL BİLGİSAYARDA KURULUM (geliştirme / deneme)
==================================================================================================

A1) MySQL 8'e yönetici (root) olarak bağlanın (MySQL Workbench veya komut satırı):

    mysql -u root -p

A2) Veritabanını ve uygulamaya özel kullanıcıyı oluşturun. Uygulama root ile ÇALIŞTIRILMAMALI.
    'GucluBirSifre_Degistirin' yerine kendi şifrenizi yazın:

    CREATE DATABASE IF NOT EXISTS hesap CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
    CREATE USER IF NOT EXISTS 'hesap_app'@'localhost' IDENTIFIED BY 'GucluBirSifre_Degistirin';
    GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, REFERENCES ON hesap.* TO 'hesap_app'@'localhost';
    FLUSH PRIVILEGES;

A3) Tabloları oluşturun (proje klasöründeyken):

    mysql -u root -p hesap < db/schema.sql

    veya MySQL Workbench'te "hesap" şemasını seçip BÖLÜM C'deki betiğin tamamını yapıştırıp çalıştırın.

A4) Bağlantı dizesini ve gizli ayarları user-secrets'a girin (şifreler dosyalara ve Git'e yazılmaz):

    cd src/Hesap.Api
    dotnet user-secrets set "ConnectionStrings:Hesap" "Server=localhost;Port=3306;Database=hesap;Uid=hesap_app;Pwd=GucluBirSifre_Degistirin;SslMode=None;AllowPublicKeyRetrieval=True;"
    dotnet user-secrets set "Jwt:Key" "<en az 64 karakter rastgele metin>"
    dotnet user-secrets set "Setup:SuperUserEmail" "siz@firmaniz.com"
    dotnet user-secrets set "Setup:SuperUserPassword" "<en az 8 karakter, harf ve rakam>"

    Rastgele anahtar üretmek için (PowerShell):
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | ForEach-Object {{[char]$_}})

A5) Uygulamayı başlatın:

    dotnet run --project src/Hesap.Api

    İlk açılışta platform yöneticisi (Setup:SuperUserEmail) oluşturulur. Giriş yaptıktan sonra
    Setup:SuperUserPassword ayarını silin:

    dotnet user-secrets remove "Setup:SuperUserPassword"


==================================================================================================
BÖLÜM B — SUNUCUDA KURULUM (canlı ortam)
==================================================================================================

B1) Veritabanı ve kullanıcı
    • Kendi sunucunuz / VPS: BÖLÜM A2'deki komutları çalıştırın; 'localhost' yerine uygulama sunucusunun
      adresini yazın (uygulama ve MySQL aynı makinedeyse 'localhost' kalır).
    • Paylaşımlı hosting (Plesk, cPanel, site4now vb.): veritabanını ve kullanıcıyı hosting panelinden
      oluşturun (karakter seti utf8mb4 seçin). Bu ortamlarda CREATE DATABASE / CREATE USER yetkisi yoktur.

B2) Tabloları oluşturun
    • phpMyAdmin: veritabanını seçin → "İçe aktar" → db/schema.sql dosyasını yükleyin
      (veya "SQL" sekmesine BÖLÜM C'yi yapıştırın).
    • MySQL Workbench ile uzaktan bağlanıp BÖLÜM C'yi çalıştırın.
    • Komut satırı: mysql -h SUNUCU -u KULLANICI -p VERITABANI < db/schema.sql

B3) Uygulama ayarları — sunucuda ortam değişkeni olarak verin (IIS: web.config environmentVariables,
    Linux: systemd Environment=, hosting paneli: "Environment Variables"):

    ConnectionStrings__Hesap   = Server=...;Port=3306;Database=...;Uid=...;Pwd=...;SslMode=Required;
    Jwt__Key                   = <en az 64 karakter rastgele, yerelden FARKLI>
    App__PublicBaseUrl         = https://alanadiniz.com   (şifre sıfırlama bağlantıları bununla kurulur)
    App__Name                  = Ürün adınız             (ekranlarda ve e-postalarda görünür)
    Smtp__Host, Smtp__Port, Smtp__Username, Smtp__Password, Smtp__FromEmail, Smtp__FromName
    Setup__SuperUserEmail, Setup__SuperUserPassword        (yalnızca ilk açılış için; sonra silin)

    Uzak MySQL bağlantısında SslMode=Required kullanın. Sunucu ile veritabanı aynı makinedeyse SslMode=None olabilir.

B4) Yedekleme (her gün önerilir; yedeği sunucu dışında saklayın):

    mysqldump -h SUNUCU -u KULLANICI -p --single-transaction --routines --triggers VERITABANI > hesap-yedek-$(date +%F).sql

    Geri yükleme:
    mysql -h SUNUCU -u KULLANICI -p VERITABANI < hesap-yedek-2026-11-08.sql

B5) Kurulum kontrolü (13 tablo görünmeli):

    SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME;


==================================================================================================
BÖLÜM C — ŞEMA BETİĞİ (db/schema.sql ile aynıdır)
==================================================================================================

{schema}


==================================================================================================
BÖLÜM D — SIK KULLANILAN YÖNETİM SORGULARI
==================================================================================================

-- Bir firmanın aboneliğini uzatmak (normalde platform panelinden yapılır):
UPDATE t_firmalar SET PAKET = 'ST', ABONELIK_BITIS = '2027-12-31' WHERE FIRMA_ID = 1;

-- Kilitlenmiş bir kullanıcının kilidini açmak:
UPDATE t_kullanicilar SET KILITLI_MI = 0, HATALI_GIRIS = 0 WHERE EPOSTA = 'kullanici@firma.com';

-- Süresi dolmuş firmalar:
SELECT FIRMA_ID, UNVAN, EPOSTA, ABONELIK_BITIS FROM t_firmalar WHERE ABONELIK_BITIS < CURDATE() ORDER BY ABONELIK_BITIS;

-- Eski denetim kayıtlarını temizlemek (2 yıldan eski):
DELETE FROM t_denetim WHERE TARIH < DATE_SUB(NOW(), INTERVAL 2 YEAR) LIMIT 10000;
"""

out = os.path.join(root, "docs", "VERITABANI-KURULUM.txt")
os.makedirs(os.path.dirname(out), exist_ok=True)
io.open(out, "w", encoding="utf-8", newline="\r\n").write(doc)
print("yazıldı:", out)
