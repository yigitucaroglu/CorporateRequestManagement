# Kurumsal Talep ve Onay Yönetim Sistemi

Çalışanların kurumsal talepler oluşturduğu ve yönetici/yönetim onay akışlarından geçirdiği .NET 8 örnek uygulamasıdır.

## Quick Start

Ön koşullar: Git, Windows PowerShell 5.1 veya PowerShell 7, .NET 8 SDK, HTTPS developer certificate ve `CREATE DATABASE` yetkili service-based SQL Server erişimi.

İlk kurulum; SQL bağlantısını doğrular, connection string ile cryptographic JWT signing key'i Git dışındaki API User Secrets store'una yazar, Release build alır ve Development migration/seed işlemini idempotent çalıştırır:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-development.ps1
```

Uygulamaları API önce, Web sonra olmak üzere readiness kontrollü başlatmak, durdurmak ve bütün kalite kapılarını çalıştırmak için:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-development.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\stop-development.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

- API Swagger: `https://localhost:7001/swagger`
- Web: `https://localhost:7002`
- Development hesapları: aşağıdaki **YALNIZCA DEVELOPMENT** seed tablosu

Demo credentials Production ortamında kullanılmamalıdır. Setup script'i secret değerlerini terminale veya repository'ye yazdırmaz; mevcut ilgisiz User Secrets anahtarlarını korur. Script kullanılamayan ortamlarda aşağıdaki manuel restore, User Secrets, initializer ve run adımları ileri seviye alternatif olarak kullanılabilir. Sorun giderme için [troubleshooting](docs/troubleshooting.md) belgesine bakın.

## Mimari

Backend Clean Architecture sınırlarını izler:

```text
Domain <- Application <- Infrastructure <- Api
```

Domain bağımsızdır; Application yalnızca Domain'e, Infrastructure Application ve Domain'e, Api ise Application ve Infrastructure'a referans verir. Ayrı MVC `Web` projesi backend projelerine referans vermez ve API ile yalnızca HTTP üzerinden iletişim kurar.

## Gereksinimler

- .NET 8 SDK
- SQL Server 2019 veya sonrası

Kurulumu doğrulamak için:

```powershell
dotnet --info
dotnet tool restore
```

## Restore, build ve test

Repository kökünde:

```powershell
dotnet restore .\CorporateRequestManagement.sln
dotnet build .\CorporateRequestManagement.sln --configuration Release --no-restore
dotnet test .\CorporateRequestManagement.sln --configuration Release --no-build
```

Entegrasyon testleri her koşuda `CorporateRequestManagement_IntegrationTests_` önekli benzersiz bir gerçek SQL Server veritabanı oluşturur, migration uygular ve yalnızca kendi oluşturduğu veritabanını siler. Test bağlantısının tek kaynağı `CRM_TEST_SQLSERVER_CONNECTION` ortam değişkenidir; değişken yoksa testler açıkça başarısız olur.

```powershell
$env:CRM_TEST_SQLSERVER_CONNECTION = "<güvenli-test-sql-server-bağlantısı>"
```

## Authentication kurulumu

JWT issuer `CorporateRequestManagement.Api`, audience `CorporateRequestManagement.Clients`, access token ömrü 15 dakika ve refresh token ömrü 7 gündür. Signing key repository dosyalarında tutulmaz; en az 32 byte Development anahtarı user-secrets veya environment üzerinden sağlanır:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<minimum-32-byte-development-key>" --project .\src\CorporateRequestManagement.Api
$env:Jwt__SigningKey = "<minimum-32-byte-development-key>"
```

Endpointler `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/revoke` ve yetkili `GET /api/auth/me` uçlarıdır. Raw refresh token yalnız istemciye döner; veritabanında yalnız SHA-256 özeti saklanır. Web BFF uygulanmıştır: browser business API isteklerini doğrudan göndermez; access/refresh token yalnız server-side session sınırında tutulur ve browser `localStorage` veya `sessionStorage` alanlarına yazılmaz.

Auth uçları IP başına dakikada 5, genel API ise authenticated kullanıcı veya IP başına dakikada 100 istekle sınırlıdır. Hatalar `code`, `traceId` ve validation için `errors` içeren ProblemDetails döndürür. Development Swagger arayüzünde Bearer access token ile Authorize kullanılabilir; örnekler `docs/api/auth.http` dosyasındadır.

## EF Core migration yönetimi

EF aracı repository-local manifest ile 8.0 sürümüne sabitlenmiştir:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext

dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext
```

