<div align="center">

# GetMan

**Windows uchun nativ API mijozi — bitta `.exe` ichiga sig'gan Postman alternativasi.**

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)](#)
[![Languages: 3](https://img.shields.io/badge/UI-English%20·%20Русский%20·%20O'zbekcha-38BDF8)](#interfeys-tillari)

[English](README.md) · [Русский](README.ru.md) · **O'zbekcha**

<img src="docs/images/main-uz.png" alt="GetMan postman-echo'ga so'rov yubormoqda: javob tanasi, testlar va vaqtlar" width="900">

</div>

---

**WPF va .NET 9** asosida yozilgan, **MaterialDesignInXamlToolkit** (Material Design 3) bilan
bezatilgan. Electron yo'q, Chromium yo'q, fonda ishlaydigan node jarayoni yo'q: bitta nativ `.exe`
bir zumda ochiladi va mavjud Postman kolleksiyalaringizni o'z holicha import qiladi.

```
dotnet run --project src/GetMan            # manbadan ishga tushirish
dotnet publish src/GetMan -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist        # bitta faylli GetMan.exe
dotnet publish src/GetMan.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist-cli    # konsolli getman.exe
```

Ish maydoni `%APPDATA%\GetMan\workspace.json` faylida saqlanadi (zaxira nusxasi bilan), har bir
Windows foydalanuvchisi uchun alohida. Hech narsa hech qayerga yuborilmaydi.

## Interfeys tillari

GetMan **ingliz, rus va o'zbek** tillarida gapiradi. Yuqoridagi paneldan tilni tanlang — barcha
yozuvlar, tugma izohlari, dialoglar va holat xabarlari darhol o'zgaradi, dasturni qayta ishga
tushirish shart emas. Birinchi ishga tushirishda GetMan Windows tilini oladi, agar u shu uchtadan
biri bo'lsa; aks holda ingliz tilini tanlaydi. Tanlov qolgan sozlamalar bilan birga saqlanadi.

| | |
|---|---|
| <img src="docs/images/main-dark.png" alt="GetMan ingliz interfeysida" width="430"> | <img src="docs/images/main-ru.png" alt="GetMan rus interfeysida" width="430"> |
| English | Русский |

Har bir satr [`src/GetMan/Assets/Lang/`](src/GetMan/Assets/Lang) ichidagi har bir tilga bitta JSON
faylda yotadi. To'rtinchi tilni qo'shish uchun `en.json` nusxasini olib, qiymatlarni tarjima qilish
va `Loc.Languages` ga bitta yozuv qo'shish kifoya — [CONTRIBUTING.md](CONTRIBUTING.md) ga qarang.
`GetMan.exe --self-check` biror tilda kalit yetishmasa buildni yiqitadi, shuning uchun tarjimalar
sezdirmay orqada qolib ketmaydi.

## Skrinshotlar

| Yorug' mavzu | Kolleksiya runneri |
|---|---|
| <img src="docs/images/main-light.png" alt="GetMan yorug' mavzuda" width="430"> | <img src="docs/images/runner.png" alt="Tugagan prognozdan keyingi kolleksiya runneri" width="430"> |

| Muhitlar | Sozlamalar |
|---|---|
| <img src="docs/images/environments.png" alt="Muhitlar menejeri" width="430"> | <img src="docs/images/settings.png" alt="Sozlamalar oynasi" width="430"> |

<img src="docs/images/dialog.png" alt="Dastur uslubidagi tasdiqlash dialogi" width="380">

---

## Postman kolleksiyalarini qanday ko'chirish mumkin

Paneldagi **Postman'dan** tugmasi ikkita ishonchli yo'li bor dialogni ochadi:

- **Shu kompyuterda** — Downloads, Documents, Desktop va OneDrive papkalarini
  `*.postman_collection.json`, `*.postman_environment.json`, global o'zgaruvchilar va to'liq
  ma'lumot dumplari uchun skanerlaydi, har bir faylda nima borligini ko'rsatadi (nomi, turi,
  so'rovlar soni) va belgilanganlarini import qiladi. Qo'shimcha papkalarni ham ko'rsatish mumkin.
- **Postman akkaunti** — shaxsiy API kalitni joylashtiring (postman.co → Settings → API keys), va
  GetMan `api.getpostman.com` orqali akkauntdagi barcha kolleksiya va muhitlarni ro'yxatlaydi,
  so'ng tanlanganlarini yuklab oladi. Postman 10 dan boshlab dastur bulut bilan sinxronlanadi,
  shuning uchun bu aynan o'rnatilgan dastur ko'rsatayotgan narsa.

GetMan o'rnatilgan Postman'ni aniqlaydi va uning versiyasini ko'rsatadi, lekin ataylab uning o'z
bazasini o'qishga **urinmaydi**: u Chromium IndexedDB (LevelDB + snappy + V8 structured clone),
hujjatlashtirilmagan, versiyadan versiyaga o'zgaradi va Postman ishlab turganda fayllar qulflangan
bo'ladi. Sinov chog'ida undan birorta ham tiklab bo'ladigan kolleksiya chiqmadi, shuning uchun
GetMan yuqoridagi ikki yo'lni taklif qiladi.

## GetMan nimalarni import qila oladi

**Import** — `Import` tugmasi, `Ctrl+O` yoki xom matn uchun `Matn / cURL`. Format mazmunidan
aniqlanadi, shuning uchun bitta tugma bularning hammasini qabul qiladi:

| Format | Qo'llab-quvvatlanadi |
|---|---|
| Postman kolleksiyasi v2.1 | ha |
| Postman kolleksiyasi v2.0 | ha |
| Postman kolleksiyasi v1 (`requests` + `folders`) | ha |
| Postman muhiti / global o'zgaruvchilar eksporti | ha |
| Postman "Export data" dumpi (`collections` + `environments` + `globals`) | ha |
| **OpenAPI 3.0 / 3.1**, JSON yoki YAML | ha |
| **Swagger 2.0**, JSON yoki YAML | ha |
| cURL buyrug'i (bash yoki cmd qo'shtirnoqlari) | ha |

### OpenAPI va Swagger

GetMan'ga `swagger.json` yoki `openapi.yaml` ni bering — URL ro'yxatini emas, ishlaydigan kolleksiya
olasiz:

- **Har bir tegga bitta papka.** Tegsiz operatsiya o'z yo'lining birinchi bo'lagi ostiga tushadi.
- **`servers` → `{{baseUrl}}`**, ham kolleksiya o'zgaruvchisi, ham muhit sifatida — shunda staging va
  production o'rtasida almashish ochiladigan ro'yxatga aylanadi. `https://{region}.api.example.com`
  kabi shablonli server `region` ni alohida o'zgaruvchi qiladi va tavsifdagi standart qiymatni beradi.
- **Parametrlar jadval qatorlariga aylanadi.** Majburiy query parametrlari URL'ga tushadi,
  ixtiyoriylari esa kerak bo'lganda belgilaydigan o'chirilgan qatorlarga. `path` parametrlari
  `:name` bo'lagiga, `header` parametrlari sarlavhalarga aylanadi va hammasi tavsifini saqlaydi.
- **So'rov tanasi sxemadan yaratiladi** — `$ref` ochiladi, `allOf` birlashtiriladi, `oneOf`/`anyOf`
  birinchi tarmoqni oladi, `example`, `default` va `enum` o'rin tutuvchilardan ustun turadi,
  formatlar esa ishonarli qiymatlarga aylanadi (`date` → `2026-01-31`, `uuid` → `{{$guid}}`,
  `email` → `user@example.com`). O'ziga havola qiladigan sxema aylanmaydi, tugaydi.
  `multipart/form-data` form-data bo'ladi va `format: binary` maydonlari fayl qatoriga aylanadi;
  `application/x-www-form-urlencoded` esa urlencoded tanaga.
- **Xavfsizlik sxemalari auth'ga aylanadi.** `http bearer`, `http basic`, `apiKey` (sarlavhada yoki
  query'da) va `oauth2` GetMan auth'iga tushadi, hisob ma'lumotlari esa to'ldirish uchun bo'sh
  kolleksiya o'zgaruvchisi bo'lib qoladi — tavsifda hech qachon maxfiy ma'lumot bo'lmaydi va GetMan
  uni o'ylab topmaydi. Bitta operatsiyada e'lon qilingan talab kolleksiya standartidan ustun turadi.

GetMan ko'chira olmagan narsa jimgina tashlab ketilmaydi, importdan keyin xabar qilinadi: ikkinchi
server, u qura olmaydigan tana turi, ekvivalenti yo'q xavfsizlik sxemasi.

**Import qilinmaydi:** WSDL, HAR, Insomnia. Bular alohida formatlar.

### Postman kolleksiyalari

Kolleksiya ichidagi hamma narsa ko'chib o'tadi: ichma-ich papkalar, query parametrlari (o'chirilgan
qatorlar ham), massiv *yoki* qatorlar bilan ajratilgan matn ko'rinishidagi sarlavhalar, tananing
barcha rejimlari (`raw` va uning tili, `urlencoded`, fayl maydonlari bilan `formdata`, `file`,
`graphql`), kolleksiya/papka/so'rov avtorizatsiyasi, pre-request va test skriptlari (`exec` massiv
yoki bitta satr sifatida), kolleksiya va papka o'zgaruvchilari, yo'l o'zgaruvchilari, tavsif
obyektlari va `protocolProfileBehavior` (`followRedirects`, `strictSSL`, `maxRedirects`,
`disableUrlEncoding`, `disableCookies`).

**Eksport** — kolleksiyani o'ng tugma bilan bosing → *Postman v2.1 sifatida eksport*, yoki istalgan
muhitni eksport qiling. Eksport qilingan fayllar Postman'ga ham, GetMan'ga ham toza qaytib import
bo'ladi — OpenAPI tavsifidan boshlangan kolleksiya ham.

---

## Ichida nima bor

**So'rov konstruktori**
- Barcha HTTP metodlari va o'zingiz kiritganlari (`PURGE`, `PROPFIND`, …)
- URL satri query-parametrlar jadvali bilan ikki tomonlama sinxron; `:pathVariable` bo'laklari
  tahrirlanadigan qatorlarga aylanadi
- Har bir qatorni yoqib-o'chirish va tavsif imkoniyati bilan sarlavhalar jadvali
- Tana: yo'q, form-data (fayl yuklash bilan), x-www-form-urlencoded, raw
  (JSON/XML/HTML/JavaScript/matn, chiroyli ko'rinishga keltirish bilan), binar fayl, GraphQL
  (so'rov + o'zgaruvchilar)
- Har bir so'rov sozlamalari: yo'naltirishlar va ularning maksimumi, SSL tekshiruvi, URL kodlash,
  cookie yuborish va saqlash, taymaut, HTTP versiyasi (1.0/1.1/2/3)

**Avtorizatsiya** — Inherit, None, Bearer, Basic, API key (sarlavhada yoki query'da), OAuth 2.0
(client credentials, PKCE va lokal redirect tinglovchi bilan authorization code, password, refresh
token), Digest (MD5, MD5-sess, SHA-256 bilan challenge/response), NTLM, AWS Signature v4 va Hawk.
Kolleksiya yoki papkada sozlangan auth pastga xuddi Postman'dagidek meros bo'lib o'tadi.

**Skriptlar** — Postman API'siga ega haqiqiy JavaScript qumdoni (Jint):
`pm.test`, `pm.expect` (chai uslubidagi tasdiqlash kutubxonasi: `equal`, `eql`, `a`/`an`,
`above`/`below`/`least`/`most`/`within`, `include`, `property`, `lengthOf`, `keys`, `members`,
`match`, `oneOf`, `empty`, `ok`, `true`/`false`/`null`/`undefined` va hammasiga `.not`),
`pm.response.to.have.status/header/jsonBody/body`,
`pm.response.to.be.json/ok/success/clientError/serverError`,
`pm.environment` / `pm.globals` / `pm.collectionVariables` / `pm.variables` / `pm.iterationData`,
`pm.request` ni o'zgartirish (`headers.add/upsert/remove`, metod, url, tana), `pm.sendRequest`,
`pm.cookies`, `pm.info`, `pm.execution.setNextRequest`, `console.*`, shuningdek eski
`postman.setEnvironmentVariable`, `tests["name"] = …`, `responseCode`, `responseBody`, `xml2Json`,
`btoa`/`atob`. Kolleksiya → papka → so'rov skriptlari shu tartibda ishlaydi.

**O'zgaruvchilar** — global, kolleksiya, papka, muhit, ma'lumot va skriptdagi lokal doiralar
Postman'dagi ustunlik tartibi bilan, `{{ichma-ich}}` yechimi va dinamik generatorlar (`{{$guid}}`,
`{{$timestamp}}`, `{{$isoTimestamp}}`, `{{$randomInt}}` — shu jumladan `{{$randomInt(1,100)}}` —
`{{$randomFullName}}`, `{{$randomEmail}}` va yana ~40 tasi). Yechilmagan tokenlar bo'shatilmaydi,
o'z holicha qoladi — shunda nima yetishmayotgani ko'rinadi.

**Javob ko'ruvchi** — Chiroyli / Xom / Ko'rinish, JSON, XML/HTML va JavaScript uchun sintaksis
bo'yash, rasmlarni ko'rsatish, `Ctrl+F` bilan qidiruv, javob sarlavhalari, cookie'lar, test
natijalari, konsol paneli va vaqt taqsimoti (DNS, TCP, TLS, birinchi baytgacha vaqt, yuklash, jami).

**Kolleksiya runneri** — so'rovlarni tanlang, takrorlashlar sonini va kechikishni belgilang, CSV yoki
JSON ma'lumot faylidan yuriting, birinchi muvaffaqiyatsizlikda to'xtang, `setNextRequest` ni hisobga
oling va har bir so'rov test natijalarini jonli kuzating.

**Buyruq satri** — oynasiz, o'sha runner, CI uchun.
[Kolleksiyalarni buyruq satridan ishga tushirish](#kolleksiyalarni-buyruq-satridan-ishga-tushirish)
ga qarang.

**Interfeys tillari** — ingliz, rus va o'zbek, yuqoridagi paneldan jonli almashtiriladi.
[Interfeys tillari](#interfeys-tillari) ga qarang.

**Yana** — menejeri bilan umumiy cookie ombori, so'rovlar tarixi, 15 ta nishon uchun kod generatsiyasi
(cURL, PowerShell, C#, Python, JavaScript fetch/axios, Node, Go, Java, PHP, Ruby, Rust, Dart, xom
HTTP), saqlanmagan o'zgarish belgisi bilan so'rov yorliqlari, daraxtda sudrab ko'chirish, tizim yoki
o'z proksisi va mijoz sertifikatlari.

**Tezkor tugmalar** — `Ctrl+Enter` yuborish · `Ctrl+S` saqlash · `Ctrl+N` yangi so'rov ·
`Ctrl+W` yorliqni yopish · `Ctrl+O` import · `Ctrl+E` muhitlar · `Ctrl+R` runner ·
`Ctrl+Shift+D` yorug'/qorong'i mavzu · `F2` tanlangan so'rov, papka yoki kolleksiya nomini
o'zgartirish.

## Kolleksiyalarni buyruq satridan ishga tushirish

`getman.exe` — ikkinchi, faqat konsolli fayl. U oynadagi bilan **bir xil** dvigatelni yuritadi: o'sha
importer, o'zgaruvchilar yechimi, auth imzosi va Jint skript muhiti. Dasturda o'tgan kolleksiya bu
yerda ham o'tadi, aksi ham to'g'ri. CI vazifasini aynan shunga qaratasiz.

```
getman run api.postman_collection.json -e staging.postman_environment.json
getman run api.json -d users.csv -n 50 --delay 200 --bail
getman run api.json -r junit -o results/getman.xml
```

```
  GetMan 1.0.0 - running "CLI demo"

  ✓  GET    Echo GET   200 OK   630 ms   1.1 KB
       ✓ Status code is 200
       ✓ Echoes the who variable
  ✗  GET    Deliberate failure   404 Not Found   185 ms   416 B
       ✗ This one is meant to fail  expected response code to be 200 but got 404

  3 request(s), 5 assertion(s), 4 passed, 1 failed
  total 1.16 s
```

| Parametr | Nima qiladi |
|---|---|
| `-e, --environment <file>` | Postman muhit eksporti; chapdan o'ngga birlashtirish uchun takrorlang |
| `-g, --globals <file>` | Postman global o'zgaruvchilar eksporti |
| `-d, --data <file>` | CSV yoki JSON ma'lumot fayli, har qator bitta takrorlash |
| `-n, --iterations <n>` | takrorlashlar soni (sukut bo'yicha ma'lumot qatorlari soni, aks holda 1) |
| `--delay <ms>` | so'rovlar orasidagi kutish |
| `--folder <name>` | kolleksiyaning faqat shu papkasini ishga tushirish |
| `--var name=value` | o'zgaruvchi berish; muhit faylidan ustun, takrorlanadi |
| `--timeout <ms>` / `--script-timeout <ms>` | so'rov va skript taymautlari |
| `--insecure` | TLS sertifikatlarini tekshirmaslik |
| `--bail` | birinchi yiqilgan so'rovda to'xtash |
| `-r, --reporter <cli\|json\|junit>` | chiqish formati |
| `-o, --output <file>` | hisobotni stdout o'rniga faylga yozish |
| `--lang <en\|ru\|uz>` · `--no-color` | til va rangsiz chiqish |

**Chiqish kodlari** — `0` hamma so'rov javob berdi va hamma tekshiruv o'tdi · `1` tekshiruv o'tmadi
yoki so'rov javob olmadi · `2` argumentlar yoki fayllar noto'g'ri. CI vazifasiga shundan ortig'i
kerak emas, ustiga `--reporter junit` unga ko'rsatish uchun hisobot beradi.

Skript o'rnatgan o'zgaruvchilar keyingi so'rovga xuddi dasturdagidek o'tadi: token saqlaydigan login
so'rovi va uni ishlatadigan keyingi so'rov o'zgarishsiz ishlayveradi.

```yaml
- run: getman run api.json -e ci.postman_environment.json --var token=${{ secrets.API_TOKEN }} -r junit -o report.xml
```

## Dizayn tizimi

Interfeys Material Design 3 (MaterialDesignInXamlToolkit, MIT) ustiga qurilgan
**dasturchi vositasi / IDE** dizayn tizimiga amal qiladi.

**Rang.** Slanets fon va bir-birining ishini bajarmaydigan ikki urg'u: yashil — ishga tushirish
harakati, osmon rangi — tanlov va fokus. Metod va holat ranglari buning ustida semantik bo'lib
qoladi.

| Token | Qorong'i | Yorug' | Nima uchun |
|---|---|---|---|
| `Bg0` … `Bg4` | `#0F172A` → `#334155` | `#FFFFFF` → `#DCE5EF` | fon, panel, chrome, hover, tanlangan |
| `Fg` / `FgDim` / `FgMuted` | `#F8FAFC` / `#94A3B8` / `#64748B` | `#0F172A` / `#475569` / `#64748B` | matn ierarxiyasi |
| `Action` | `#22C55E` | `#16A34A` | Yuborish va barcha asosiy tugmalar |
| `Accent` | `#38BDF8` | `#0284C7` | tanlov, yorliq chizig'i, fokus halqasi, havolalar |

Fonga nisbatan kontrast: qorong'ida `Fg` 16.4:1, `FgDim` 7.4:1, `FgMuted` 4.6:1; yorug'da
17.9:1 / 7.5:1 / 4.8:1 — hammasi 4.5:1 chegarasidan yuqori.

**Shrift.** Interfeys uchun Fira Sans, kodga o'xshash hamma narsa uchun Fira Mono (URL'lar,
sarlavhalar, tanalar, skriptlar, kod parchalari). Ikkalasi ham SIL Open Font litsenziyasi ostida
binarga joylashtirilgan va lotin, kirill hamda yunon yozuvlarini qamraydi.

**Joylashuv.** 52px balandlikdagi dastur paneli, 72px kenglikdagi belgilar paneli (Kolleksiyalar /
Muhitlar / Tarix, pastida runner, cookie va sozlamalar), kengligi o'zgaradigan yon panel va
"so'rov javob ustida" bo'linishi. Oraliqlar 4/8/12/16/24 shkalasida, burchaklar 5/8/12.

**Mavzular.** Sukut bo'yicha qorong'i, yorug'i bir bosishda (panel yoki `Ctrl+Shift+D`). Har bir
rang — `DynamicResource` token, shuning uchun `Themes/Tokens.Dark.xaml` va
`Themes/Tokens.Light.xaml` ish vaqtida almashadi — muharrirdagi sintaksis bo'yash ham, uning o'z
yorug' palitrasi bor.

**Diagrammalar.** Vaqt taqsimoti — legendasi bor qatlamli shalola-chiziq, ostida esa xuddi shu
raqamlar jadval ko'rinishida: bunday nisbatlarda doiraviy diagramma o'qishga qiyinroq va ekran
o'quvchilari uchun yomonroq bo'lardi.

**Qulaylik.** Har bir interaktiv elementda ko'rinadigan osmon rangli fokus halqasi, yozuvsiz
tugmalarda `AutomationProperties.Name`, keyin nima qilishni aytadigan bo'sh holatlar, faqat rang
emas, belgi va matn bilan ko'rsatiladigan xatolar va Windows'ning "animatsiyalarni ko'rsatish"
sozlamasiga rioya — u o'chiq bo'lsa barcha davomiylik nolga tushadi.

**Harakat.** Interaktiv hamma narsa kursorga javob beradi:

| Element | Ustiga kelganda | Tanlangan / bosilgan |
|---|---|---|
| Tugmalar | kattalashadi, chegara urg'u tomon isiydi | bosilganda kichrayadi, asosiy tugma bir pog'ona ko'tariladi |
| Belgili tugmalar | kattalashadi, urg'u rangiga bo'yaladi | bosilganda kichrayadi |
| Yuborish tugmasi | qog'oz samolyot oldinga siljiydi | — |
| Daraxt va ro'yxat qatorlari | fon yorishadi, tarkib o'ngga siljiydi | chapdan urg'u chizig'i chiqadi |
| Bo'lim yorliqlari | fon yorishadi, yozuv yorqinlashadi | tag chizig'i markazdan o'sadi |
| So'rov yorliqlari | fon yorishadi, yopish belgisi to'qlashadi | urg'u chizig'i yuqori bo'ylab o'sadi |
| Daraxt ochish belgisi | shevron bo'yaladi | 90° ga buriladi |
| Maydonlar | yumshoq nur paydo bo'ladi | fokusda yorqinroq nur |
| Kartochkalar | 2px ko'tariladi, soya Dp1 → Dp3 | — |
| Ajratgichlar | chiziq ustida urg'u paydo bo'ladi | sudrayotganda yoqilgan turadi |
| Javob paneli | — | har safar javob kelganda 14px ko'tarilib paydo bo'ladi |

Davomiylik 130–280 ms, kubik ease-out bilan — bir zumdek his qilinadigan darajada qisqa — va
`DurFast` / `DurSlow` / `DurPop` resurslarida yashaydi, shunda kamaytirilgan harakat rejimi ularni
nolga tushira oladi. Effektlarni ikki mexanizm ko'taradi: `Controls/HoverAssist.cs` (istalgan
elementga masshtab / ko'tarilish / siljish / burilish beradigan biriktirilgan xossa, transformlar
kodda quriladi) va GetMan o'ziga tegishli shablonlar ichidagi qatlamli `Opacity` qoplamalari —
shunda hover va tanlov bitta cho'tka uchun tortishmaydi. Hech qanday cho'tka animatsiya qilinmaydi:
`Setter` ga berilgan cho'tka uslub muhrlanganda muzlatiladi va animatsiya ish vaqtida yiqilardi.

`Themes/Tokens.*.xaml` rang tokenlarini, `Themes/Typography.xaml` shrift va shkalani,
`Themes/Animations.xaml` harakatni saqlaydi, `Themes/Controls.xaml` esa Material uslublari ustidagi
yupqa qatlam. Dasturni qayta bo'yash uchun shularni o'zgartiring.

---

## Tuzilma

```
src/GetMan/
  Models/        so'rov, kolleksiya, muhit va javob modellari
  Services/      HTTP dvigateli, o'zgaruvchilar yechimi, skript muhiti, Postman import/eksporti,
                 cURL importi, kod generatsiyasi, saqlash
  ViewModels/    MainViewModel, RequestTabViewModel
  Views/         so'rov/javob muharrirlari va dialog oynalari
  Controls/      AvalonEdit xosti, kalit-qiymat jadvali, konverterlar
  Themes/        ranglar va boshqaruv elementlari uslublari
tools/SelfTest/  butun xizmat qatlami uchun headless testlar to'plami
```

## Testlar

```
dotnet run --project tools/SelfTest                      # postman-echo'ga jonli HTTP bilan
dotnet run --project tools/SelfTest -- --offline         # tarmoq bo'limisiz
dotnet run --project tools/SelfTest -- --import a.json   # haqiqiy kolleksiyalarni import va round-trip
dotnet run --project tools/SelfTest -- --unicode         # faqat kirill / CJK / emoji

GetMan.exe --self-check                                  # barcha oynalarni quradi, so'ng haqiqiy
                                                         # ssenariylarni yuritadi (nom o'zgartirish
                                                         # fokusi, yaratish, qidiruv, sudrash,
                                                         # yorliqlar, mavzu)
GetMan.exe --render auth shot.png [light]                # bitta ko'rinishni ekrandan tashqarida chizish
GetMan.exe --shots docs/images                           # hujjat skrinshotlarini qayta yaratish
powershell -File tools/capture.ps1 -Out shot.png         # ishlab turgan dastur skrinshoti
powershell -File tools/capture.ps1 -HoverX 115 -HoverY 244  # ...hoverni ko'rsatish uchun kursor bilan
```

To'plam kolleksiya importini (v1/v2.0/v2.1 va noqulay real shakllar), muhit importini, eksport
round-tripini, lokal Postman'ni aniqlashni, o'zgaruvchilar ustunligi va dinamik o'zgaruvchilarni,
cURL tahlilini, so'rov tayyorlash va auth merosini, AWS SigV4 / Digest / Hawk imzosini, kod
generatsiyasini, butun `pm.*` skript yuzasini, jonli HTTP'ni
(GET/POST/form/multipart/basic-auth/cookies/404/DNS xatosi) va kolleksiya skripti, so'rov skripti,
so'rovning o'zi va uning testlari kelishishi kerak bo'lgan uchdan-uchgacha prognozni qamraydi.

---

## Loyihaga hissa qo'shish

Xato hisobotlari, tarjimalar va pull request'lar mamnuniyat bilan qabul qilinadi.
[CONTRIBUTING.md](CONTRIBUTING.md) dan boshlang — unda build, har bir o'zgarish o'tishi shart
bo'lgan ikkita test to'plami va tilni qo'shish yoki tuzatish yo'li bayon qilingan. Ishtirok
etuvchilarning barchasidan [xulq-atvor kodeksi](CODE_OF_CONDUCT.md) ga rioya qilish kutiladi.
Xavfsizlik muammolari uchun alohida yo'l bor: [SECURITY.md](SECURITY.md).

## Litsenziya

[MIT](LICENSE) © GetMan ishtirokchilari.

GetMan boshqalarning mehnati ustida turibdi:
[MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
(MIT), [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) (MIT),
[Jint](https://github.com/sebastienros/jint) (BSD-2-Clause),
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT),
[YamlDotNet](https://github.com/aaubry/YamlDotNet) (MIT) va
[Fira Sans / Fira Mono](https://github.com/mozilla/Fira) (SIL Open Font License 1.1).

GetMan Postman, Inc. bilan bog'liq emas. "Postman" nomi faqat GetMan o'qiydigan fayl formatlari va
API'ni tavsiflash uchun ishlatiladi.
