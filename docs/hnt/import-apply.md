# Импорт и применение HNT-кода

![Модал импорта HNT - ввод кода HNT-AQS7-MFVJ + кнопка «Найти»](../assets/screenshots/screen-057.png)

## Два шага: Preview → Apply

Юзер вбивает код в модал. Сначала **preview** (не применяя), потом если согласен - **apply** (тяжёлый install pipeline).

### Preview

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
public async Task<HntCodeDto> HntCodePreviewAsync(string code)
{
    try
    {
        var row = await _hntCodesRepo.ImportAsync(code);
        return ToHntCodeDto(row);
    }
    catch (SupabaseException ex) when (ex.Message.Contains("code_not_found"))
    {
        throw new InvalidOperationException("HNT_CODE_NOT_FOUND");
    }
    catch (SupabaseException ex) when (ex.Message.Contains("code_expired"))
    {
        throw new InvalidOperationException("HNT_CODE_EXPIRED");
    }
}
```

`_hntCodesRepo.ImportAsync` вызывает Supabase RPC:

```sql
create function import_hnt_code(p_code text) returns hnt_codes as $$
declare
  row hnt_codes;
begin
  select * into row from hnt_codes where code = p_code;
  if not found then raise exception 'code_not_found'; end if;
  if row.expires_at is not null and row.expires_at < now() then
    raise exception 'code_expired';
  end if;

  -- инкремент счётчика загрузок
  update hnt_codes set
    downloads_count = downloads_count + 1,
    last_downloaded_at = now()
  where code = p_code
  returning * into row;

  return row;
end;
$$ language plpgsql security definer;
```

Preview-модал в UI показывает:

- Какой redux + author + версия (если есть).
- Какой gunpack (если есть).
- Сколько selected guns (если есть).
- **Есть ли кастомизация** - если да, ещё раскрывается список «minimap from X, tracers from Y».
- Estimated размер download'а.

Юзер видит full picture и кликает «Apply» или «Cancel».

### Apply - последовательный install

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
public async Task<HntImportResultDto> HntCodeApplyAsync(HntPayloadDto payload)
{
    if (payload is null)
        return new HntImportResultDto(false, "Payload is null.", null, null, null);

    using var _mtx = await UpdateRpfMutex.AcquireAsync("hnt-apply");

    HntInstallStepResultDto reduxStep        = new(true, true, null);
    HntInstallStepResultDto gunpackStep      = new(true, true, null);
    HntInstallStepResultDto selectedGunsStep = new(true, true, null);

    // === STEP 1: Redux ===
    if (!string.IsNullOrWhiteSpace(payload.ReduxId))
    {
        try
        {
            var customizeDraft = ExtractCustomizeDraft(payload);
            InjectResultDto r;
            if (customizeDraft is not null)
            {
                // Если в коде есть customize, применяем customize-flow
                var draftWithVersion = customizeDraft with { BaseVersionId = payload.ReduxVersionId };
                r = await ReduxCustomizeApplyAsync(payload.ReduxId, draftWithVersion);
            }
            else
            {
                // Обычный install
                r = await ReduxInstallAsync(payload.ReduxId, payload.ReduxVersionId);
            }
            reduxStep = new HntInstallStepResultDto(
                Skipped: false, Success: r.Success,
                ErrorMessage: r.Success ? null : r.ErrorMessage);

            if (!r.Success)
            {
                // Redux упал - НЕ продолжаем дальше (gunpack/guns не имеют смысла)
                return new HntImportResultDto(
                    false, r.ErrorMessage, reduxStep,
                    new HntInstallStepResultDto(true, true, null),
                    new HntInstallStepResultDto(true, true, null));
            }
        }
        catch (Exception ex)
        {
            return new HntImportResultDto(false, ex.Message,
                new HntInstallStepResultDto(false, false, ex.Message),
                new HntInstallStepResultDto(true, true, null),
                new HntInstallStepResultDto(true, true, null));
        }
    }

    // === STEP 2: Gunpack ===
    if (!string.IsNullOrWhiteSpace(payload.GunpackId))
    {
        try
        {
            var r = await GunpackInstallAllAsync(payload.GunpackId);
            gunpackStep = new HntInstallStepResultDto(
                Skipped: false, Success: r.Success,
                ErrorMessage: r.Success ? null : r.ErrorMessage);
        }
        catch (Exception ex)
        {
            gunpackStep = new HntInstallStepResultDto(false, false, ex.Message);
        }
    }

    // === STEP 3: Selected guns ===
    if (payload.SelectedGuns is { Count: > 0 })
    {
        int okCount = 0;
        var failures = new List<string>();
        foreach (var g in payload.SelectedGuns)
        {
            try
            {
                var r = await SelectedGunsInstallAsync(g.GunpackId, g.InternalName);
                if (r.Success) okCount++;
                else failures.Add($"{g.DisplayName}: {r.ErrorMessage ?? "unknown"}");
            }
            catch (Exception ex)
            {
                failures.Add($"{g.DisplayName}: {ex.Message}");
            }
        }
        selectedGunsStep = new HntInstallStepResultDto(
            Skipped: false,
            Success: failures.Count == 0,
            ErrorMessage: failures.Count == 0 ? null
                : $"Установлено {okCount} из {payload.SelectedGuns.Count}. " +
                  $"Не удалось: {string.Join("; ", failures)}");
    }

    var overallSuccess = reduxStep.Success && gunpackStep.Success && selectedGunsStep.Success;
    return new HntImportResultDto(
        overallSuccess,
        overallSuccess ? null
            : reduxStep.ErrorMessage ?? gunpackStep.ErrorMessage ?? selectedGunsStep.ErrorMessage,
        reduxStep, gunpackStep, selectedGunsStep);
}
```

