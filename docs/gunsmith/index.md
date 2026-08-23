# 3D-студия оружейных скинов

Мастерская - это полноценный редактор оружейных скинов: three.js-сцена со стволом, рисование прямо по UV, наклейки и текст слоями, вшивание своих 3D-моделей, запись результата обратно в `.ydr`/`.ytd`. Живёт она в `<iframe>` внутри того же WebView2, где крутится React-оболочка лаунчера, а всю тяжёлую работу с форматами GTA делает C#.

Из этого вырастают четыре инженерные задачи, каждая со своим неочевидным решением. Редактору нужен HTTP-протокол - но поднимать в десктопном приложении локальный веб-сервер мы отказались. Ему нужны файлы конкретного ствола - но они лежат внутри `pack.zip` на 20-53 МБ, а стволу нужно 3-4 файла из него. Ему нужно говорить с оболочкой - но это два разных origin внутри одного WebView2. И ему нужна 3D-модель - которую надо собирать из `.ydr` и пересобирать ровно тогда, когда что-то поменялось, и ни разом чаще.

Эта страница про механику. Форматы `.ydr`/`.ytd` разобраны в [обзоре RPF](../rpf/index.md), конвертер в `.glb` и просмотрщик каталога - в [GLB-viewer в UI](../3d/glb-viewer.md).

## Виртуальный хост вместо веб-сервера

Редактор - обычная веб-страница: `index.html`, ES-модули, `fetch('/api/...')`, `GLTFLoader` тянет модель по URL. Самый прямой способ это обслужить - поднять Kestrel на localhost. Мы его не поднимаем: это слушающий порт, который надо выбрать, который может быть занят и к которому может обратиться любой процесс на машине.

Вместо этого мы вешаемся на `WebResourceRequested` WebView2 и отвечаем на запросы к хосту `gunsmith.huntergraphics.local` сами - и на статику, и на `/api/*`. Одна origin на страницу и на API, никакого CORS, полный контроль над `/api`, и запрос не покидает процесс.

```cs title="MiamiGraphics.Shell/Services/GunsmithApiInterceptor.cs"
private const string Host = "gunsmith.huntergraphics.local";

public void Register()
{
    if (_registered) return;
    _registered = true;
    // Весь хост, любой контекст - мы отдаём и статику, не только /api.
    _webView.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
    _webView.WebResourceRequested += OnRequested;
}
```

Обработчик берёт deferral (ответ асинхронный - материализация ствола ходит в сеть), разбирает путь и разводит запрос по двум веткам:

```cs title="MiamiGraphics.Shell/Services/GunsmithApiInterceptor.cs"
var deferral = args.GetDeferral();
...
if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
{
    var res = await _service.HandleAsync(method, path, ParseQuery(uri.Query), body, CancellationToken.None);
    args.Response = _webView.Environment.CreateWebResourceResponse(
        new MemoryStream(res.Body, writable: false), res.Status, StatusReason(res.Status),
        "Content-Type: " + res.ContentType + "\r\nCache-Control: no-store");
}
else ServeStatic(args, path);
```

### Почему не SetVirtualHostNameToFolderMapping

