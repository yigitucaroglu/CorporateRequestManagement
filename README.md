# Kurumsal Talep ve Onay Yönetim Sistemi

Bu repository, çalışanların kurumsal talepler oluşturduğu ve yönetici/yönetim onay akışlarından geçirdiği .NET 8 teknik değerlendirme uygulamasını içerir. Phase 0 kapsamında yalnızca solution iskeleti ve repository mühendislik standartları kurulmuştur.

## Mimari

Backend, Clean Architecture sınırlarını izler:

```text
Domain <- Application <- Infrastructure <- Api
```

Gerçek proje referansları daha dar tutulur: Domain bağımsızdır; Application yalnızca Domain'e; Infrastructure, Application ve Domain'e; Api ise Application ve Infrastructure'a referans verir. Ayrı MVC `Web` projesinin backend projelerine referansı yoktur ve ilerleyen dilimlerde API ile yalnızca HTTP üzerinden iletişim kuracaktır.

Geliştirme, foundation-first vertical slice yaklaşımıyla yürütülür. Temellerden sonra her özellik iş kuralları, uygulama akışı, persistence, API, güvenlik, test, manuel API doğrulaması ve ilgili Web akışıyla birlikte tamamlanır.

## Gereksinimler

- .NET 8 SDK

Kurulu SDK'yı doğrulamak için:

```powershell
dotnet --info
```

## Restore, build ve test

Repository kökünde:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## API'yi çalıştırma

```powershell
dotnet run --project src/CorporateRequestManagement.Api --urls http://localhost:5100
```

Phase 0 API kabuğu henüz bir özellik endpoint'i sunmaz.

## Web uygulamasını çalıştırma

```powershell
dotnet run --project src/CorporateRequestManagement.Web --urls http://localhost:5200
```

Tarayıcıdan `http://localhost:5200` adresine gidilebilir.

## Phase 0 sınırı

Bu fazda entity, authentication, repository, DbContext, migration, iş kuralı, özellik endpoint'i, typed API client veya özellik ekranı eklenmemiştir. Metronic entegrasyonu ve özellik geliştirmesi, ilgili API sözleşmeleri kararlı hale geldikten sonraki dikey dilimlerde başlayacaktır.