## Partial success

В UI результат показывается **per-step**:

- ✅ Redux: «Hunter Reborn 4.0» - установлен.
- ✅ Gunpack: «FiveM Realism Pack» - установлен.
- ⚠️ Selected guns: 4 из 5. «Tactical AWP - недоступен в каталоге».

Юзер видит **что сработало**, что нет. Это лучше чем «installation failed» на весь HNT.

## UpdateRpfMutex

Apply держит **глобальный** [UpdateRpfMutex](../inject/mutex.md) на всё время - redux + gunpack + selected guns могут идти 30 минут на медленной сети, но никакая другая install/customize не запустится в это время.

Это **намеренно**: если юзер случайно жмёт «применить пресет минимапы» пока HNT-apply ставит redux, у него получится мешанина. Mutex blocks этот race.

## Customize-aware

Если в payload `extras.customizeDraft` есть - мы вызываем **customize**-pipeline, а не обычный install:

```cs
var customizeDraft = ExtractCustomizeDraft(payload);
if (customizeDraft is not null)
{
    var draftWithVersion = customizeDraft with { BaseVersionId = payload.ReduxVersionId };
    r = await ReduxCustomizeApplyAsync(payload.ReduxId, draftWithVersion);
}
```

Это значит юзер на принимающей стороне получит **точно ту же** кастомизацию что и отправитель: тот же donor-redux для minimap, тот же RGB-цвет HP-бара. Воспроизводимость 1:1.

## Edge cases

### Redux удалён из каталога

`ReduxInstallAsync` бросает «redux not found». Step фейлится с message «Этот мод больше недоступен в каталоге». UI предлагает попробовать **похожий** мод (по author или tags) из текущего каталога.

### Версия удалена, но redux остался

`ReduxVersionId` указывает на удалённую row. `ReduxInstallAsync` падает с «version not found». UI предлагает «использовать **последнюю** версию этого мода» - fallback to `GetLatestVersionAsync(reduxId)`.

### Gunpack удалён

Skip с warning. Redux всё равно остаётся, юзер не теряет всё.

### Selected gun из удалённого gunpack'а

Skip единичной пушки, обработка идёт дальше. В summary помечается «N не установлено».

## Где UI

Импорт UI - модал `HntImportModal.tsx`. Прямо на главной странице кнопка «Импорт HNT». При открытии - поле ввода, кнопка [Preview]. После preview раскрывается секция с details и кнопкой [Apply]. Прогресс отображается по шагам через стандартный [progress stream](../inject/pipeline.md).

Это конец секции HNT-кодов.