Yeni migration ve design-time database update işlemleri de aynı Infrastructure factory standardını kullanır:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext `
  --output-dir Persistence\Migrations

dotnet tool run dotnet-ef database update `
  --project .\src\CorporateRequestManagement.Infrastructure `
  --startup-project .\src\CorporateRequestManagement.Infrastructure `
  --context ApplicationDbContext
```

Bu komutlar yalnız design-time migration yönetimi içindir. Runtime Development migration ve seed akışı aşağıdaki explicit API initializer komutuyla ayrı tutulur.

Development veritabanını migration ile oluşturup idempotent demo verisini eklemek için:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\CorporateRequestManagement.Api -- --initialize-database
```

Normal API başlangıcı migration veya seed çalıştırmaz. `--initialize-database` komutu Development dışındaki ortamlarda reddedilir. Bağlantı dizesi Development ayarında güvenli bir LocalDB varsayılanı içerir; diğer ortamlar `ConnectionStrings__DefaultConnection` sağlamalıdır.

> **YALNIZCA DEVELOPMENT:** Aşağıdaki deterministic hesaplar yerel geliştirme ve değerlendirme içindir. Production, paylaşılan test veya internete açık bir ortamda kullanılmamalı; production başlangıcı bu hesapları seed etmemelidir.

Demo kullanıcıları:

| Rol | E-posta | Parola |
|---|---|---|
| Admin | `admin@company.com` | `CrmDemo!2026` |
| Manager | `manager@company.com` | `CrmDemo!2026` |
| Employee | `employee@company.com` | `CrmDemo!2026` |

Parolalar veritabanında ASP.NET Core `PasswordHasher<User>` çıktısı olarak tutulur; düz metin parola saklanmaz. Bu kimlik bilgileri yalnızca Development içindir.

Seeder deterministic `Yazılım` ve `Finans` departmanlarını oluşturur. Manager ve Employee `Yazılım` departmanına bağlıdır; `Department.ManagerId` ilişkisi tutarlıdır. Seeder idempotent ve transactionaldır; Production ortamında otomatik çalışmaz.

## Uygulamaları çalıştırma

```powershell
dotnet run --project .\src\CorporateRequestManagement.Api --urls https://localhost:7001
dotnet run --project .\src\CorporateRequestManagement.Web --urls https://localhost:7002
```

API root adresinin 404 dönmesi normaldir; işlevler Swagger ve belgelenmiş `/api/*` rotaları üzerinden sunulur.

## Web authentication ve Metronic

Web uygulaması browser-for-frontend yaklaşımı kullanır: tarayıcı Authentication API'yi doğrudan çağırmaz. Access ve refresh token değerleri HTML, JavaScript, cookie ticket, `localStorage` veya `sessionStorage` içine yazılmaz; yalnızca Web sunucusundaki ASP.NET Core session token store içinde tutulur. Tarayıcıda güvenli, HttpOnly ve SameSite=Lax olan `__Host-Crm.Web.Auth` kimlik cookie'si ile opaque `__Host-Crm.Web.Session` session cookie'si bulunur. Form POST işlemleri antiforgery korumasındadır.

Web'in API adresi `BackendApi:BaseUrl` ile yapılandırılır. Development için API ve Web ayrı terminallerde şu sırayla başlatılabilir:

```powershell
dotnet run --project .\src\CorporateRequestManagement.Api --urls https://localhost:7001
dotnet run --project .\src\CorporateRequestManagement.Web --urls https://localhost:7002
```

Rotalar: `GET/POST /account/login`, yalnız POST olan `/account/logout` ve yetkili `/` dashboard. Dashboard isteğinde API 401 dönerse aynı opaque session kimliği için tek refresh çalıştırılır, dönen rotation tokenları atomik olarak session'a yazılır ve istek yalnız bir kez yinelenir.

Focused Web API client'ları `BackendApi:BaseUrl` üzerinden merkezi gateway'i kullanır. API bağlantısı kurulamaz veya response erken kesilirse güvenli 503; `HttpClient` timeout oluşursa güvenli 504 kullanıcı mesajına çevrilir. Caller cancellation maskelenmez. Ham exception, hostname, socket/TLS ayrıntısı, token veya request body kullanıcıya yansıtılmaz; geçici hata sonraki isteğin aynı Web process'i içinde yeniden denenmesini engellemez.

Bu case implementasyonu tek node üzerinde memory-backed session kullanır. Çoklu instance production dağıtımında ortak bir distributed session store ve paylaşılan data-protection key ring gerekir.

Metronic kaynak paketi `.vendor/metronic` altında local-only tutulur. Repository'ye yalnız corporate login ve uygulama shell'i için gereken derlenmiş bundle, font ve görsel alt kümesi kopyalanır; demo sayfaları, kaynak arşivleri, `node_modules`, dokümantasyon ve lisans dosyaları kopyalanmaz. Metronic kaynak/sürüm ve lisans sahipliği proje tesliminde lisans sahibi tarafından doğrulanmalıdır.

## Employee request self-service API

Yetkili kullanıcılar `POST /api/requests`, `GET /api/requests`, `GET /api/requests/{id}`, `PUT /api/requests/{id}`, `POST /api/requests/{id}/submit` ve `POST /api/requests/{id}/cancel` uçlarıyla kendi taleplerini yönetir. Manager’ın kendi talebine ilişkin domain kuralı bulunduğundan sözleşme yeni bir Employee-only rol varsaymaz; authentication ve own-resource ownership zorunludur. Başka kullanıcıya ait veya bulunmayan talep aynı güvenli `404 request.not_found` sonucunu üretir.

`GET /api/requests` üzerindeki optional `searchTerm`, kullanıcının kendi aktif taleplerinde başlık ve açıklamayı SQL Server tarafında arar. Trimlenmiş maksimum uzunluk 100 karakterdir; arama status/type/date filtreleri, deterministic sorting ve server-side paging ile birlikte çalışır.

`CreatedByUserId`, `DepartmentId` ve `Status` istemci body’sinden alınmaz. Owner authenticated kullanıcıdan, departman ise kullanıcının güncel departmanından türetilir ve talepte snapshot olarak saklanır. Listeleme paging, status/type/date filtreleri, whitelist sort alanları ve kararlı Id tie-breaker kullanır.

Mutation response’ları SQL Server RowVersion değerini 8-byte Base64 string olarak döndürür. Update, submit ve cancel son response’daki RowVersion’ı ister; malformed değer 400, stale değer `409 concurrency.conflict` üretir. Audit action’ları `CorporateRequest.Created`, `CorporateRequest.DraftUpdated`, `CorporateRequest.Submitted` ve `CorporateRequest.Cancelled` olup description, token veya RowVersion içermez. Örnek çağrılar [requests.http](docs/api/requests.http) dosyasındadır.

## Manager ve Admin approval API

Manager approval kapsamı kullanıcının departman alanından değil, authoritative `Department.ManagerId` ilişkisinden belirlenir. Manager queue/detail/decision uçları yalnız ilgili aktif departmanın `PendingManagerApproval` taleplerini kapsar ve manager’ın kendi taleplerini actionable queue dışında bırakır. Başka departmandaki veya bulunmayan talep aynı güvenli 404 sonucunu; self-action `409 approval.self_action_forbidden` sonucunu üretir. Admin, Manager rolünü devralmaz ve manager uçlarını kullanamaz.

