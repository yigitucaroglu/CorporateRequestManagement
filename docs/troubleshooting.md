# Development Troubleshooting

## DefaultConnection yapılandırılmalıdır

`powershell -ExecutionPolicy Bypass -File .\scripts\setup-development.ps1` çalıştırın. API projesindeki `UserSecretsId` store'unda `ConnectionStrings:DefaultConnection` ve `Jwt:SigningKey` anahtarlarının bulunduğunu, değerleri ekrana basmadan doğrulayın. Secret'ı `appsettings*.json` veya Git kapsamındaki başka bir dosyaya yazmayın.

## DLL başka process tarafından kullanılıyor

Önce `.\scripts\stop-development.ps1` çalıştırın. Script yalnız state/port ve repository command line doğrulamasından geçen API/Web process tree'lerini kapatır. Çalışan API/Web varken clean veya aynı output'u değiştiren build çalıştırmayın.

## 7001 veya 7002 kullanımda

`run-development.ps1` owning PID ve process adını bildirir. Process bu repository'ye aitse stop script'ini veya bilinçli olarak `-ForceRestart` kullanın. Başka uygulamaya aitse script onu kapatmaz; uygulamayı kendiniz durdurun veya farklı `-ApiUrl`/`-WebUrl` seçin.

## HTTPS certificate

```powershell
dotnet dev-certs https --check
dotnet dev-certs https --trust
```

HTTP fallback kullanmayın; canonical adresler API için 7001, Web için 7002 HTTPS'tir.

## Seed login çalışmıyor

`ASPNETCORE_ENVIRONMENT=Development` olduğundan, doğru Development database'e bağlandığınızdan ve initializer'ın tamamlandığından emin olun. Setup script'ini yeniden çalıştırmak migration ve seed açısından idempotent'tir. Hesaplar `admin@company.com`, `manager@company.com`, `employee@company.com`; Development-only parola README tablosundadır.

## SQL bağlantı veya yetki problemi

Server/named-instance, Windows veya SQL Authentication modu ve login durumunu doğrulayın. Setup ve integration testleri master bağlantısı ile minimum `CREATE ANY DATABASE` yetkisi ister; sysadmin gerekmez. SQL parolasını command line'a veya repository dosyasına yazmayın.

## Integration testleri

Fixture yalnız `CorporateRequestManagement_IntegrationTests_<32-hex-guid>` isimli disposable database'leri siler. `CRM_TEST_SQLSERVER_CONNECTION` service-based SQL Server bağlantısını process environment içinde sağlayabilir veya test script'inin API User Secrets Development bağlantısından güvenli master bağlantısı türetmesine izin verebilirsiniz. LocalDB kabul edilmez; testler 5-7 dakika sürebilir.

## API kapalıyken Web

Web güvenli 503 mesajı gösterir. Normal geliştirmede `run-development.ps1`, API readiness tamamlanmadan Web'i başlatmaz.
