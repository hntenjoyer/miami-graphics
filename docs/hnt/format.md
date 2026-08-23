# Формат и payload HNT

## Формат кода

```
HNT-XXXXXX
```

- Префикс `HNT-` - для распознавания в текстах. Юзер видит «вот HNT-K7P2X9» в Discord, лаунчер при импорте принимает с/без префикса (`K7P2X9` тоже валидно).
- 6 символов алфавит **upper-case без визуально похожих** - `A-Z` минус `O, I, L`, плюс `2-9` минус `0, 1`. Это даёт алфавит из 30 символов, итого `30^6 ≈ 729M` комбинаций.
- Регистро-независимо при вводе (нормализуется в upper).

Сжатие до 6 символов - это compromise. 4 было бы 810K вариантов (мало, коллизии при 100K кодах). 8 - длинно и неудобно в voice chat. 6 - sweet spot для 10-100K кодов в системе.

## Генерация уникального

```sql title="generate_hnt_code() RPC на Supabase"
create or replace function generate_hnt_code() returns text as $$
declare
  alphabet  text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';   -- 30 символов
  code      text;
  attempts  int := 0;
begin
  loop
    code := 'HNT-' ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1) ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1) ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1) ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1) ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1) ||
      substr(alphabet, 1 + floor(random() * 30)::int, 1);

    perform 1 from hnt_codes where hnt_codes.code = generate_hnt_code.code;
    if not found then return code; end if;

    attempts := attempts + 1;
    if attempts > 100 then raise exception 'cannot generate unique hnt code'; end if;
  end loop;
end;
$$ language plpgsql;
```

В худшем случае при заполнении 99% пространства гены 100 попыток будет мало - но мы далеки от этого. При текущей загрузке (1-2K кодов) коллизия практически невозможна.

## Payload JSON

Пример full payload:

```json
{
  "reduxId": "8fa1e2c3-...",
  "reduxVersionId": "12cba34-...",
  "reduxName": "Hunter Reborn 4.0",
  "reduxAuthor": "Hunter",
  "gunpackId": "55ec1ba2-...",
  "gunpackName": "FiveM Realism Pack",
  "selectedGuns": [
    {
      "gunpackId": "aa11bb-...",
      "gunpackName": "Sniper Collection",
      "internalName": "WEAPON_SNIPERRIFLE",
      "displayName": "Tactical AWP"
    }
  ],
  "extras": {
    "customizeDraft": {
      "baseReduxId": "8fa1e2c3-...",
      "baseVersionId": "12cba34-...",
      "componentOverrides": {
        "minimap": {
          "donorReduxId": "diff-redux-uuid",
          "donorVersionId": "...",
          "settings": { "hpColor": "#FF0000" }
        }
      }
    }
  }
}
```

## Selective export

При export'е юзер может **снять** галки с любого блока через UI:

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
public async Task<HntCodeDto> HntCodeExportAsync(
    string userId,
    bool includeRedux,
    bool includeGunpack,
    bool includeSelectedGuns)
{
    var payload = await CaptureHntPayloadAsync();

    if (!includeRedux)
        payload = payload with { ReduxId = null, ReduxVersionId = null,
                                  ReduxName = null, ReduxAuthor = null, Extras = null };
    if (!includeGunpack)
        payload = payload with { GunpackId = null, GunpackName = null };
    if (!includeSelectedGuns)
        payload = payload with { SelectedGuns = new List<HntSelectedGunDto>() };

    var payloadElement = JsonSerializer.SerializeToElement(payload, _hntJsonOpts);
    var row = await _hntCodesRepo.ExportAsync(userId, payloadElement);

    Debug.WriteLine($"[hnt.export] code={row.Code} user={userId} " +
                    $"mask=R{includeRedux}/G{includeGunpack}/S{includeSelectedGuns} " +
                    $"payloadBytes={payloadElement.GetRawText().Length}");
    return ToHntCodeDto(row);
}
```

Отключённый блок просто **обнуляется** в payload - на стороне получателя `HntCodeApplyAsync` пропустит этот шаг.

## Capture текущего state

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs"
private async Task<HntPayloadDto> CaptureHntPayloadAsync()
{
    var state = ReadInstallState();    // .miamigraphics/install-state.json

    string? reduxName = null, reduxAuthor = null;
    if (state is not null && !string.IsNullOrWhiteSpace(state.ReduxId))
    {
        try
        {
            var item = await _catalog.GetByIdAsync(state.ReduxId);
            reduxName = item?.Name;
            reduxAuthor = item?.Author;
        }
        catch (Exception ex) { Debug.WriteLine($"[hnt.export] catalog lookup failed: {ex.Message}"); }
    }

    var gunpackState = await _gunpackInstaller.GetInstalledStateAsync();
    var selectedRows = await _selectedGunsInstaller.ListInstalledAsync();
    var selectedGuns = selectedRows.Select(g => new HntSelectedGunDto(
        g.GunpackId, g.GunpackName, g.InternalName, g.DisplayName)).ToList();

    JsonElement? extras = null;
    if (state?.CustomizationDraft is not null)
        extras = JsonSerializer.SerializeToElement(
            new { customizeDraft = state.CustomizationDraft }, _hntJsonOpts);

    return new HntPayloadDto(
        ReduxId:        state?.ReduxId,
        ReduxVersionId: state?.VersionId,
        ReduxName:      reduxName,
        ReduxAuthor:    reduxAuthor,
        GunpackId:      gunpackState.ActiveGunpackId,
        GunpackName:    gunpackState.ActiveGunpackName,
        SelectedGuns:   selectedGuns,
        Extras:         extras);
}
```

Источники state'а:

| Поле | Откуда читаем |
|---|---|
| `ReduxId`, `VersionId`, `CustomizationDraft` | `%LocalAppData%\MiamiGraphics\install-state.json` |
| `ReduxName`, `Author` | Lookup в Supabase по `ReduxId` (для preview UI) |
| `GunpackId`, `GunpackName` | `gunpack-state.json` через `_gunpackInstaller` |
| `SelectedGuns` | `selected-guns-state.json` |

Если юзер ставил redux **руками** (не через лаунчер) - `install-state.json` отсутствует, capture вернёт пустой payload. Это нормально, не ошибка, просто кодом обмениваться нечего.

## Размер payload'а

Типичный payload:
- Полный (redux + gunpack + 5 selected guns + customize draft) - **~3-5 KB** JSON.
- Минимальный (только reduxId) - **~200 байт**.

Лимит на стороне Supabase - `jsonb` без ограничений до 1 GB. Лимита в нашем коде нет, но через UI юзер не сможет напихать больше 50 selected guns (UI лимит).

[Дальше: Импорт и применение →](import-apply.md)