Manager approve işleminde amount null veya yapılandırılmış `Approval:Threshold` değerine eşit/altındaysa talep final `Approved`; threshold üstündeyse `PendingAdminApproval` olur. Varsayılan threshold 10.000’dir. Manager reject final `Rejected` üretir. Admin yalnız `PendingAdminApproval` taleplerini global queue üzerinden approve/reject edebilir; iki karar da final durum ve `CompletedDate` üretir. Güvenli varsayılan olarak Admin self-action da yasaktır.

Her karar güncel 8-byte Base64 RowVersion ister. Stale değer `409 concurrency.conflict`, aynı seviyedeki tekrar karar `409 approval.already_processed` üretir. Request mutation, tekil `(RequestId, ApprovalLevel)` RequestApproval kaydı ve redakte audit kaydı tek `SaveChanges` atomiklik sınırındadır. Audit payload’ı rejection reason, token veya RowVersion taşımaz.

Approval history owner, authoritative department manager ve Admin tarafından görülebilir; diğer aktörlerde kaynak varlığını sızdırmayan 404 döner. Manager/Admin Metronic UI akışı bu HTTP sözleşmesini server-side focused client'lar üzerinden kullanır. Örnekler [approvals.http](docs/api/approvals.http) dosyasındadır.
## Premium talep ve onay arayüzü

Web uygulaması server-rendered MVC/BFF akışını korur: browser yalnız Web uygulamasına form gönderir; Requests ve Approvals API çağrıları server-side odaklı HTTP client’lar üzerinden yapılır. Access/refresh token HTML, JavaScript, cookie ticket, `localStorage` veya `sessionStorage` içine yazılmaz. Mevcut opaque session token store ve tek-sefer refresh/retry düzeni kullanılır.

UI rotaları `/Requests`, `/Requests/Create`, `/Requests/{id}`, `/Requests/{id}/Edit`, `/Approvals/Manager` ve `/Approvals/Admin` şeklindedir. Tüm mutation’lar antiforgery korumalı POST ve başarılı sonuçlarda PRG uygular. Exact-role navigasyon kullanılır; API authorization authoritative kalır.

RowVersion yalnız hidden business alanı olarak taşınır. Concurrency conflict otomatik overwrite edilmez; kullanıcı güncel kaydı yeniden yüklemeye yönlendirilir. API hataları raw içerik göstermeyen Türkçe deneyimlere çevrilir. Desktop tablolar mobilde yatay scroll oluşturmayan kartlara dönüşür.

Görünür ürün kimliği “Kurumsal Talep Yönetimi”dir. Metronic yalnız kontrollü UI altyapısı olarak kullanılır; görünür template markası veya demo içeriği yoktur. Ayrıntılar [Phase 6 UI Guide](docs/ui/phase6-ui-guide.md) içindedir.
### Rol bazlı analytics dashboard

Employee dashboard yalnız kullanıcının kendi taleplerini; Manager dashboard authoritative `Department.ManagerId` kapsamındaki gelen talepleri ve yöneticinin kendi kararlarını; Admin dashboard ise güvenli sistem aggregate'lerini gösterir. Bugün, bu ay ve bu yıl sınırları UTC ve `[başlangıç, sonraki başlangıç)` semantiğiyle hesaplanır. Son 30 günlük talep grafiği SQL Server günlük aggregate sorgularından gelir; sahte KPI veya rastgele grafik verisi kullanılmaz.

Akış Browser → Web BFF → role-specific `/api/dashboard/*` → MediatR → dashboard read repository → SQL Server şeklindedir. Manager/Admin onay kuyrukları server-side `searchTerm` aramasını ve Taleplerim ile ortak toolbar, collapsible filtre ve 10/20/50/100 page-size pagination standardını kullanır.

## Phase 7 yönetim, profil ve teslim kapsamı

