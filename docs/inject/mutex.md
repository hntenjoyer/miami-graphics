# UpdateRpfMutex

Глобальный `SemaphoreSlim(1, 1)` который гарантирует что только **один** install pipeline трогает `update.rpf` в каждый момент времени.

## Реализация

```cs title="MiamiGraphics.Shell/Services/UpdateRpfMutex.cs"
public static class UpdateRpfMutex
{
    private static readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromMinutes(10);
    private static string? _currentHolder;
    private static readonly object _holderLock = new();

    public static async Task<IDisposable> AcquireAsync(string holderName, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var entered = await _mutex.WaitAsync(AcquireTimeout, ct);
        if (!entered)
        {
            string? current;
            lock (_holderLock) current = _currentHolder;
            throw new TimeoutException(
                $"UpdateRpfMutex acquire timeout ({AcquireTimeout.TotalMinutes:F0} min). " +
                $"Current holder: {current ?? "<unknown>"}. Requested by: {holderName}.");
        }
        lock (_holderLock) _currentHolder = holderName;
        Debug.WriteLine($"[update.rpf] mutex ACQUIRED by '{holderName}' (waited {sw.ElapsedMilliseconds}ms)");
        return new Releaser(holderName);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _holder;
        private int _disposed;

        public Releaser(string holder) { _holder = holder; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_holderLock) { if (_currentHolder == _holder) _currentHolder = null; }
            try { _mutex.Release(); } catch { }
            Debug.WriteLine($"[update.rpf] mutex RELEASED by '{_holder}'");
        }
    }
}
```

## Использование

Каждый bridge handler который трогает `update.rpf` обёрнут в `using`:

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
public async Task<InjectResultDto> ReduxInstallAsync(string reduxId, Guid? versionId)
{
    using var _mtx = await UpdateRpfMutex.AcquireAsync($"redux-install:{reduxId}");
    return await ReduxInstallInternalAsync(reduxId, versionId, force: false);
}

public async Task<InjectResultDto> ArmorLibraryInstallAsync(string armorLibraryId, ...)
{
    using var _mtx = await UpdateRpfMutex.AcquireAsync($"armor-library-install:{armorLibraryId}");
    // ...
}

public async Task<InjectResultDto> GunpackInstallAllAsync(string gunpackId, ...)
{
    using var _mtx = await UpdateRpfMutex.AcquireAsync($"gunpack-install-all:{gunpackId}");
    // ...
}

// и так далее - armor / reticle / minimap / sound / hnt-code
```

10 endpoints обёрнуто. Все они трогают `update.rpf`. Если хоть один забыть - баг.

## Зачем нужен - реальный кейс

Юзер сидит на `/redux` экране и кликает «Установить» на Kukanka Redux. Процесс start'ует:

1. Preflight → SHA = clean → ОК сразу инжект.
2. Качаем 200 МБ patch.zip... (60 секунд)
3. Юзер думает «долго» и кликает «Установить» на Allegri V3 в другой вкладке.
4. Без mutex: второй pipeline стартует параллельно. Preflight для Allegri тоже видит SHA = clean (потому что первый ещё не доехал до Smart Rebuild). Качает свой patch.zip.
5. Первый заканчивает Smart Rebuild → новый `update.rpf` это Kukanka.
6. Второй заканчивает Smart Rebuild → новый `update.rpf` это Allegri.
7. Финальный файл это Allegri **без Kukanka внутри**, но в `install_state.json` написано что Kukanka установлен.
8. Юзер запускает GTA, видит Allegri (а не Kukanka которую кликал первой).

С mutex'ом:

1. Первый Kukanka acquire'нул.
2. Второй Allegri ждёт на `_mutex.WaitAsync(...)`.
3. UI кнопки Allegri показывает spinner - `installing` state, юзер видит «уже в процессе».
4. Через минуту Kukanka заканчивает, mutex освобождается.
5. Allegri начинается, делает свой preflight, видит `matchesLast = Kukanka SHA`. **Auto-restore clean**, потом инжектит Allegri.
6. Финал: Allegri установлен, в `install_state.json` Allegri SHA. Корректно.

## Timeout 10 минут

Если install pipeline по какой-то причине завис (network freeze, антивирус ест 100% CPU и pause'нул процесс) - после 10 минут все другие install'ы получат `TimeoutException`.

```cs
throw new TimeoutException(
    $"UpdateRpfMutex acquire timeout (10 min). " +
    $"Current holder: {_currentHolder}. Requested by: {holderName}.");
```

`_currentHolder` в сообщении даёт диагностику: «mutex держит redux-install:abc-uuid». Юзер в UI видит ошибку с указанием **что** именно зависло.

10 минут - компромисс. Меньше - повышенный риск ложных timeout'ов на медленных машинах. Больше - юзер сидит и не понимает почему вторая установка не идёт.

## Identity check на Release

В `Releaser.Dispose` есть тонкость:

```cs
lock (_holderLock) { if (_currentHolder == _holder) _currentHolder = null; }
```

Только **сбрасываем** holder name если он наш. Зачем - защита от race condition в логировании. Без identity check:

1. Поток A `AcquireAsync` → `_currentHolder = "redux-install:X"`.
2. Поток A `Dispose` (например через double-Dispose из-за бага) → `_currentHolder = null` + `_mutex.Release()`.
3. Между Release и реальным переходом на поток B, поток B `AcquireAsync` → новый `_currentHolder = "armor-install:Y"`.
4. Поток A в `Dispose` доходит до второй части `_currentHolder = null` → **затирает** имя B.
5. Timeout error в логах: «Current holder: <unknown>», хотя на самом деле его держит B.

С identity check этого не происходит - A не сможет затереть B потому что `_currentHolder != A._holder`.

## Не reentrant

`SemaphoreSlim(1, 1)` - это **не reentrant**. Если внутри обёрнутого endpoint'а **снова** вызвать другой обёрнутый - будет deadlock:

```cs
public async Task<InjectResultDto> ReduxInstallAsync(...)
{
    using var _mtx = await UpdateRpfMutex.AcquireAsync("redux-install");

    // плохо: вложенный вызов снова acquire'нет, deadlock
    var result = await ArmorLibraryInstallAsync(...);
}
```

В нашем коде это не происходит - мы **никогда** не вызываем другой обёрнутый bridge endpoint из тела другого. Все рекурсивные операции (например `customize.apply` который собирает компоненты из разных мест) делают это **в одном потоке** через прямые вызовы Core методов, не через bridge.

## Что было до mutex

Несколько релизов работали без него. Юзеры сообщали о «странных» поведениях - иногда после установки A → B, в `update.rpf` оказывалась смесь, иногда `install_state.json` был врёт про текущий редукс. Не часто, но достаточно чтобы попасть в bug-report'ах.

Mutex добавили после backup-audit'а где явно прописали что **отсутствие mutex на критическом ресурсе = баг**. С тех пор «двойной клик» работает предсказуемо - второй клик ждёт первый.

[Дальше: Backup pipeline →](../backup/pipeline.md)