У WebView2 есть готовый механизм отдачи локальной папки под виртуальным именем - `SetVirtualHostNameToFolderMapping`. Для React-оболочки мы им и пользуемся: `app.huntergraphics.local` с `DenyCors` смотрит в `bin\...\ui\`.

Для мастерской он не годится. WebView2 **не поднимает** `WebResourceRequested` для ресурсов, отданных folder-mapping'ом. Отдай мы статику маппингом - `fetch('/api/gun')` ушёл бы туда же и получил 404 от файловой системы, в C# он бы не пришёл никогда. Поэтому перехватчик обслуживает весь хост целиком, а побочным эффектом мы получаем два разных origin внутри одного WebView2 (`app.…` и `gunsmith.…`) - отсюда протокол postMessage, разобранный ниже.

### Отдача статики

Путь резолвится относительно корня статики и обязан под ним остаться - иначе 404. Дальше три вещи, которые тут важны:

- `Cache-Control: no-store` на **всех** ответах, включая статику. Файлы редактора правятся без пересборки C# - мы читаем их с диска на каждый запрос, и кеш браузера прятал бы свежие правки.
- MIME отдаём сами. `.js`/`.mjs` обязаны получить `text/javascript`, иначе браузер откажется исполнять ES-модуль, а весь редактор - это модули с importmap.
- three.js лежит рядом, а не на CDN: `vendor/three.module.js`, ревизия 169, 1.36 МБ, плюс четыре аддона (`OrbitControls`, `GLTFLoader`, `RoomEnvironment`, `BufferGeometryUtils`). Мастерская работает без сети, если файлы ствола уже в кеше.

### Ворота записи

Мастерская ходит своим каналом, мимо основного моста между React и C#. На аудите выяснилось, что её записывающие ручки обходили общий запрет на изменение файлов игры в режиме Rockstar (файлы заморожены, запись всё равно не сохранится). Шлюз теперь стоит в самом перехватчике: пути `/api/apply`, `/api/anim-install`, `/api/anim-remove`, `/api/install` и `/api/remove` при заморозке получают 409 и до `GunsmithService` не доходят. Всё остальное - чтение, его не блокируем.

## Ручки API

Единственная точка входа - `GunsmithService.HandleAsync(method, path, query, body, ct)`. Ключ везде один и тот же: `pack` - id ганпака (либо псевдо-пак `_custom` для сохранённого скина, либо `_vanilla` для стандартного ствола игры), `gun` - internal_name ствола (либо id строки для `_custom`).

| Путь | Метод | Что делает | Рабочая копия |
|---|---|---|---|
| `/api/packs` | GET | каталог паков и стволов | не трогает |
| `/api/progress` | GET | фаза и процент материализации | не трогает и **не запускает** |
| `/api/gun` | GET | текстуры, шейдеры, кости, вершины, стекло | материализует оригинал |
| `/api/glb` | GET | модель для сцены | пересобирает превью, если устарело |
| `/api/texture` | GET | PNG текстуры, опционально `size` | читает активную версию |
| `/api/texture` | POST | записать PNG в текстуру | создаёт |
| `/api/attach`, `/api/attach-mesh` | POST | вшить пластинку, брелок или свой меш | создаёт |
| `/api/glass` | POST | пометить текстуры стеклом | создаёт |
| `/api/reset` | POST | начать заново | удаляет |
| `/api/apply` | POST | поставить скин в игру | читает |
| `/api/save`, `/api/publish` | POST | слот сохранения, отправка на модерацию | читает |

Исключения маппятся в коды в одном месте: `OperationCanceledException` → 499, `FileNotFoundException`/`DirectoryNotFoundException` → 404, `ArgumentException` → 400, остальное → 500. Тело ошибки - всегда `{ok:false, error:"текст"}`, и редактор берёт сообщением именно текст из тела: раньше он выбрасывал тело и писал в журнал голое «500 /api/gun?…», по которому разобрать причину было нельзя. Каждый запрос уходит в `gunsmith-api.log` строкой вида `POST /api/attach -> 200 (312b)`, для статусов ≥ 400 дописывается тело ответа.

## Откуда берутся файлы ствола

`EnsureAsync` идемпотентна: если в `extract\{pack}\{gun}\` уже лежит хоть один `.ydr`, она сразу возвращает ключи. Иначе материализует, и дальше путь зависит от типа пака.

| `pack` | Что означает `gun` | Источник файлов |
|---|---|---|
| id ганпака | internal_name ствола | `pack.zip` пака: кеш → Range → полная загрузка |
| `_custom` | id строки сохранённого скина | zip скина по его `files_url`; если скин ни разу не публиковался - базовый ствол из ганпака |
| `_vanilla` | internal_name стандартного ствола | наше хранилище с проверкой sha256; запасной путь - извлечение из RPF игры |

У `_vanilla` порядок именно такой не случайно. Извлечение из игры было бесплатным и всегда совпадало с версией игрока, но ломалось на урезанных сборках, на другом билде и когда не загрузились ключи. Хуже: если у человека стоит редукс, переписавший `weapons.rpf`, «базой стандартного ствола» становился чужой мод - и красил игрок не ваниль. Поэтому основной путь - архив из хранилища, одинаковый у всех и сверяемый по sha256, а извлечение из игры осталось запасным на случай, когда до серверов не достучаться.

Для ганпака лестница из трёх ступеней:

```cs title="MiamiGraphics.Shell/Services/GunsmithService.cs"
// 1) пак уже целиком в кеше → достаём с диска, сеть не нужна
var cached = PackZipCache.CachedPathOrNull(pack.PackZipSha256!);
if (cached != null) files = PackZipCache.ExtractFiles(cached, g.Files);

