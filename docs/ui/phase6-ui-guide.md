# Phase 6 UI Guide

## Tasarım ilkeleri

Arayüz sakin, ferah ve iş akışı odaklı kurumsal SaaS görünümündedir. Beyaz surface, sıcak açık gri arka plan, slate metin ve sınırlı indigo/yeşil/amber/kırmızı semantik palet kullanılır. Radius değerleri 10/14/18 px; animasyonlar en fazla 180 ms’dir. `prefers-reduced-motion` desteklenir ve tema fixed light’tır.

## Marka

Ürün adı “Kurumsal Talep Yönetimi”, kısa navigasyon adı “Kurumsal Talep”tir. Marka işareti CSS geometrisidir; favicon özgün SVG’dir. Görünür Metronic, KeenThemes veya demo kimliği kullanılmaz.

## Sayfalar ve rol navigasyonu

- Tüm roller: Ana Sayfa, Taleplerim, Yeni Talep
- Manager: Yönetici Onayları
- Admin: Yönetim Onayları
- Employee: approval menüsü yok

Sayfalar login, dashboard, request list/create/edit/detail/history, Manager queue/detail/decision, Admin queue/detail/decision ve güvenli 403/404/5xx durumlarını kapsar.

## Durum, form ve hata davranışı

Durum, request type, approval level/status ve rol metinleri merkezi `UiDisplay` mapper ile Türkçeleştirilir. Bilinmeyen değer “Bilinmiyor” gösterir. Mutation’lar antiforgery POST + PRG kullanır. Concurrency durumunda otomatik retry/overwrite yapılmaz. Raw ProblemDetails, SQL metni ve stack trace render edilmez.

Amount alanı nullable decimal semantiğini koruyan, culture-independent pozitif değer doğrulaması kullanır. `RangeAttribute` string sınırları kullanılmaz; böylece tr-TR Create/Edit render aşamasında client-validation metadata hatası oluşmaz. Input `inputmode="decimal"` ve `step="0.01"` taşır; API doğrulaması authoritative kalır.

Employee list filtreleri ilk yüklemede kapalı, aktif filtre varken açık başlayan erişilebilir panel içindedir. Başlık ve açıklama araması server-rendered GET formuyla API'nin `searchTerm` sözleşmesini kullanır; browser doğrudan API çağrısı yapmaz. Arama, filtre, sıralama ve paging query değerleri birlikte korunur. Desktop semantic table kontrollü `colgroup`, sağ hizalı tabular amount ve tek İşlemler menüsü kullanır; mobilde kart düzenine dönüşür.

## Responsive ve erişilebilirlik

Desktop sidebar mobilde overlay drawer’a dönüşür. Tablolar 991 px altında kart düzenidir; form ve modal tek sütuna iner. Touch target’lar yaklaşık 44 px, focus görünür, input label’ları bağlı, modal keyboard/ESC uyumlu ve feedback `aria-live` alanındadır. Uzun kullanıcı metni wrap edilir ve Razor encoding korunur.

Create ve Edit ekranları aynı 1120 px maksimum genişlikli, yatay ortalanmış form container'ını kullanır; heading ve premium form kartı aynı eksendedir. Taleplerim ekranında search formu geniş toolbar'ın sol grubunda, Filtreler ve Yeni Talep sağ grubundadır. Tablet kırılımında kontrollü wrap, mobilde tam genişlik search ve iki kolonlu aksiyon satırı kullanılır.

## Manuel smoke checklist

- 375×812, 430×932, 768×1024 ve 1440×900 görünüm
- Login, yanlış parola ve başarılı giriş
- Employee list/filter/create/edit/detail/submit/cancel/history
- Manager ve Admin queue/detail/approve/reject/history
- Mobile drawer, modal, keyboard ve %200 zoom
- CSS/JS/font/favicon 200; beklenmeyen 404 yok
- HTML/response/storage/cookie ticket içinde token yok
- Yatay scroll ve görünür template markası yok
## Liste navigasyonu ve UI düzeltmeleri

- Taleplerim, Yönetici Onayları ve Yönetim Onayları listeleri API'nin 1-based `pageNumber` / `pageSize` sözleşmesini kullanır.
- Ortak liste footer'ı 10, 20, 50 ve 100 kayıt seçeneklerini, önceki/sonraki bağlantılarını ve erişilebilir sayfa penceresini sunar. Page-size değişimi ilk sayfaya döner; mevcut filtre ve sıralama değerleri korunur.
- Talep başlıkları masaüstü tablo ve mobil kart görünümünde güvenli MVC detay bağlantılarıdır.
- Taleplerim filtre aksiyonları aynı footer satırında secondary `Temizle` ve primary `Filtrele` butonları olarak düzenlenmiştir.
- Dashboard primary aksiyonunun normal, visited, hover, active ve focus durumlarındaki kontrastı app-owned CSS ile sabitlenmiştir.
- Sidebar `Yeni Talep` öğesi dekoratif, `currentColor` kullanan app-owned document-add SVG ikonuna sahiptir.
## Role-based analytics dashboard standardı

- Dashboard 1400px dengeli container, dört KPI kartı, üç segmentli dönem özeti, 2/3 trend + 1/3 dağılım grid'i kullanır.
- Employee içerikleri owner scoped, Manager içerikleri departman/kişisel karar scoped, Admin içerikleri aggregate-only'dir.
- Manager “Son Taleplerim” ve “Bekleyen Onaylar” kartları aynı `1fr 1fr` grid içinde eşit yüksekliktedir.
- Grafikler app-owned server-rendered SVG/CSS bileşenleridir; CDN veya yeni frontend pipeline yoktur. Empty state, metin özeti, semantic renk ve reduced-motion desteği vardır.
- Bugün/bu ay/bu yıl UTC sınırlarını, talep grafiği bugün dahil son 30 günü kullanır.

## Ortak liste standardı

Taleplerim, Yönetici Onayları ve Yönetim Onayları aynı search toolbar, collapsible GET filter paneli, tablo/card surface ve `_Pagination` partial'ını kullanır. Search ve filtre değerleri paging/page-size linklerinde korunur; page-size 10/20/50/100 seçenekleridir ve değişince sayfa 1'e döner. Mobilde search tam genişlik, aksiyonlar alt satır ve tablolar card davranışındadır.