Exact Admin rotaları kullanıcı ve departman CRUD, açık manager assignment, bütün aktif talepler, RowVersion korumalı request soft delete ve redakte audit list/detail yeteneklerini sunar. Manager’ın `/api/manager/requests` görünümü approval queue değildir; yalnız authoritative `Department.ManagerId` ilişkisindeki departman çalışanlarının bütün durumdaki aktif taleplerini gösterir. Admin ve Manager listeleri controlled search, status/type/date, department (Admin), min/max amount, allow-list sorting ve server-side paging kullanır.

Güncel yönetim sözleşmesi `/api/admin/users` list/detail/create/update/reset-password/delete; `/api/admin/departments` list/detail/create/update/eligible-managers/manager/create-and-assign/delete; `/api/admin/requests` list/detail/delete ve `/api/audit-logs` list/detail/actors uçlarını kapsar. Actor lookup, audit filtresinde ad-soyad/e-posta seçimi için kullanılır. Bütün Web listeleri filtreleri koruyan ortak 10/20/50/100 global pagination bileşenini kullanır.

Kullanıcı, departman ve talep silme fiziksel değildir. Kullanıcı silme aktif tokenları revoke eder; current Admin self-delete/self-disable/self-demotion engellenir. Aktif departman yöneticisi rol/departman/aktiflik değişikliğinden veya silmeden önce manager assignment’tan çıkarılmalıdır. Departman, aktif kullanıcı veya aktif talep taşıyorsa silinmez; güvenli silmede manager ilişkisi aynı transaction içinde temizlenir. Historical request/approval/audit kayıtları korunur.

`GET /api/account/profile` ve `POST /api/account/change-password` bütün authenticated roller içindir. Parola değişimi mevcut parolayı doğrular, güçlü ve farklı yeni parola ister, bütün refresh tokenları revoke eder ve yalnız `User.PasswordChanged` metadata audit’i yazar. Web başarıdan sonra local token/session/cookie durumunu temizleyip login’e döner. Admin reset endpoint’i kendi hesabında kullanılamaz; self-service Profile akışı tercih edilir.

Web rotaları `/Admin/Users`, `/Admin/Departments`, `/Admin/Requests`, `/Admin/AuditLogs`, `/Manager/Requests`, `/Profile` ve `/Profile/ChangePassword` şeklindedir. Browser API’yi doğrudan çağırmaz; mutation’lar server-side focused client ve antiforgery POST üzerinden yürür.

API örnekleri `docs/api/` altındadır. Veri modeli için [ER diyagramı](docs/er-diagram.md), runtime/dependency kararları için [mimari belge](docs/architecture.md), resmî gereksinim kanıtları için [case compliance matrix](docs/case-compliance-matrix.md) kullanılır.

`Phase7CleanDatabaseE2ETests.Clean_database_should_migrate_seed_and_complete_single_end_to_end_management_workflow`, yalnız `CorporateRequestManagement_IntegrationTests_<32-hex-guid>` guard’ını geçen ayrı SQL Server veritabanında sıfırdan migration, iki kez idempotent seed, üç rol login, request create/submit, Manager/Admin approval, user/department management, soft delete, audit query ve fiziksel history korumasını tek akışta doğrular. Development ana veritabanı kullanılmaz veya silinmez.

Resmî kaynak karşılaştırması authoritative `Essa_Teknoloji_Junior_DotNet_Backend_Case_Aday.pdf` ve onun makine tarafından okunabilir `.project/source/ESSA_Official_Case_Text.md` aktarımı üzerinden yapılmıştır. Canonical current-state kanıtları `docs/case-compliance-matrix.md` içindedir.

Bilinen manuel doğrulama sınırı: gerçek cihaz/browser üzerinde 1440×900, 768×1024, 430×932 ve 375×812 responsive görsel smoke bu otomatik test turunda çalıştırılmamıştır. Backend/API/SQL güvenlik ve davranış kapıları otomatik test kapsamındadır.
