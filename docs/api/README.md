# API örnekleri

Bu klasördeki `.http` dosyaları authentication, profil, dashboard, çalışan
talepleri, Manager/Admin onayları, kullanıcı ve departman yönetimi ile audit
uçlarını elle doğrulamak için hazırlanmıştır.

Development ortamında interaktif Swagger UI
`https://localhost:7001/swagger`, OpenAPI belgesi ise
`https://localhost:7001/swagger/v1/swagger.json` adresindedir. Swagger ve bu
örnekler Development içindir; Production ortamında Swagger middleware'i
etkin değildir.

Dosyalar VS Code REST Client veya JetBrains HTTP Client ile çalıştırılabilir.
Önerilen sıra:

1. `auth.http` ile login yapın.
2. Dönen access tokenı ilgili dosyadaki yalnız yerel `@token` placeholder'ına
   yapıştırın; gerçek tokenı kaydetmeyin veya commit etmeyin.
3. `profile.http`, `dashboard.http` ve `requests.http` ile self-service akışını
   deneyin.
4. `manager-requests.http` ve `approvals.http` ile onay akışını doğrulayın.
5. Admin hesabıyla `admin-users.http`, `admin-departments.http`,
   `admin-requests.http` ve `audit-logs.http` örneklerini çalıştırın.
6. Development seed'in temel erişimini kontrol etmek için `seed-smoke.http`
   kullanın.

Örneklerdeki kimlik, row-version ve token placeholder'ları önceki response
değerleriyle değiştirilmelidir. Demo parolaları yalnız Development seed
hesapları içindir; gerçek credential veya secret değerlerini `.http`
dosyalarına yazmayın.