// 2) не в кеше → тянем Range'ами ТОЛЬКО файлы этого ствола
if (files == null || files.Count == 0)
    files = await RangeZipFetcher.TryFetchAsync(pack.PackZipUrl!, g.Files, prog, ct);

// 3) частичная выборка не удалась → полная загрузка с кешированием
if (files == null || files.Count == 0)
{
    var zip = await _packZip.EnsurePackZipAsync(pack.PackZipUrl!, pack.PackZipSha256!, pack.PackZipSize, prog, ct);
    files = PackZipCache.ExtractFiles(zip, g.Files);
}
```

## Выборка по HTTP Range

`pack.zip` весит 20-53 МБ и содержит все стволы пака. Открыть один ствол - это 3-4 файла: `.ydr`, `_hi.ydr`, `.ytd`, иногда магазин. Качать ради них весь архив бессмысленно, поэтому `RangeZipFetcher` читает zip удалённо, как локальный: сначала хвост с центральным каталогом, потом точечно нужные записи.

```cs title="MiamiGraphics.Shell/Services/RangeZipFetcher.cs"
private const int MaxTail = 256 * 1024;

long total = await GetLengthAsync(url, ct);      // GET c Range: 0-0, ждём 206
int tailLen = (int)Math.Min(total, MaxTail);
var tail = await RangeAsync(url, total - tailLen, total - 1, ct);

int eocd = -1;                                    // EOCD ищем в хвосте с конца
for (int i = tail.Length - 22; i >= 0; i--)
    if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EOCD_SIG) { eocd = i; break; }
if (eocd < 0) return null;

