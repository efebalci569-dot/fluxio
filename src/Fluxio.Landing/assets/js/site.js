// Fluxio website: mobile menu, GitHub links and the English/Turkish switch.
// English is the page source (for search engines); Turkish replaces marked elements.
(function () {
  'use strict';

  var TR = {
    'skip': 'İçeriğe geç',
    'nav.features': 'Özellikler',
    'nav.how': 'Nasıl Çalışır',
    'nav.security': 'Güvenlik',
    'nav.download': 'İndir',
    'nav.github': 'GitHub',
    'nav.cta': 'İndir',

    'hero.eyebrow': 'Ücretsiz ve açık kaynak · Windows 10/11',
    'hero.title': 'Dosyaların. Cihazların. <span class="grad">Tek bağlantı.</span>',
    'hero.lead': 'Bilgisayarın ile telefonun arasında basit bir QR kodla dosya aktar.',
    'hero.download': 'Windows için indir',
    'hero.how': 'Nasıl çalıştığını gör',
    'hero.note1': 'Hesap yok',
    'hero.note2': 'Buluta yükleme yok',
    'hero.note3': 'Telefona uygulama gerekmez',

    'features.kicker': 'Özellikler',
    'features.title': 'Dosya taşımak için gereken her şey. Fazlası değil.',
    'features.lead': 'Fluxio Windows bilgisayarında çalışır ve telefonunun tarayıcısında açılır. Dosyalar yerel ağında doğrudan iki cihaz arasında taşınır.',
    'f1.t': 'QR Kod ile Eşleştirme',
    'f1.d': 'Kodu telefonunun kamerasıyla tara. Her kod tek kullanımlıktır ve birkaç dakika sonra geçersiz olur.',
    'f2.t': 'İki Yönlü Aktarım',
    'f2.d': 'Bilgisayardan telefona veya telefondan bilgisayara — aynı bağlantıda.',
    'f3.t': 'Yerel Ağ Üzerinden',
    'f3.d': 'Dosyalar doğrudan Wi-Fi veya yerel ağın üzerinden gider. Hiçbir bulut sunucusuna yüklenmez.',
    'f4.t': 'Sürükle ve Bırak',
    'f4.d': 'Dosyaları veya klasörleri Fluxio\'ya bırak, listeyi gözden geçir, istemediklerini çıkar.',
    'f5.t': 'Büyük Dosya Desteği',
    'f5.d': 'Dosyalar parça parça aktarılır, belleğe tamamen yüklenmez; yarıda kalan yüklemeler kaldığı yerden devam eder.',
    'f6.t': 'Aktarım İlerlemesi',
    'f6.d': 'Dosya bazında ve toplam ilerleme, gerçek ölçülen hız ve kalan süre — iki ekranda da.',
    'f7.t': 'Gizlilik Odaklı Tasarım',
    'f7.d': 'Her telefonu ve gelen her dosya grubunu sen onaylarsın. Her dosya SHA-256 ile doğrulanır. Telemetri yok.',
    'f8.t': 'Zorunlu Hesap Yok',
    'f8.d': 'Kayıt yok, hesap hiç yok. Kur, aç, tara — bu kadar.',

    'how.kicker': 'Nasıl çalışır',
    'how.title': 'Üç adım. Kablo yok.',
    'how.lead': 'Bilgisayarın ve telefonun aynı Wi-Fi veya yerel ağda olması yeterli.',
    's1.t': 'Bilgisayarında Fluxio\'yu aç.',
    's1.d': '<b>Dosya Gönder</b> veya <b>Dosya Al</b>\'ı seç. Fluxio kısa ömürlü bir oturum başlatır ve QR kod gösterir.',
    's2.t': 'QR kodu telefonunla tara.',
    's2.d': 'Kamera, Fluxio sayfasını tarayıcıda açar. Telefona izin vermek için 4 haneli kodu bilgisayarında onayla.',
    's3.t': 'Dosyaları seç ve aktar.',
    's3.d': 'Dosyaları istediğin cihazda seç. Diğer taraf kabul eder, dosyalar aktarılır ve iki taraf da doğrular.',
    'flow.pc': 'Bilgisayarın',
    'flow.lan': 'Yerel ağın',
    'flow.phone': 'Telefonun',

    'sec.kicker': 'Güvenlik',
    'sec.title': 'Yalnızca senin telefonun girebilsin diye tasarlandı.',
    'sec.lead': 'Fluxio ağındaki her cihaza güvenmez. Erişim kısa sürelidir, açıkça onaylanır ve yalnızca paylaşmayı seçtiklerinle sınırlıdır.',
    'sec.more': 'Güvenlik modelini oku',
    'c1.t': 'Tek kullanımlık, süreli QR kodlar',
    'c1.d': '256 bit rastgele gizli anahtar, tek telefon tarafından kullanılabilir, dakikalarca geçerli. Yeni kod eskisini geçersiz kılar.',
    'c2.t': 'Telefonu sen onaylarsın',
    'c2.d': 'Bir şey aktarılmadan önce iki ekranda da aynı kod görünür.',
    'c3.t': 'Gelen dosyaları onayla',
    'c3.d': 'Sen kabul edene kadar hiçbir şey yazılmaz. Mevcut dosyaların üzerine sormadan asla yazılmaz.',
    'c4.t': 'SHA-256 doğrulaması',
    'c4.d': 'Aktarım ancak iki taraf da sağlama toplamında uzlaşınca tamamlanmış sayılır.',
    'c5.t': 'Yalnızca yerel ağ, yalnızca gerektiğinde',
    'c5.d': 'Servis tek bir yerel adreste ve yalnızca oturum sırasında dinler. Dosyalar asla açılmaz veya çalıştırılmaz.',
    'sec.honestT': 'Bilmende fayda var',
    'sec.honestD': 'Fluxio ağının içinde düz HTTP kullanır (havadaki trafiği Wi-Fi şifrelemen korur), çünkü telefon tarayıcıları yerel bir sertifikaya uyarı göstermeden güvenemez. Fluxio\'yu güvendiğin ağlarda kullan.',

    'req.kicker': 'Gereksinimler',
    'req.title': 'Neye ihtiyacın var?',
    'req.pcT': 'Bilgisayar',
    'req.pcD': 'Windows 11 veya Windows 10 (64 bit). Ayrıca .NET kurmana gerek yok.',
    'req.phoneT': 'Telefon',
    'req.phoneD': 'QR kod okuyabilen kameralı ve güncel tarayıcılı (Chrome, Safari, Samsung Internet, Firefox) herhangi bir Android veya iPhone.',
    'req.netT': 'Ağ',
    'req.netD': 'İki cihaz aynı Wi-Fi veya yerel ağda. “İstemci yalıtımı” açık misafir ağları cihazlar arası trafiği engeller.',

    'faq.title': 'Sorular',
    'q1': 'Telefonuma uygulama kurmam gerekiyor mu?',
    'a1': 'Hayır. QR kod, Fluxio sayfasını telefonunun tarayıcısında açar.',
    'q2': 'İnternet üzerinden çalışıyor mu?',
    'a2': 'Bu sürümde hayır. İki cihaz aynı yerel ağda olmalı. Farklı ağlar arası aktarım ileriki bir sürüm için planlanıyor ve sunulmadan önce nasıl çalıştığı açıkça anlatılacak.',
    'q3': 'Dosyalar telefonumda nereye kaydediliyor?',
    'a3': 'Buna telefonunun tarayıcısı karar verir — genellikle İndirilenler klasörü, iPhone\'da Dosyalar › İndirilenler. Bir web sayfası klasör seçemez.',
    'q4': 'Boyut sınırı var mı?',
    'a4': 'Dosya başına 100 GB ve aktarım başına 500 dosyaya kadar. Telefona çok büyük indirmeler (1 GB üzeri, iPhone\'da 512 MB) tarayıcının kendi indirme yöneticisini kullanır ve Fluxio bunları telefonda doğrulayamaz.',
    'q5': 'Kurulum dosyasını çalıştırınca Windows neden uyarıyor?',
    'a5': 'İlk sürümler henüz kod imzalı değil; SmartScreen uyarı gösterebilir. Yalnızca resmi GitHub sürümlerinden indir ve indirme sayfasındaki SHA-256 sağlama toplamını karşılaştır.',
    'q6': 'Fluxio ücretsiz mi?',
    'a6': 'Evet. Fluxio MIT lisansıyla açık kaynaktır.',
    'cta.title': 'Dosyaları zahmetsizce taşı.',
    'cta.lead': 'Windows için Fluxio\'yu al, telefonunu saniyeler içinde bağla.',

    'footer.tag': 'Dosyaların. Cihazların. Tek bağlantı.',
    'footer.product': 'Ürün',
    'footer.changes': 'Sürüm notları',
    'footer.trust': 'Güven',
    'footer.privacy': 'Gizlilik',
    'footer.source': 'Açık kaynak',
    'footer.license': 'MIT Lisansı',
    'footer.noTrack': 'Bu web sitesi çerez ve analiz aracı kullanmaz.',

    'dl.title': 'Windows için Fluxio\'yu indir',
    'dl.lead': 'Ücretsiz ve açık kaynak. Windows 11 ve Windows 10, 64 bit. Başka bir şey kurmana gerek yok.',
    'dl.loading': 'GitHub\'daki son sürüm kontrol ediliyor…',
    'dl.noneTitle': 'Henüz yayımlanmış bir sürüm yok',
    'dl.noneBody': 'Fluxio geliştirme aşamasında ve ilk derleme henüz GitHub Releases\'ta yayımlanmadı. Bugün kaynak koddan derleyebilir (aşağıya bak) ya da daha sonra tekrar bakabilirsin.',
    'dl.viewSource': 'Kaynak kodu GitHub\'da gör',
    'dl.errorTitle': 'Sürüm bilgisi alınamadı',
    'dl.errorBody': 'GitHub\'a ulaşılamadı (veya istek sınırına takıldı). Sürümler sayfasını doğrudan aç:',
    'dl.openReleases': 'GitHub Releases\'ı aç',
    'dl.latest': 'Son sürüm',
    'dl.date': 'Yayın tarihi',
    'dl.windows': 'Desteklenen Windows',
    'dl.windowsValue': 'Windows 11 · Windows 10 (64 bit)',
    'dl.checksumNote': 'Her sürümle birlikte SHA-256 sağlama toplamları yayımlanır (SHA256SUMS.txt). Dosyayı çalıştırmadan önce karşılaştır.',
    'dl.notes': 'Sürüm notları',
    'dl.installTitle': 'Kurulum',
    'dl.install1': '<b>Kurulum dosyası</b> (FluxioSetup-x.y.z.exe): kendi kullanıcın veya tüm kullanıcılar için kurar, Başlat menüsüne ekler ve istersen özel ağlar için Windows Güvenlik Duvarı kuralı ekler.',
    'dl.install2': '<b>Taşınabilir</b> (Fluxio-x.y.z-win-x64.zip): zip\'i aç ve Fluxio.exe\'yi çalıştır. Windows, Fluxio\'nun ağı kullanıp kullanamayacağını bir kez sorar — özel ağlarda izin ver.',
    'dl.install3': 'Fluxio <b>henüz kod imzalı değil</b>. SmartScreen “Windows bilgisayarınızı korudu” uyarısı gösterebilir. SHA-256 sağlama toplamını doğrula, sonra “Ek bilgi” › “Yine de çalıştır”ı seç.',
    'dl.install4': 'Fluxio çalışmak için asla yönetici izni gerektirmez.',
    'dl.phoneTitle': 'Telefonunda',
    'dl.phoneBody': 'Kurulacak bir şey yok. Fluxio\'nun gösterdiği QR kodu kamera uygulamasıyla tara.',
    'dl.buildTitle': 'Kaynak koddan derle',
    'dl.buildBody': '.NET 10 SDK ve Node.js 20+ gerekir. Depoyu klonla ve <code>scripts/build.ps1</code>\'i çalıştır; ayrıntılar <code>docs/development.md</code> içinde.',

    'secp.title': 'Güvenlik ve gizlilik',
    'secp.lead': 'Fluxio ağındaki her cihazın dost olduğunu varsaymaz. Bu sayfa dosyalarını tam olarak neyin koruduğunu — ve neyin korumadığını — anlatır.',
    'secp.h1': 'Eşleştirme',
    'secp.p1': 'Her oturumun, işletim sisteminin kriptografik rastgele üretecinden gelen 128 bit rastgele bir kimliği ve 256 bit bir gizli anahtarı vardır. İkisi de yalnızca QR kodda bulunur (<code>#</code> sonrasındaki kısımda; tarayıcılar bu kısmı sunucuya göndermez ve yönlendiren bilgisine eklemez).',
    'secp.p2': 'Bir kod birkaç dakika (2–15, ayarlanabilir) ve yalnızca <strong>bir</strong> telefon için geçerlidir. Yeni kod oluşturmak önceki oturumu sonlandırır, gizli anahtarını ve belirteçlerini geçersiz kılar.',
    'secp.p3': 'Taramadan sonra telefon kendine ait rastgele bir erişim belirteci alır. Ardından telefonu bilgisayarında onaylarsın; doğru cihazı onayladığından emin olman için iki ekranda da aynı 4 haneli kod görünür. Onaylanana kadar telefon hiçbir şey aktaramaz.',
    'secp.p4': 'Eşleştirme denemeleri sınırlandırılır ve gizli anahtarlar sabit sürede karşılaştırılır.',
    'secp.h2': 'Yerel servis',
    'secp.s1': 'Fluxio\'nun web servisi <strong>yalnızca bir oturum açıkken</strong> çalışır ve <strong>tek</strong> bir özel yerel adreste dinler — asla tüm arayüzlerde değil. Oturum bitince veya Fluxio kapanınca durur.',
    'secp.s2': 'Yerel olmayan (genel) adreslerden gelen istekler reddedilir. <code>Host</code> başlığı oturum adresiyle eşleşmelidir; bu, DNS yeniden bağlama saldırılarını engeller.',
    'secp.s3': 'Erişim belirteci çerezlerde değil <code>Authorization</code> başlığında taşınır; böylece başka web siteleri senin adına istek yapamaz (CSRF yok). Siteler arası ve farklı kökenli istekler reddedilir.',
    'secp.s4': 'Sıkı güvenlik başlıkları: satır içi betik içermeyen İçerik Güvenliği Politikası, çerçeveleme yok, yönlendiren bilgisi yok, içerik tahmini yok. İstek boyutları ve istek hızları sınırlıdır.',
    'secp.h3': 'Dosyalar',
    'secp.f1': 'Gelen dosyalar senin onayını gerektirir (bunu kapatmadıkça). Kabul etmeden önce hiçbir şey yazılmaz.',
    'secp.f2': 'Telefondan gelen dosya adları temizlenir: klasör kısımları, <code>..</code>, ayrılmış Windows adları, kontrol karakterleri ve sağdan sola hileleri kaldırılır; böylece bir dosya seçilen klasörün dışına asla yazılamaz.',
    'secp.f3': 'Veri gizli bir geçici dosyaya aktarılır ve SHA-256 sağlama toplamı eşleştikten sonra gerçek adına taşınır. Yarıda kalan veya iptal edilen aktarımlar geride yarım dosya bırakmaz.',
    'secp.f4': '“Üzerine yaz”ı seçmedikçe mevcut dosyaların üzerine asla yazılmaz. Sistem ve program klasörleri indirme klasörü olarak kullanılamaz.',
    'secp.f5': 'Fluxio alınan dosyaları asla açmaz veya çalıştırmaz. Alınan dosyalar indirilmiş olarak işaretlenir (Mark of the Web); böylece Windows alınan bir programı çalıştırmadan önce uyarır.',
    'secp.f6': 'Telefon yalnızca göndermeyi seçtiğin dosyaların ad ve boyutlarını görür — bilgisayarındaki yolları veya diğer klasörleri asla görmez.',
    'secp.h4': 'Şifreleme — dürüst kısım',
    'secp.e1': 'Yerel ağının içinde telefon ile bilgisayar arasındaki bağlantı <strong>HTTP</strong> kullanır. Yerel bir IP adresine güvenilir HTTPS bağlantısı, telefona sertifika kurmayı gerektirir; kendinden imzalı sertifikalar güvenlik uyarılarına yol açar ve Fluxio bu uyarıları geçmeni asla istemez.',
    'secp.e2': 'Seni koruyanlar: Wi-Fi şifrelemen (WPA2/WPA3) havadaki trafiği şifreler; oturumlar kısadır ve onaylanmış tek bir telefona bağlıdır; içerik SHA-256 ile doğrulanır. Korumayan: ağını kontrol eden biri (örneğin güvenilmeyen bir ortak Wi-Fi\'da) bir aktarımı izleyebilir veya bozabilir. Windows ağı Ortak olarak işaretlediğinde Fluxio uyarır. <strong>Fluxio\'yu güvendiğin ağlarda kullan.</strong>',
    'secp.h5': 'Windows Güvenlik Duvarı',
    'secp.w1': 'Fluxio ilk kez dinlemeye başladığında Windows izin verilip verilmeyeceğini sorar. <strong>Özel</strong> ağlarda izin ver. Ağ türü sonradan değişirse Fluxio güvenlik duvarının onu engellediğini algılar ve yalnızca özel ağlar, yerel alt ağ ve Fluxio\'nun portlarıyla sınırlı bir kural eklemeyi önerir (yönetici onayın gerekir).',
    'secp.h6': 'Gizlilik',
    'secp.v1': 'Hesap yok, bulut yok, telemetri yok, analiz yok.',
    'secp.v2': 'Yerel geçmiş dosya adı, boyut, tarih, sonuç ve telefonun tarayıcı tanımını saklar — içerikleri veya klasör yollarını asla. İstediğin zaman silebilirsin.',
    'secp.v3': 'Günlükler 7 gün boyunca bilgisayarında kalır ve belirteç, gizli anahtar, dosya içeriği veya yol içermez.',
    'secp.v4': 'Telefonda isteğe bağlı geçmiş yalnızca o tarayıcının yerel depolamasında tutulur.',
    'secp.v5': 'Bu web sitesi çerez ve analiz aracı kullanmaz. İndirme sayfası son sürüm için GitHub\'ın genel API\'sine sorar.',
    'secp.h7': 'Sorun bildirme',
    'secp.r1': 'Güvenlik sorunlarını lütfen herkese açık bir konu yerine depodaki GitHub “Report a vulnerability” özelliğiyle gizli olarak bildir.',

    'log.title': 'Sürüm notları',
    'log.lead': 'Yayımlanmış derlemeler ve dosyaları <a href="#" data-github-path="/releases">GitHub Releases</a> sayfasındadır.',
    'log.unreleased': '— ilk sürüm (henüz yayımlanmadı)',
    'log.i1': 'Tek kullanımlık, süreli kodlarla QR eşleştirme ve eşleşen kodla cihaz onayı.',
    'log.i2': 'Tek oturumda bilgisayar → telefon ve telefon → bilgisayar aktarımı, aynı anda birden çok dosya.',
    'log.i3': 'Telefondan parça parça ve kaldığı yerden devam eden yükleme; devam edebilen akışlı indirme; iki tarafta SHA-256 doğrulaması.',
    'log.i4': 'Gelen dosyaların onaylanması ve ad çakışması yönetimi (ikisini de sakla, üzerine yaz, atla).',
    'log.i5': 'Gerçek hız ve kalan süreli Aktarımlar sayfası, yerel geçmiş, ayarlar, Türkçe ve İngilizce, koyu ve açık tema.',
    'log.i6': 'Windows ağ profili ve Windows Güvenlik Duvarı için ağ teşhisi.',
    'log.known': 'Bilinen kısıtlamalar',
    'log.k1': 'İki cihaz aynı yerel ağda olmalı; internet üzerinden aktarım henüz yok.',
    'log.k2': 'Ağ içindeki bağlantı HTTP kullanır (bkz. Güvenlik).',
    'log.k3': 'Telefona çok büyük indirmeler tarayıcının indirme yöneticisini kullanır ve sayfa tarafından doğrulanmaz.',
    'log.k4': 'Derlemeler henüz kod imzalı değil.',
  };

  var repo = (window.FLUXIO && window.FLUXIO.repo) || '';
  var originals = new Map();

  function setGitHubLinks() {
    if (!repo) return;
    document.querySelectorAll('[data-github]').forEach(function (a) { a.href = 'https://github.com/' + repo; a.rel = 'noopener'; });
    document.querySelectorAll('[data-github-path]').forEach(function (a) { a.href = 'https://github.com/' + repo + a.getAttribute('data-github-path'); a.rel = 'noopener'; });
  }

  function applyLang(lang) {
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      var key = el.getAttribute('data-i18n');
      if (!originals.has(el)) originals.set(el, el.innerHTML);
      // Only static strings from this file are inserted (never user or network data).
      el.innerHTML = lang === 'tr' && TR[key] !== undefined ? TR[key] : originals.get(el);
    });
    document.querySelectorAll('[data-lang-toggle]').forEach(function (btn) {
      btn.textContent = lang === 'tr' ? 'EN' : 'TR';
      btn.setAttribute('aria-label', lang === 'tr' ? 'English' : 'Türkçe');
    });
    setGitHubLinks();
    try { localStorage.setItem('fluxio.site.lang', lang); } catch (e) { /* storage unavailable */ }
  }

  function initialLang() {
    try {
      var saved = localStorage.getItem('fluxio.site.lang');
      if (saved === 'tr' || saved === 'en') return saved;
    } catch (e) { /* ignore */ }
    return (navigator.language || '').toLowerCase().indexOf('tr') === 0 ? 'tr' : 'en';
  }

  document.querySelectorAll('[data-lang-toggle]').forEach(function (btn) {
    btn.addEventListener('click', function () { applyLang(document.documentElement.lang === 'tr' ? 'en' : 'tr'); });
  });

  var menu = document.querySelector('[data-menu]');
  var nav = document.getElementById('nav');
  if (menu && nav) {
    menu.addEventListener('click', function () {
      var open = nav.classList.toggle('open');
      menu.setAttribute('aria-expanded', String(open));
    });
    nav.addEventListener('click', function (event) {
      if (event.target.closest('a')) { nav.classList.remove('open'); menu.setAttribute('aria-expanded', 'false'); }
    });
  }

  var lang = initialLang();
  if (lang === 'tr') applyLang('tr'); else setGitHubLinks();
})();