uint cdSize   = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF) return null;   // zip64 → полный путь
```

Центральный каталог чаще всего уже целиком лежит в этих 256 КБ хвоста - тогда второго запроса нет вовсе. Дальше по каждой совпавшей записи забирается локальный заголовок и данные **одним** диапазоном, с запасом 512 байт на имя и extra-поле, чтобы не платить за отдельный запрос ради 30 байт заголовка:

```cs title="MiamiGraphics.Shell/Services/RangeZipFetcher.cs"
long blockEnd = e.localOffset + 30 + 512 + e.compSize;
var block = await RangeAsync(url, e.localOffset, Math.Min(blockEnd, total - 1), ct);
// длины имени и extra читаем из локального заголовка (смещения 26 и 28)
int dataStart = 30 + lNameLen + lExtraLen;
```

Сопоставление идёт по **базовому** имени файла: в `pack.zip` записи лежат в папках вида `carbinerifle/w_ar_carbinerifle.ydr`, а список нужных файлов в каталоге хранит голые имена.

Весь класс - best-effort. Любая проблема возвращает `null`, и вызывающий уходит на полную загрузку:

| Условие | Почему сдаёмся |
|---|---|
| ответ на `Range: 0-0` не 206 | сервер или прокси не поддерживает диапазоны |
| `cdOffset` или `cdSize` = `0xFFFFFFFF` | zip64, разбирать его тут не умеем |
| метод сжатия не 0 и не 8 | поддерживаем только stored и deflate |
| ни одна запись не совпала по имени | список файлов ствола разошёлся с архивом |
| любое исключение или обрыв | сеть; таймаут клиента - 3 минуты |

## Кеш pack.zip по SHA-256

Ключ кеша - не id пака, а его sha256 из каталога: `cache\packzips\{sha256}.zip`. Два следствия. Два пака с побайтно одинаковым содержимым делят одно место на диске. И перезалитый пак с тем же id, но новыми байтами, получает новую запись кеша - инвалидировать старую руками не нужно.

На попадании мы не верим самому факту наличия файла: сверяем размер с `pack_zip_size` из каталога и пересчитываем sha. Не совпало - качаем заново.

```cs title="MiamiGraphics.Shell/Services/PackZipCache.cs"
var sizeOk = !expectedSize.HasValue || new FileInfo(dst).Length == expectedSize.Value;
var actual = sizeOk ? await Task.Run(() => ComputeFileSha256(dst), ct) : null;
if (string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
{
    DataQuota.Touch(dst);   // LRU-штамп: NTFS время доступа сам не пишет
    return dst;
}
```

Пустой `expectedSha256` - отказ работать: кешировать без ключа целостности мы не будем. Лимит дискового пространства резервируется **до** первого байта, размер известен из каталога. Загрузка идёт в `{sha}.part` и переименовывается только после сверки хеша; расхождение - файл удаляется, наверх летит ошибка с первыми 16 символами ожидаемого и полученного хеша.

## Прогресс без стриминга

Ответ на `/api/gun` приходит один раз, в конце. Пока он готовится, пользователь смотрит на оверлей загрузки, и ему надо показать, что происходит. Держим отдельную карту прогресса, ключ - `pack|gun`:

```cs title="MiamiGraphics.Shell/Services/GunsmithService.cs"
private readonly ConcurrentDictionary<string, (string phase, int pct)> _progress = new();

case "/api/progress":   // чистое чтение - НЕ должно запускать материализацию
    var pr = _progress.TryGetValue(PKey(P("pack"), P("gun")), out var v) ? v : (phase: "idle", pct: 0);
    return ApiResponse.JsonOk(JsonSerializer.Serialize(new { phase = pr.phase, pct = pr.pct }, Json));
```

`/api/progress` - единственная ручка, которая не зовёт `EnsureAsync`. Позови она её, опрос прогресса сам бы запускал вторую материализацию того же ствола параллельно первой.

Редактор опрашивает её раз в 250 мс, пока висит оверлей:

```js title="MiamiGraphics.Shell/gunsmith/web/app.js"
loadPollTimer = setInterval(async () => {
  const p = await (await fetch(`/api/progress?pack=${enc(pack)}&gun=${enc(gun)}&v=${Date.now()}`)).json();
  if (p.phase === 'download')     setLoad(HG_T('load.download', { pct: p.pct || 0 }), p.pct || 0);
  else if (p.phase === 'extract') setLoad(HG_T('load.extract'), null);
  else if (p.phase === 'build')   setLoad(HG_T('load.build'), null);
}, 250);
```

Проценты есть только у фазы `download` - там знаменатель известен (сумма сжатых размеров нужных записей при Range-выборке, `Content-Length` при полной загрузке). `extract` и `build` идут без числа.

## Рабочая копия: оригиналы неприкосновенны

Всё дерево мастерской лежит под `cache\gunsmith\` (общий корень данных, переезжает вместе с кешем при смене диска):

```
cache\gunsmith\
├── extract\{pack}\{gun}\    ← оригиналы .ydr/.ytd, НЕ трогаем никогда
├── work\{pack}\{gun}\       ← рабочая копия, все правки тут
│   ├── _glass.json          ← пометки стекла
│   ├── _anim\               ← анимированная деталь
│   ├── _preview.glb         ← GLB для редактора
│   └── _preview_game.glb    ← GLB «как в игре»
├── glb\{pack}\{gun}.glb     ← кеш GLB оригинала
└── _crash\{MMdd_HHmmss}\    ← пост-мортем упавшего attach
```

Разделение даёт две вещи бесплатно: «начать заново» - это `Directory.Delete(work, recursive: true)`, а не откат правок, и повторное открытие ствола не тянет сеть, даже если предыдущая сессия его исковеркала.

Какую версию читать, решает одна функция - `ActiveDir` возвращает `work`, если папка существует, иначе `extract`. Сама копия создаётся лениво - первой же операцией записи, а не при открытии ствола:

```cs title="MiamiGraphics.Core/Gunsmith/TextureStudio.cs"
if (!Directory.Exists(work))
{
    Directory.CreateDirectory(work);
    foreach (var f in Directory.GetFiles(orig))
        if (!f.EndsWith("_summary.json", StringComparison.OrdinalIgnoreCase))
            File.Copy(f, Path.Combine(work, Path.GetFileName(f)), overwrite: true);
    // _anim приезжает вместе со скином (слот/публикация) - без копии
    // рабочая версия «теряла» аним-деталь при первом же редактировании.
    string origAnim = Path.Combine(orig, "_anim");
    if (Directory.Exists(origAnim)) { /* копируем и её */ }
}
```

Имена паков и стволов приходят из query-строки и уходят в `Path.Combine`, поэтому проверяются как простые имена папок: строка с `..`, `/`, `\` или `:` отвергается исключением.

### Атомарность записи текстуры

Текстура патчится во **всех** `.ydr`/`.ytd` ствола, где встретилось её имя: базовый и `_hi` варианты у модеров часто побайтно одинаковы, магазины нередко ссылаются на общий атлас. Раньше файлы писались по мере обработки - исключение на `_hi` оставляло базовый пропатченным, а `_hi` оригинальным, и ствол в игре расходился сам с собой. Теперь мутируем и сериализуем всё в память, и только если прошли все - пишем на диск:

```cs title="MiamiGraphics.Core/Gunsmith/TextureStudio.cs"
// Декодируем PNG ДО создания рабочей копии - если он битый, оригиналы целы.
var (bgra, w, h) = TextureCodec.PngToBgra(pngBytes);
string work = EnsureWorkCopy(pack, gun);

var pending = new List<(string Path, byte[] Bytes)>();
foreach (var file in EnumerateResourceFiles(work))
    if (MutateTexture(/* словарь этого файла */, name, bgra, w, h))
        pending.Add((file, ydr.Save()));

if (pending.Count == 0) return new ReplaceResult { Ok = false, Error = ... };
foreach (var (path, bytes) in pending) File.WriteAllBytes(path, bytes);
```

Порядок обхода файлов тоже не произволен: `_hi`-вариант идёт первым, дальше по алфавиту. Ванильные словари Rockstar называются через плюс (`w_x+hi.ytd`), модерские - через подчёркивание (`w_x_hi.ytd`), и проверять надо оба. Пока проверялось только `_hi`, в GLB (слияние last-wins) побеждала базовая низкодетальная текстура, а холст рисования строился из `+hi` - и ствол менял вид скачком на первом же мазке.

Стекло правит ресурсы иначе - недеструктивно. `SetGlass` создаёт рабочую копию, но пишет в неё только `_glass.json`; сам трансформ (`bucket` → 1 и альфа в диффузе) накладывается на лету в `BuildInstallFiles` в момент установки. Сами `.ydr`/`.ytd` при этом не меняются, поэтому снять стекло - это удалить запись из json, а не откатить файлы.

## Сборка превью-GLB

GLB собирается из `.ydr` + всех `.ytd` ствола тем же конвертером, что и превью каталога. Файлов превью три, и они разные по смыслу.

| Файл | Что внутри | Кто отдаёт |
|---|---|---|
| `glb\{pack}\{gun}.glb` | оригинал, собирается один раз | `/api/glb`, пока нет рабочей копии |
| `work\…\_preview.glb` | рабочая копия, **голая** геометрия | `/api/glb`, когда рабочая копия есть |
| `work\…\_preview_game.glb` | рабочая копия со стеклом и аним-деталью | сохранение и публикация скина |

Голое превью для редактора - осознанный выбор. Редактор сам накладывает стекло на материалы в three.js (ползунок прозрачности обязан отзываться мгновенно, без похода в C#) и сам подгружает аним-деталь отдельным GLB. Отдай ему запечённую версию - прозрачность умножится дважды (0.1 × 0.1, ствол почти исчезнет), а деталь нарисуется в двух экземплярах.

«Игровая» версия нужна ровно там, где ствол смотрят чужие люди: в каталоге и на картинке-превью он должен выглядеть так же, как в бою. Пока туда уезжал редакторский GLB, приходили репорты «в игре есть, в 3D-просмотре нет».

### Когда пересобирается

Первым условием было просто «файла нет». Из-за этого превью обновлялось только из `ReplaceTexture`: смена стекла и генерация аним-детали его не трогали, и в 3D-просмотре ствол оставался голым, хотя в игре был со стеклом и деталью. Сейчас сравниваем время записи GLB со **всем**, что на него влияет:

```cs title="MiamiGraphics.Core/Gunsmith/TextureStudio.cs"
if (!File.Exists(glbPath)) return true;
var glbTime = File.GetLastWriteTimeUtc(glbPath);

foreach (var f in EnumerateResourceFiles(workDir))
    if (File.GetLastWriteTimeUtc(f) > glbTime) return true;

// Стекло и деталь влияют только на «игровое» превью - редакторское от них
// не зависит, и гонять из-за ползунка прозрачности тяжёлую пересборку
// голого GLB незачем.
if (!includeGameLayers) return false;

string glass = Path.Combine(workDir, "_glass.json");
if (File.Exists(glass) && File.GetLastWriteTimeUtc(glass) > glbTime) return true;

foreach (var f in Directory.GetFiles(Path.Combine(workDir, "_anim")))
    if (File.GetLastWriteTimeUtc(f) > glbTime) return true;

return false;
```

Итого два триггера пересборки:

- **Сразу, синхронно** - после операций, меняющих геометрию или пиксели: `ReplaceTexture`, `Attach`, `AttachRawMesh`, `RemoveAccessory`. Каждая заканчивается `BuildGlb(work, _preview.glb)`, и следующий `/api/glb` уже застаёт готовый файл.
- **Лениво, по обращению** - `/api/glb` и путь сохранения проверяют `IsPreviewStale` и собирают, только если что-то из входных файлов новее превью.

Смена стекла не пересобирает ничего: она меняет `_glass.json`, редактор двигает прозрачность у себя в three.js, а `_preview_game.glb` пересоберётся при следующем сохранении - там `includeGameLayers` включён.

Сборка «игрового» превью не портит рабочую копию: стекло применяется к копиям файлов во временной папке, оттуда конвертер и читает.

## Протокол postMessage

Редактор живёт на `gunsmith.…`, оболочка - на `app.…`. Это разные origin, прямого доступа к DOM друг друга у них нет, и единственный канал - `postMessage`. `chrome.webview` редактор не трогает вообще: с C# он говорит только через `fetch` на свой собственный origin, с лаунчером - только сообщениями. Граница получается ровная - её видно по одному полю `source`.

Редактор → оболочка, `{source: 'gunsmith', type, ...}`:

| `type` | Поля | Что делает оболочка |
|---|---|---|
| `ready` | - | снимает лоадер, показывает iframe |
| `applied` | `gun` | скин поставлен в игру |
| `published` | `id`, `name` | перечитывает каталог и закрывает мастерскую |
| `ownpack-saved` | `packId`, `packName`, `gunCount`, `gunCap`, `gunId` | тянет свежий список паков, потом отвечает `ownpack-finish` |
| `flow-cancel` | - | закрывает оверлей, шаг флоу остаётся под ним |
| `close` | - | закрывает мастерскую |

Оболочка → редактор, `{source: 'hg-host', type, ...}`:

| `type` | Поля | Что делает редактор |
|---|---|---|
| `open` | `flow`, `session`, `pack`, `gun`, `packName`, `gunName`, `ownPackId`, `ownPackName`, `lang` | перестраивает нижнюю панель под флоу и грузит ствол |
| `lang` | `lang` | переключает свою локализацию |
| `ownpack-finish` | - | убирает лоадер с галочки успеха |

```js title="MiamiGraphics.Shell/gunsmith/web/app.js"
const postToHost = (type, extra) => {
  try { parent.postMessage({ source: 'gunsmith', type, ...extra }, '*'); } catch {}
};

window.addEventListener('message', (e) => {
  const d = e.data;
  if (!d || d.source !== 'hg-host') return;
  if (d.type === 'lang' || d.lang) HG_SETLANG(d.lang);
  if (d.type === 'lang') return;
  if (d.type === 'open') {
    FLOW.mode = d.flow || ''; FLOW.session = d.session || ''; FLOW.gun = d.gun || '';
    applyFlowChrome();
    if (d.pack && d.gun) loadGun(d.pack, d.gun, d.packName || d.pack, d.gunName || d.gun);
  }
});
```

### Почему параметры не едут в src

Часть контекста редактор всё-таки получает query-строкой: `owner`, `ownerName`, `lang` и, для правки существующего скина, `pack=_custom&gun={id}`. Но команда «открой такой-то ствол в таком-то флоу» приходит только сообщением. Причина - прогрев.

Мастерская - тяжёлый iframe: three.js, каталог на 2000+ стволов, веб-шрифты. Оболочка держит один экземпляр смонтированным всё время работы лаунчера, скрытым под `opacity-0` и `pointer-events-none`, задолго до того, как пользователь до неё дойдёт. К моменту показа он полностью прогрет. Любое изменение `src` перезагрузило бы iframe и обнулило прогрев, поэтому `src` считается один раз и в зависимостях `useMemo` нет ни языка, ни выбранного ствола.

```tsx title="MiamiGraphics.Shell/ui/src/screens/guns/workshop/WorkshopScreen.tsx"
const src = useMemo(() => {
  const p = new URLSearchParams();
  ...
  p.set('lang', i18n.language);
  if (req.customGunId) { p.set('pack', '_custom'); p.set('gun', req.customGunId); }
  return `https://gunsmith.huntergraphics.local/index.html?${p.toString()}`;
}, [req.customGunId]);   // ни языка, ни выбранного ствола в зависимостях
```

Отсюда же дедупликация отправки: ключ последней команды `open` хранит `flow|pack|gun|session`, и один и тот же запрос не уезжает повторно на каждый ре-рендер. Открытие без флоу шлёт `open` с пустым `flow` - иначе у редактора залипал бы режим «свой ганпак» с прошлого захода, с кнопками «Готово»/«Отменить» вместо обычных.

### Показ по 'ready'

Редактор загружается и раскладывает сетку около секунды. Показанный сразу, он даёт видимый reflow из узкой раскладки в широкую. Поэтому iframe висит прозрачным под лоадером, пока не придёт `ready` - а его редактор шлёт через **два** `requestAnimationFrame` после отрисовки каталога, чтобы первый показанный кадр был уже финальной раскладкой:

```js title="MiamiGraphics.Shell/gunsmith/web/app.js"
requestAnimationFrame(() => requestAnimationFrame(() => postToHost('ready')));
```

Страховка на случай, если `ready` не придёт вовсе: `onLoad` iframe взводит таймер на 1400 мс, который показывает редактор принудительно. Лучше увидеть reflow, чем вечный лоадер.
