import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import {
  Gauge, RefreshCw, Cpu, MemoryStick, HardDrive, MonitorCog, Eye,
  AlertTriangle, AlertOctagon, Info, CircleAlert, Zap, Layers3,
  ArrowRight, Wrench, Settings2,
} from 'lucide-react';
import { bridge } from '@/bridge';
import type { PcDiagFinding, PcDiagTweak } from '@/bridge/IAppBridge';
import { EASE_DEPTH } from '@/design';
import { GlassPanel } from '@/design/primitives/GlassPanel';
import { AccentLoader } from '@/design/primitives/AccentLoader';
import { ScreenHero } from '@/screens/ScreenHero';
import { useNavStore } from '@/store/navStore';
import { readCache, writeCache, clearCache } from '@/store/catalogCache';
import { usePcDiagStore } from '@/store/pcDiagStore';
import { useSessionStore, useCanSeeTesterFeature } from '@/store/sessionStore';

function AiText({ text, className = 'text-[15px] leading-[1.7]' }: { text: string; className?: string }) {
  const parts = text.split(/\*\*(.+?)\*\*/g);
  return (
    <div className={className + ' whitespace-pre-wrap'}>
      {parts.map((p, i) => (i % 2 === 1 ? <b key={i} className="font-semibold text-text-primary">{p}</b> : <span key={i}>{p}</span>))}
    </div>
  );
}

function aiErrorText(t: TFunction, code: string): string {
  switch (code) {
    case 'daily_limit': return t('pcdiag.ai.limit', 'Дневной лимит запросов к ИИ исчерпан. Завтра счётчик обнулится.');
    case 'ai_not_configured': return t('pcdiag.ai.notConfigured', 'ИИ временно недоступен: сервер не настроен.');
    case 'guest': return t('pcdiag.ai.guest', 'Разбор ИИ доступен после входа в аккаунт.');
    default: return t('pcdiag.ai.failed', 'ИИ не ответил. Попробуйте ещё раз через минуту.');
  }
}

function Island({ className = '', children }: { className?: string; children: ReactNode }) {
  return (
    <GlassPanel
      depth="z2" tint="ultra" rounded="2xl" highlight edge
      className={'relative overflow-hidden border border-white/[0.08] ' + className}
    >
      <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
      <div className="relative h-full">{children}</div>
    </GlassPanel>
  );
}

function describeFinding(t: TFunction, f: PcDiagFinding): { title: string; body: string } {
  const d = f.data;
  switch (f.id) {
    case 'cpu-tier-s': return {
      title: t('pcdiag.f.cpuTierS.title', 'Процессор класса S для GTA RP'),
      body: t('pcdiag.f.cpuTierS.body', '{{family}}, L3 {{l3Mb}} МБ: лучший вариант для RP, лимитировать будет что-то другое.', { family: d.family, l3Mb: d.l3Mb }),
    };
    case 'cpu-tier-c': return {
      title: t('pcdiag.f.cpuTierC.title', 'Процессор: потолок ощущается'),
      body: t('pcdiag.f.cpuTierC.body', '{{family}}: в центре города при полном сервере просадки неизбежны. Настройки добавят 10-20%, дальше потолок задаёт сам процессор.', { family: d.family }),
    };
    case 'cpu-tier-d': return {
      title: t('pcdiag.f.cpuTierD.title', 'Процессор: главный лимитер системы'),
      body: t('pcdiag.f.cpuTierD.body', '{{family}}: апгрейд процессора даст больше кадров, чем все твики вместе взятые.', { family: d.family }),
    };
    case 'cpu-hybrid': return {
      title: t('pcdiag.f.cpuHybrid.title', 'Гибридный процессор (P+E ядра)'),
      body: t('pcdiag.f.cpuHybrid.body', 'Старый движок GTA иногда ловит статтер от попадания потоков на E-ядра. Привязка игры к P-ядрам может помочь; проверяется замером до и после на конкретной машине.'),
    };
    case 'cpu-unrecognized': return {
      title: t('pcdiag.f.cpuUnknown.title', 'Процессор не распознан'),
      body: t('pcdiag.f.cpuUnknown.body', '{{name}}: тир уточнится после обновления базы процессоров.', { name: d.name }),
    };
    case 'ram-critical': return {
      title: t('pcdiag.f.ramCritical.title', 'Оперативной памяти критически мало'),
      body: t('pcdiag.f.ramCritical.body', 'Всего {{totalGb}} ГБ: RP-клиент с интерфейсами ест 8-12 ГБ, фризы неизбежны. 16 ГБ это минимум для RP.', { totalGb: d.totalGb }),
    };
    case 'ram-low': return {
      title: t('pcdiag.f.ramLow.title', 'Оперативной памяти впритык'),
      body: t('pcdiag.f.ramLow.body', 'Всего {{totalGb}} ГБ: на игру хватает, но фоновый браузер добьёт остаток и начнутся фризы стриминга.', { totalGb: d.totalGb }),
    };
    case 'ram-xmp-off': return {
      title: t('pcdiag.f.xmpOff.title', 'Память работает медленнее паспорта: XMP выключен'),
      body: t('pcdiag.f.xmpOff.body', 'Фактически {{actual}} МТ/с при паспортных {{rated}}. Профиль XMP/EXPO включается в BIOS за минуту и это самый крупный бесплатный прирост на этой машине.', { actual: d.actual, rated: d.rated }),
    };
    case 'ram-jedec-speed': return {
      title: t('pcdiag.f.jedec.title', 'Память на стартовой частоте'),
      body: t('pcdiag.f.jedec.body', '{{gen}} работает на {{actual}} МТ/с. Возможно, у планок есть профиль быстрее, который система не показывает: стоит проверить XMP в BIOS.', { gen: d.gen, actual: d.actual }),
    };
    case 'ram-single-channel': return {
      title: t('pcdiag.f.singleChannel.title', 'Память в одноканальном режиме'),
      body: t('pcdiag.f.singleChannel.body', 'Пропускная способность вдвое ниже возможной. Вторая планка или правильные слоты дают двузначный прирост кадров в CPU-сценах.'),
    };
    case 'disk-hdd-present': return d.hasSsd === '1' ? {
      title: t('pcdiag.f.hddSome.title', 'В системе есть HDD'),
      body: t('pcdiag.f.hddSome.body', '{{model}}: проверьте, что GTA и клиент RP стоят не на нём. На HDD фризы стриминга неизбежны.', { model: d.hddModel }),
    } : {
      title: t('pcdiag.f.hddOnly.title', 'Единственный диск: HDD'),
      body: t('pcdiag.f.hddOnly.body', '{{model}}: для RP обязателен SSD. Это самый важный апгрейд, важнее видеокарты.', { model: d.hddModel }),
    };
    case 'vram-critical': return {
      title: t('pcdiag.f.vramCritical.title', 'Видеопамяти мало для RP'),
      body: t('pcdiag.f.vramCritical.body', '{{gpu}}: {{vramGb}} ГБ VRAM. Донатные машины и одежда переполнят её: текстуры на минимум, иначе фризы и пропадающие текстуры.', { gpu: d.gpu, vramGb: d.vramGb }),
    };
    case 'vram-low': return {
      title: t('pcdiag.f.vramLow.title', 'Видеопамять без запаса'),
      body: t('pcdiag.f.vramLow.body', '{{gpu}}: {{vramGb}} ГБ VRAM. Хватит, но качество текстур не выше среднего: нужен запас под донатные модели сервера.', { gpu: d.gpu, vramGb: d.vramGb }),
    };
    case 'gpu-driver-old': return {
      title: t('pcdiag.f.driverOld.title', 'Драйвер видеокарты сильно устарел'),
      body: t('pcdiag.f.driverOld.body', '{{gpu}}: драйвер не обновлялся {{months}} мес. За это время вышли оптимизации и исправления.', { gpu: d.gpu, months: d.months }),
    };
    case 'gpu-driver-aging': return {
      title: t('pcdiag.f.driverAging.title', 'Драйвер видеокарты старше года'),
      body: t('pcdiag.f.driverAging.body', '{{gpu}}: драйверу {{months}} мес., стоит обновить.', { gpu: d.gpu, months: d.months }),
    };
    case 'dual-gpu-check-render': return {
      title: t('pcdiag.f.dualGpu.title', 'Два GPU: проверить, на чём рендерит игра'),
      body: t('pcdiag.f.dualGpu.body', 'Классическая беда ноутбуков: игра запускается на встройке вместо {{dgpu}}. Проверяется в настройках графики Windows.', { dgpu: d.dgpu }),
    };
    case 'power-saver': return {
      title: t('pcdiag.f.powerSaver.title', 'Схема питания: экономия энергии'),
      body: t('pcdiag.f.powerSaver.body', '«{{scheme}}» прямо режет частоты процессора во время игры.', { scheme: d.scheme }),
    };
    case 'power-balanced': return d.laptop === '1' ? {
      title: t('pcdiag.f.powerBalancedLaptop.title', 'Схема питания душит ноутбук'),
      body: t('pcdiag.f.powerBalancedLaptop.body', '«{{scheme}}» на ноутбуке придерживает частоты CPU. Схема «Высокая производительность» на время игры даст заметный прирост.', { scheme: d.scheme }),
    } : {
      title: t('pcdiag.f.powerBalanced.title', 'Схема питания: сбалансированная'),
      body: t('pcdiag.f.powerBalanced.body', 'На десктопе эффект небольшой, но «Высокая производительность» стабильнее держит частоты в игре.'),
    };
    case 'power-custom': return {
      title: t('pcdiag.f.powerCustom.title', 'Нестандартная схема питания'),
      body: t('pcdiag.f.powerCustom.body', '«{{scheme}}»: вендорская или ручная. Стоит проверить минимальное состояние процессора в её настройках.', { scheme: d.scheme }),
    };
    case 'gamedvr-on': return {
      title: t('pcdiag.f.gamedvr.title', 'Фоновая запись Xbox Game Bar включена'),
      body: t('pcdiag.f.gamedvr.body', 'Windows пишет геймплей в фоновый буфер: энкодер и копирование кадров работают прямо во время игры. Если вы не пользуетесь повторами Game Bar, функция вам не нужна.'),
    };
    case 'vbs-running': return {
      title: t('pcdiag.f.vbs.title', 'Виртуализационная защита (VBS) запущена'),
      body: t('pcdiag.f.vbs.body', 'Отключение вернёт порядка 5% производительности, но снизит защиту системы от эксплойтов уровня ядра. Выбор остаётся за вами.'),
    };
    case 'game-on-hdd': return {
      title: t('pcdiag.f.gameHdd.title', 'GTA установлена на HDD'),
      body: t('pcdiag.f.gameHdd.body', '{{path}} лежит на жёстком диске: фризы стриминга и минуты загрузки неизбежны. Перенос на SSD это самый важный апгрейд, важнее видеокарты.', { path: d.path }),
    };
    case 'game-not-in-av-exclusions': return {
      title: t('pcdiag.f.avExcl.title', 'Папка игры не в исключениях антивируса'),
      body: t('pcdiag.f.avExcl.body', 'Антивирус на лету сканирует тысячи мелких файлов при стриминге ассетов: это фризы при загрузке. Узкое исключение на папку игры и клиента RP убирает их, почти не снижая защиту.'),
    };
    case 'pagefile-off': return {
      title: t('pcdiag.f.pagefileOff.title', 'Файл подкачки выключен'),
      body: t('pcdiag.f.pagefileOff.body', 'Народный твик, который вредит: RP-клиент с интерфейсами при пике памяти просто вылетит. Файл подкачки нужно вернуть, на SSD он ничего не замедляет.'),
    };
    case 'sysmain-off': return {
      title: t('pcdiag.f.sysmain.title', 'Служба SysMain выключена'),
      body: t('pcdiag.f.sysmain.body', 'След стороннего оптимизатора. На SSD отключение не ускоряет ничего, повторные запуски программ медленнее. На серверах с проверкой ПК выключенный SysMain вместе с Prefetch вызывает вопросы у администрации. Стоит включить обратно.'),
    };
    case 'wsearch-off': return {
      title: t('pcdiag.f.wsearch.title', 'Поиск Windows выключен'),
      body: t('pcdiag.f.wsearch.body', 'На FPS не влияет вообще. Если поиск по файлам вам нужен, верните службу; если нет, можно оставить как есть.'),
    };
    case 'bcd-useplatformclock': return {
      title: t('pcdiag.f.bcdClock.title', 'Найден вредный флаг загрузчика useplatformclock'),
      body: t('pcdiag.f.bcdClock.body', 'Флаг из старых гайдов: на современной Windows принудительный таймер HPET замедляет систему, а не ускоряет. Флаг стоит убрать.'),
    };
    case 'bcd-disabledynamictick': return {
      title: t('pcdiag.f.bcdTick.title', 'Найден флаг загрузчика disabledynamictick'),
      body: t('pcdiag.f.bcdTick.body', 'Спорный твик из гайдов: измеримой пользы на современных системах нет. Знайте, что он у вас стоит.'),
    };
    case 'display-not-max-hz': return {
      title: t('pcdiag.f.hz.title', 'Монитор работает не на максимальной герцовке'),
      body: t('pcdiag.f.hz.body', 'Сейчас {{current}} Гц, а экран умеет {{max}} на этом разрешении ({{res}}). Кадров игре это не прибавит, но плавность и отклик картинки поднимет заметно. Меняется в настройках дисплея Windows.', { current: d.current, max: d.max, res: d.res }),
    };
    case 'wifi-only': return {
      title: t('pcdiag.f.wifi.title', 'Игра через Wi-Fi'),
      body: t('pcdiag.f.wifi.body', 'На FPS не влияет, но пинг и потери пакетов на Wi-Fi всегда хуже провода: в перестрелках это рассинхрон. Подключение кабелем убирает проблему полностью.'),
    };
    case 'vpn-active': return {
      title: t('pcdiag.f.vpn.title', 'Активен VPN-туннель'),
      body: t('pcdiag.f.vpn.body', '{{adapter}}: если игровой трафик идёт через VPN, пинг до сервера едет кружным путём. FPS это не трогает, пинг может вырасти заметно.', { adapter: d.adapter }),
    };
    case 'gamemode-off': return {
      title: t('pcdiag.f.gamemodeOff.title', 'Игровой режим Windows выключен'),
      body: t('pcdiag.f.gamemodeOff.body', 'Его выключают по старым гайдам, и это миф: Game Mode даёт игре приоритет и глушит фоновые обновления. Стоит включить обратно.'),
    };
    case 'hags-state': return {
      title: d.on === '1'
        ? t('pcdiag.f.hagsOn.title', 'Аппаратное планирование GPU (HAGS) включено')
        : t('pcdiag.f.hagsOff.title', 'Аппаратное планирование GPU (HAGS) выключено'),
      body: t('pcdiag.f.hags.body', 'Результаты в GTA V Legacy расходятся: стабильного вердикта нет, разница в пределах пары процентов в обе стороны. Проверяется замером до и после на конкретной машине.'),
    };
    case 'transparency-on': return {
      title: t('pcdiag.f.transparency.title', 'Прозрачность интерфейса Windows включена'),
      body: t('pcdiag.f.transparency.body', 'Эффект от отключения: доли процента. Заметен только на очень слабых видеокартах.'),
    };
    case 'bg-browser': return {
      title: t('pcdiag.f.bgBrowser.title', 'Браузер держит память в фоне'),
      body: t('pcdiag.f.bgBrowser.body', 'Сейчас {{gb}} ГБ в {{n}} процессах. RP-клиент с интерфейсами ест 8-12 ГБ: перед игрой браузер лучше закрывать целиком, а не сворачивать.', { gb: d.gb, n: d.count }),
    };
    case 'bg-wallpaper': return {
      title: t('pcdiag.f.bgWallpaper.title', 'Wallpaper Engine работает'),
      body: t('pcdiag.f.bgWallpaper.body', 'Живые обои рисуются видеокартой постоянно, в том числе под игрой в окне без рамки. На время игры ставьте паузу (в настройках Wallpaper Engine есть автопауза при запуске игр).'),
    };
    case 'bg-torrent': return {
      title: t('pcdiag.f.bgTorrent.title', 'Торрент-клиент работает'),
      body: t('pcdiag.f.bgTorrent.body', '{{name}} нагружает диск и сеть: это прямые фризы стриминга и скачки пинга. Перед игрой закрывайте или ставьте раздачи на паузу.', { name: d.name }),
    };
    case 'bg-widgets': return {
      title: t('pcdiag.f.bgWidgets.title', 'Виджеты Windows крутятся в фоне'),
      body: t('pcdiag.f.bgWidgets.body', 'Панель виджетов держит {{gb}} ГБ памяти. Если вы ей не пользуетесь, её можно выключить в настройках панели задач.', { gb: d.gb }),
    };
    case 'bg-discord-overlay': return {
      title: t('pcdiag.f.bgDiscord.title', 'Discord запущен: проверьте оверлей'),
      body: t('pcdiag.f.bgDiscord.body', 'Сам Discord закрывать не требуется. Его игровой оверлей рисуется в каждом кадре: если вы им не пользуетесь, выключите его в настройках Discord, раздел «Оверлей».'),
    };
    case 'bg-overwolf': return {
      title: t('pcdiag.f.bgOverwolf.title', 'Overwolf работает в фоне'),
      body: t('pcdiag.f.bgOverwolf.body', 'Оверлеи Overwolf встраиваются в игру и стоят кадров. Если приложения Overwolf не нужны в GTA, закройте его перед игрой.'),
    };
    case 'autostart-crowded': return {
      title: t('pcdiag.f.autostart.title', 'В автозагрузке {{n}} программ', { n: d.count }),
      body: t('pcdiag.f.autostart.body', 'Среди них: {{sample}}. Каждая занимает память с самого старта Windows. Пройдитесь по списку в Диспетчере задач → Автозагрузка и выключите то, чем не пользуетесь ежедневно.', { sample: d.sample }),
    };
    case 'av-third-party': return {
      title: t('pcdiag.f.av3rd.title', 'Сторонний антивирус: {{names}}', { names: d.names }),
      body: t('pcdiag.f.av3rd.body', 'Удалять его не требуется. Но сканирование на лету при стриминге ассетов даёт фризы: добавьте папки GTA и клиента RP в исключения.'),
    };
    case 'prefetch-off': return {
      title: t('pcdiag.f.prefetchOff.title', 'Prefetch отключён'),
      body: t('pcdiag.f.prefetchOff.body', 'FPS это не добавляет: на SSD Windows управляет им сама. Важнее другое: администрация RP-серверов при проверке ПК читает Prefetch как журнал запусков программ, и отключённый Prefetch трактуется как сокрытие следов. Если играете на серверах с проверками, включите обратно.'),
    };
    case 'eventlog-off': return {
      title: t('pcdiag.f.eventlogOff.title', 'Журнал событий Windows отключён'),
      body: t('pcdiag.f.eventlogOff.body', 'На производительность не влияет. При проверке ПК на сервере отключённые журналы выглядят как зачистка следов и могут стоить бана; без них также сложнее разбирать вылеты игры. Включите обратно.'),
    };
    case 'game-priority-normal': return {
      title: t('pcdiag.f.prioNormal.title', 'Игра запущена с обычным приоритетом'),
      body: t('pcdiag.f.prioNormal.body', 'Приоритет «Высокий» уменьшает микрофризы, когда фоновые программы борются за процессор. На текущую сессию ставится в Диспетчере задач: Подробности → {{process}}.exe → Задать приоритет → Высокий. Realtime не ставьте: ломает звук и ввод.', { process: d.process }),
    };
    case 'game-priority-realtime': return {
      title: t('pcdiag.f.prioRt.title', 'У игры выставлен приоритет Realtime'),
      body: t('pcdiag.f.prioRt.body', 'Realtime ставит игру выше системных потоков: трещит звук, подлагивает ввод, система может зависнуть. Верните «Высокий».'),
    };
    case 'game-affinity-limited': return {
      title: t('pcdiag.f.affinity.title', 'Игре разрешены не все ядра: {{cores}} из {{total}}', { cores: d.cores, total: d.total }),
      body: t('pcdiag.f.affinity.body', 'Либо это осознанная привязка (например, к P-ядрам), либо след стороннего софта. Если вы этого не настраивали, проверьте, что ограничило маску ядер.'),
    };
    case 'device-power-savings': return {
      title: t('pcdiag.f.devPower.title', 'Устройства засыпают ради экономии энергии'),
      body: t('pcdiag.f.devPower.body', 'В активной схеме питания: {{what}}. Каждое пробуждение устройства во время игры даёт микроскачок времени кадра. Отключается только для питания от сети, на батарее экономия остаётся.', { what: d.what }),
    };
    case 'visualfx-full': return {
      title: t('pcdiag.f.visualFx.title', 'Визуальные эффекты Windows включены'),
      body: t('pcdiag.f.visualFx.body', 'Анимации и тени окон занимают немного ресурсов в фоне. Перевод в режим производительности освобождает их; сглаживание шрифтов сохраняем. Эффект небольшой, заметен на слабых машинах.'),
    };
    case 'gta-settings-headroom': return {
      title: t('pcdiag.f.gtaSettings.title', 'Настройки графики GTA: есть запас до +{{gain}}% кадров', { gain: d.gain }),
      body: t('pcdiag.f.gtaSettings.body', 'Разбор вашего settings.xml показывает, что часть настроек стоит дороже, чем выглядит. Пресет под ваше железо применяется на вкладке «Настройки игры» с резервной копией текущего файла.'),
    };
    default: return { title: f.id, body: JSON.stringify(f.data) };
  }
}

const SEVERITY_STYLE: Record<PcDiagFinding['severity'], { border: string; text: string; icon: typeof Info }> = {
  Critical: { border: 'border-l-red-500',    text: 'text-red-400',    icon: AlertOctagon },
  Major:    { border: 'border-l-orange-400', text: 'text-orange-300', icon: AlertTriangle },
  Minor:    { border: 'border-l-yellow-400', text: 'text-yellow-300', icon: CircleAlert },
  Info:     { border: 'border-l-sky-400',    text: 'text-sky-300',    icon: Info },
};

const TIER_STYLE: Record<string, string> = {
  S: 'bg-emerald-500/20 text-emerald-300 border-emerald-400/40',
  A: 'bg-green-500/15 text-green-300 border-green-400/35',
  B: 'bg-sky-500/15 text-sky-300 border-sky-400/35',
  C: 'bg-amber-500/15 text-amber-300 border-amber-400/35',
  D: 'bg-red-500/15 text-red-300 border-red-400/35',
  Unknown: 'bg-white/[0.06] text-text-muted border-white/[0.12]',
};

function TierBadge({ tier, t }: { tier: string; t: TFunction }) {
  if (!tier || tier === 'Unknown') return null;
  return (
    <span className={'px-2 py-0.5 rounded-md border text-[11px] font-semibold ' + (TIER_STYLE[tier] ?? TIER_STYLE.Unknown)}>
      {t('pcdiag.tier.label', 'GTA-тир {{tier}}', { tier })}
    </span>
  );
}

function severityLabel(t: TFunction, s: PcDiagFinding['severity']): string {
  switch (s) {
    case 'Critical': return t('pcdiag.sev.critical', 'Критично');
    case 'Major': return t('pcdiag.sev.major', 'Важно');
    case 'Minor': return t('pcdiag.sev.minor', 'Заметно');
    default: return t('pcdiag.sev.info', 'К сведению');
  }
}

const CATEGORY_ORDER: PcDiagFinding['category'][] = ['Hardware', 'Game', 'Windows', 'Apps', 'Driver'];

function categoryLabel(t: TFunction, c: PcDiagFinding['category']): string {
  switch (c) {
    case 'Hardware': return t('pcdiag.cat.hardware', 'Железо');
    case 'Game': return t('pcdiag.cat.game', 'Игра');
    case 'Windows': return t('pcdiag.cat.windows', 'Windows');
    case 'Apps': return t('pcdiag.cat.apps', 'Программы в фоне');
    default: return t('pcdiag.cat.driver', 'Драйвер');
  }
}

const SEVERITY_RANK: Record<PcDiagFinding['severity'], number> = { Critical: 0, Major: 1, Minor: 2, Info: 3 };

function catalogText(t: TFunction, tw: PcDiagTweak): { title: string; body: string } {
  const d = tw.data;
  switch (tw.id) {
    case 'mmcss-games': return {
      title: t('pcdiag.c.mmcss.title', 'Профиль планировщика для игр'),
      body: t('pcdiag.c.mmcss.body', 'Штатный механизм Windows (MMCSS): приоритет GPU 8, категория High для игровых потоков. Эффект небольшой, риск нулевой.'),
    };
    case 'system-responsiveness': return {
      title: t('pcdiag.c.sysresp.title', 'Меньше резерва CPU под фоновые задачи'),
      body: t('pcdiag.c.sysresp.body', 'Windows резервирует 20% CPU под фоновые мультимедиа-службы; снижаем до 10%. Эффект небольшой.'),
    };
    case 'gamebar-nexus-off': return {
      title: t('pcdiag.c.nexus.title', 'Кнопка Xbox без оверлея'),
      body: t('pcdiag.c.nexus.body', 'Кнопка на геймпаде перестаёт открывать Game Bar поверх игры.'),
    };
    case 'stickykeys-off': return {
      title: t('pcdiag.c.sticky.title', 'Горячие клавиши залипания: выключить'),
      body: t('pcdiag.c.sticky.body', 'Пять нажатий Shift в бою больше не откроют системное окно поверх игры. Сами спецвозможности остаются доступны через Параметры.'),
    };
    case 'mouse-accel-off': return {
      title: t('pcdiag.c.mouse.title', 'Ускорение указателя мыши: выключить'),
      body: t('pcdiag.c.mouse.body', 'Одинаковое движение руки всегда даёт одинаковое движение прицела. Меняет привычное ощущение мыши, поэтому применяется только вручную. Подействует после перезахода в систему.'),
    };
    case 'w32-priority-separation': return {
      title: t('pcdiag.c.w32ps.title', 'Кванты планировщика (0x26)'),
      body: t('pcdiag.c.w32ps.body', 'Известный твик с эффектом на грани погрешности. Применяйте вместе с замером; разницы нет - возвращайте.'),
    };
    case 'network-throttling-off': return {
      title: t('pcdiag.c.netthr.title', 'Троттлинг сети при мультимедиа: выключить'),
      body: t('pcdiag.c.netthr.body', 'На большинстве систем разницы нет. Имеет смысл проверять тем, кто стримит во время игры.'),
    };
    case 'nvidia-profile': return {
      title: t('pcdiag.c.nvprof.title', 'Профиль драйвера NVIDIA для игры'),
      body: t('pcdiag.c.nvprof.body', 'Три настройки для gta5.exe через NVAPI: максимальная производительность (карта не сбрасывает частоты), короткая очередь кадров (ниже задержка ввода), кэш шейдеров 10 ГБ (меньше статтера). «Threaded optimization» не трогаем: это настройка OpenGL, к DX11-игре она отношения не имеет.'),
    };
    case 'commandline-clean': return {
      title: t('pcdiag.c.cmdline.title', 'Чистка commandline.txt от плацебо-флагов'),
      body: d.flags
        ? t('pcdiag.c.cmdline.bodyFound', 'Найдены флаги из чужих гайдов: {{flags}}. Таких параметров у GTA V не существует, файл они только замусоривают. Убираем; прежнее содержимое сохраняется в журнале.', { flags: d.flags })
        : t('pcdiag.c.cmdline.bodyClean', 'Файл параметров запуска без мусорных флагов.'),
    };
    case 'shader-cache-clean': return {
      title: t('pcdiag.c.shader.title', 'Очистка кэша шейдеров: {{mb}} МБ', { mb: d.mb ?? '0' }),
      body: t('pcdiag.c.shader.body', 'Лечит статтер от повреждённого кэша драйвера. Кэш пересоберётся сам, первые запуски игр будут немного дольше. Отката нет.'),
    };
    case 'temp-clean': return {
      title: t('pcdiag.c.temp.title', 'Временные файлы старше недели: {{mb}} МБ', { mb: d.mb ?? '0' }),
      body: t('pcdiag.c.temp.body', 'Освобождает место на диске. На FPS не влияет, и мы прямо об этом говорим. Отката нет.'),
    };
    case 'hags-on': return {
      title: t('pcdiag.c.hags.title', 'Аппаратное планирование GPU'),
      body: t('pcdiag.c.hags.body', 'Очередью кадров управляет сама видеокарта, а не Windows. На картах уровня GTX 10xx и новее иногда снижает задержку, на старых бывает хуже. Применяйте с проверкой в игре: стало хуже - кнопка «Вернуть». Нужна перезагрузка.'),
    };
    case 'fso-off-gta': return {
      title: t('pcdiag.c.fso.title', 'Полноэкранные оптимизации GTA: выключить'),
      body: t('pcdiag.c.fso.body', 'Windows подменяет полный экран игры гибридным режимом. Классический полный экран на части систем держит фреймтайм ровнее. Эффект зависит от машины - проверяйте в игре, возврат кнопкой.'),
    };
    case 'power-throttling-gta': return {
      title: t('pcdiag.c.pthrottle.title', 'Экономия энергии для GTA5.exe: запретить'),
      body: t('pcdiag.c.pthrottle.body', 'Windows умеет придушивать процессы ради батареи. Штатная команда запрещает это для GTA5.exe. На ноутбуках убирает часть просадок, на десктопах работает как страховка.'),
    };
    case 'widgets-off': return {
      title: t('pcdiag.c.widgets.title', 'Виджеты Windows: выключить'),
      body: t('pcdiag.c.widgets.body', 'Фоновый процесс виджетов (погода и новости на панели задач) постоянно висит в памяти. Выключение убирает процесс и кнопку с панели. Возвращается кнопкой, нужна перезагрузка.'),
    };
    case 'background-apps-off': return {
      title: t('pcdiag.c.bgapps.title', 'Фон приложений Store: запретить'),
      body: t('pcdiag.c.bgapps.body', 'Приложения из Microsoft Store перестают крутиться в фоне. Уведомления продолжат приходить, а память и такты процессора освобождаются - заметнее всего на слабых машинах.'),
    };
    default: return { title: tw.id, body: '' };
  }
}

const GRADE_CHIP: Record<PcDiagTweak['grade'], { label: string; cls: string }> = {
  works: { label: 'РАБОТАЕТ', cls: 'text-emerald-300 bg-emerald-500/10' },
  micro: { label: 'МИКРО', cls: 'text-sky-300 bg-sky-500/10' },
  experiment: { label: 'ЭКСПЕРИМЕНТ', cls: 'text-violet-300 bg-violet-500/10' },
  device: { label: 'ДЕВАЙСЫ', cls: 'text-amber-300 bg-amber-500/10' },
  maintenance: { label: 'ОБСЛУЖИВАНИЕ', cls: 'text-text-muted bg-white/[0.06]' },
};

function tweakTitle(t: TFunction, id: string): string {
  switch (id) {
    case 'mmcss-games': return t('pcdiag.tw.mmcss', 'Профиль планировщика для игр');
    case 'system-responsiveness': return t('pcdiag.tw.sysresp', 'Резерв CPU под фон: 10%');
    case 'gamebar-nexus-off': return t('pcdiag.tw.nexus', 'Кнопка Xbox без оверлея');
    case 'stickykeys-off': return t('pcdiag.tw.sticky', 'Горячие клавиши залипания выключены');
    case 'mouse-accel-off': return t('pcdiag.tw.mouse', 'Ускорение мыши выключено');
    case 'w32-priority-separation': return t('pcdiag.tw.w32ps', 'Кванты планировщика 0x26');
    case 'network-throttling-off': return t('pcdiag.tw.netthr', 'Троттлинг сети выключен');
    case 'commandline-clean': return t('pcdiag.tw.cmdline', 'commandline.txt очищен');
    case 'nvidia-profile': return t('pcdiag.tw.nvprof', 'Профиль NVIDIA для gta5.exe');
    case 'shader-cache-clean': return t('pcdiag.tw.shader', 'Кэш шейдеров очищен');
    case 'temp-clean': return t('pcdiag.tw.temp', 'Временные файлы удалены');
    case 'power-balanced': case 'power-saver': return t('pcdiag.tw.power', 'Схема питания: Высокая производительность');
    case 'gamedvr-on': return t('pcdiag.tw.gamedvr', 'Фоновая запись Game Bar: выключена');
    case 'gamemode-off': return t('pcdiag.tw.gamemode', 'Игровой режим: включён');
    case 'transparency-on': return t('pcdiag.tw.transparency', 'Прозрачность Windows: выключена');
    case 'display-not-max-hz': return t('pcdiag.tw.hz', 'Герцовка монитора: максимальная');
    case 'sysmain-off': return t('pcdiag.tw.sysmain', 'Служба SysMain: включена обратно');
    case 'wsearch-off': return t('pcdiag.tw.wsearch', 'Поиск Windows: включён обратно');
    case 'eventlog-off': return t('pcdiag.tw.eventlog', 'Журнал событий: включён обратно');
    case 'prefetch-off': return t('pcdiag.tw.prefetch', 'Prefetch: включён обратно');
    case 'pagefile-off': return t('pcdiag.tw.pagefile', 'Файл подкачки: автоматический');
    case 'bcd-useplatformclock': return t('pcdiag.tw.bcd', 'Флаг useplatformclock: убран');
    case 'game-priority-normal': return t('pcdiag.tw.priority', 'Приоритет GTA5.exe: High');
    case 'hags-on': return t('pcdiag.tw.hags', 'Аппаратное планирование GPU');
    case 'fso-off-gta': return t('pcdiag.tw.fso', 'Полноэкранные оптимизации GTA выключены');
    case 'power-throttling-gta': return t('pcdiag.tw.pthrottle', 'Троттлинг GTA5.exe запрещён');
    case 'widgets-off': return t('pcdiag.tw.widgets', 'Виджеты Windows выключены');
    case 'background-apps-off': return t('pcdiag.tw.bgapps', 'Фон приложений Store запрещён');
    default: return id;
  }
}

type PcDiagTab = 'overview' | 'fix' | 'ai' | 'log';

function agoText(t: TFunction, ts: number | null): string {
  if (ts == null) return '';
  const min = Math.floor((Date.now() - ts) / 60000);
  if (min < 1) return t('pcdiag.ago.now', 'только что');
  if (min < 60) return t('pcdiag.ago.min', '{{n}} мин назад', { n: min });
  const h = Math.floor(min / 60);
  if (h < 24) return t('pcdiag.ago.hour', '{{n}} ч назад', { n: h });
  const d = Math.floor(h / 24);
  if (d === 1) return t('pcdiag.ago.yesterday', 'вчера');
  return t('pcdiag.ago.day', '{{n}} дн назад', { n: d });
}

function driverUrl(gpu: string): string {
  const s = gpu.toLowerCase();
  if (s.includes('nvidia') || s.includes('geforce') || s.includes('rtx') || s.includes('gtx'))
    return 'https://www.nvidia.com/ru-ru/drivers/';
  if (s.includes('amd') || s.includes('radeon'))
    return 'https://www.amd.com/ru/support/download/drivers.html';
  return 'https://www.intel.com/content/www/ru/ru/download-center/home.html';
}

function StatTile({ icon: IconEl, label, value, hint, badge }: {
  icon: typeof Cpu;
  label: string;
  value: string;
  hint?: ReactNode;
  badge?: ReactNode;
}) {
  return (
    <Island className="p-4 h-full flex flex-col">
      <div className="flex items-center gap-2 mb-2">
        <IconEl size={14} className="text-text-muted shrink-0" />
        <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{label}</span>
        {badge && <span className="ml-auto">{badge}</span>}
      </div>
      <div className="flex-1 flex flex-col justify-center min-h-0">
        <div className="text-[15px] font-medium leading-snug truncate" title={value}>{value}</div>
        {hint && <div className="text-[13px] text-text-muted mt-1.5 leading-[1.6]">{hint}</div>}
      </div>
    </Island>
  );
}

export function PcDiagScreen() {
  const { t } = useTranslation();

  const canApply = useCanSeeTesterFeature();

  const report         = usePcDiagStore(s => s.report);
  const takenAt        = usePcDiagStore(s => s.takenAt);
  const journal        = usePcDiagStore(s => s.journal);
  const tweaks         = usePcDiagStore(s => s.tweaks);
  const scanning       = usePcDiagStore(s => s.scanning);
  const error          = usePcDiagStore(s => s.error);
  const ensureSnapshot = usePcDiagStore(s => s.ensureSnapshot);
  const refreshState   = usePcDiagStore(s => s.refreshState);

  const [tab, setTab] = useState<PcDiagTab>('overview');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, { ok: boolean; text: string }>>({});

  useEffect(() => {
    void ensureSnapshot();
    if (canApply) void refreshState();
  }, [ensureSnapshot, refreshState, canApply]);

  const appliedIds = new Set(journal.filter(e => !e.reverted).map(e => e.id));

  const requestNavigate = useNavStore(s => s.requestNavigate);

  const auth = useSessionStore(s => s.auth);
  const aiUserId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const aiHistoryKey = aiUserId ? `pcdiag.ai.history.${aiUserId}` : null;
  const [aiHistory, setAiHistory] = useState<{ role: 'user' | 'model'; text: string }[]>(
    () => (aiHistoryKey ? readCache<{ role: 'user' | 'model'; text: string }[]>(aiHistoryKey) : null) ?? []);
  useEffect(() => {
    if (aiHistoryKey && aiHistory.length > 0) writeCache(aiHistoryKey, aiHistory.slice(-40));
  }, [aiHistory, aiHistoryKey]);
  useEffect(() => {
    if (!aiHistoryKey) return;
    const saved = readCache<{ role: 'user' | 'model'; text: string }[]>(aiHistoryKey);
    if (saved?.length) setAiHistory(prev => (prev.length === 0 ? saved : prev));
  }, [aiHistoryKey]);
  const [aiBusy, setAiBusy] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);
  const [aiQuestion, setAiQuestion] = useState('');

  const clearAi = useCallback(() => {
    setAiHistory([]);
    setAiError(null);
    if (aiHistoryKey) clearCache(aiHistoryKey);
  }, [aiHistoryKey]);

  const aiEndRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => { aiEndRef.current?.scrollIntoView({ block: 'end' }); }, [aiHistory, aiBusy, tab]);

  const askAi = useCallback(async (question: string | null) => {
    if (!aiUserId) { setAiError('guest'); return; }
    setAiBusy(true); setAiError(null);
    try {
      const r = await bridge.pcDiagAi(aiUserId, question, aiHistory.slice(-8));
      if (!r.ok) { setAiError(r.error || 'ai_failed'); return; }
      setAiHistory(prev => [
        ...prev,
        ...(question ? [{ role: 'user' as const, text: question }] : []),
        { role: 'model' as const, text: r.text },
      ]);
      setAiQuestion('');
    } catch {
      setAiError('ai_failed');
    } finally {
      setAiBusy(false);
    }
  }, [aiUserId, aiHistory]);

  const fixableIds = canApply ? [
    ...(report ? report.findings.filter(f => f.autoFixable && !appliedIds.has(f.id)).map(f => f.id) : []),
    ...tweaks.filter(tw => tw.state === 'Ready' && tw.inAllSafe && !appliedIds.has(tw.id)).map(tw => tw.id),
  ] : [];

  const reload = useCallback(async () => {
    await Promise.all([ensureSnapshot(true), refreshState()]);
  }, [ensureSnapshot, refreshState]);

  const applyAll = useCallback(async (ids: string[]) => {
    for (const id of ids) {
      setBusyId(id);
      try {
        const r = await bridge.pcDiagApply(id);
        const restart = r.requiresRestart ? ' ' + t('pcdiag.apply.restart', 'Полностью подействует после перезагрузки.') : '';
        setResults(prev => ({ ...prev, [id]: { ok: r.ok, text: r.message + restart } }));
      } catch (e) {
        setResults(prev => ({ ...prev, [id]: { ok: false, text: e instanceof Error ? e.message : String(e) } }));
      }
    }
    setBusyId(null);
    await reload();
  }, [t, reload]);

  const doApply = useCallback(async (id: string, revert: boolean) => {
    setBusyId(id);
    try {
      const r = revert ? await bridge.pcDiagRevert(id) : await bridge.pcDiagApply(id);
      const restart = r.requiresRestart ? ' ' + t('pcdiag.apply.restart', 'Полностью подействует после перезагрузки.') : '';
      setResults(prev => ({ ...prev, [id]: { ok: r.ok, text: r.message + restart } }));
      await reload();
    } catch (e) {
      setResults(prev => ({ ...prev, [id]: { ok: false, text: e instanceof Error ? e.message : String(e) } }));
    } finally {
      setBusyId(null);
    }
  }, [t, reload]);

  const monitors = report?.monitors ?? [];

  const counts: Record<PcDiagFinding['severity'], number> = { Critical: 0, Major: 0, Minor: 0, Info: 0 };
  report?.findings.forEach(f => { counts[f.severity]++; });

  const ranked = report
    ? [...report.findings].sort((a, b) =>
        SEVERITY_RANK[a.severity] - SEVERITY_RANK[b.severity] ||
        CATEGORY_ORDER.indexOf(a.category) - CATEGORY_ORDER.indexOf(b.category))
    : [];

  const gameSettingsFinding = report?.findings.find(f => f.id === 'gta-settings-headroom') ?? null;

  const fixedEntries = (() => {
    if (!report) return [];
    const visibleIds = new Set(report.findings.map(f => f.id));
    const catalogIds = new Set(tweaks.map(tw => tw.id));
    return journal.filter(e => !e.reverted && !visibleIds.has(e.id) && !catalogIds.has(e.id));
  })();

  const appliedTweaks = tweaks.filter(tw => appliedIds.has(tw.id));

  const tabs: ReadonlyArray<{ id: PcDiagTab; label: string; count?: number }> = [
    { id: 'overview', label: t('pcdiag.tab.overview', 'Обзор') },
    {
      id: 'fix',
      label: canApply ? t('pcdiag.tab.fix', 'Исправления') : t('pcdiag.tab.issues', 'Что не так'),
      count: report?.findings.length,
    },
    { id: 'ai', label: t('pcdiag.tab.ai', 'Разбор ИИ') },
    ...(canApply ? [{ id: 'log' as const, label: t('pcdiag.tab.log', 'Журнал'), count: appliedIds.size }] : []),
  ];

  const FindingRow = ({ f }: { f: PcDiagFinding }) => {
    const st = SEVERITY_STYLE[f.severity];
    const { title, body } = describeFinding(t, f);
    const IconEl = st.icon;
    const isApplied = appliedIds.has(f.id);
    const res = results[f.id];
    return (
      <Island className={'p-4 border-l-2 ' + st.border}>
        <div className="flex items-start gap-3">
          <IconEl size={17} className={'mt-0.5 shrink-0 ' + st.text} />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="text-[15px] font-semibold">{title}</span>
              <span className={'text-[11px] px-1.5 py-0.5 rounded bg-white/[0.05] ' + st.text}>
                {severityLabel(t, f.severity)}
              </span>
              {f.gainMinPercent != null && (
                <span className="text-[11px] px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-300 inline-flex items-center gap-1">
                  <Zap size={11} />
                  {t('pcdiag.findings.gain', 'до +{{max}}% кадров', { max: f.gainMaxPercent })}
                </span>
              )}
            </div>
            <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">{body}</p>
            {res && (
              <p className={'text-xs mt-1.5 ' + (res.ok ? 'text-emerald-300' : 'text-red-300')}>{res.text}</p>
            )}
          </div>
          {canApply && f.autoFixable && (
            <button
              onClick={() => void doApply(f.id, isApplied)}
              disabled={busyId !== null}
              className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                         bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1]
                         disabled:opacity-50 transition-colors"
            >
              {busyId === f.id
                ? <RefreshCw size={12} className="animate-spin" />
                : isApplied
                  ? t('pcdiag.apply.revert', 'Вернуть')
                  : t('pcdiag.apply.do', 'Применить')}
            </button>
          )}
          {f.id === 'gta-settings-headroom' && (
            <button
              onClick={() => requestNavigate('settings')}
              className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                         bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1] transition-colors"
            >
              {t('pcdiag.apply.configure', 'Настроить')}
              <ArrowRight size={12} />
            </button>
          )}
          {(f.id === 'gpu-driver-old' || f.id === 'gpu-driver-aging') && (
            <button
              onClick={() => window.open(driverUrl(String(f.data.gpu ?? '')), '_blank', 'noopener,noreferrer')}
              className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                         bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1] transition-colors"
            >
              {t('pcdiag.apply.driverPage', 'Скачать драйвер')}
              <ArrowRight size={12} />
            </button>
          )}
        </div>
      </Island>
    );
  };

  const rescanControls = (
    <div className="flex items-center gap-3 ml-auto">
      {takenAt != null && (
        <span className="text-xs text-text-muted hidden sm:inline">
          {t('pcdiag.snapshotAge', 'снимок: {{ago}}', { ago: agoText(t, takenAt) })}
        </span>
      )}
      <button
        onClick={() => void reload()}
        disabled={scanning}
        className="shrink-0 inline-flex items-center gap-2 px-3.5 h-9 rounded-xl text-sm
                   bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1]
                   disabled:opacity-50 transition-colors"
      >
        <RefreshCw size={15} className={scanning ? 'animate-spin' : ''} />
        {scanning ? t('pcdiag.scanning', 'Сканирую...') : t('pcdiag.rescan', 'Пересканировать')}
      </button>
    </div>
  );

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <header className="shrink-0 px-8">
        <ScreenHero
          title={t('pcdiag.title', 'Оптимизация')}
          subtitle={t('pcdiag.subtitle', 'Что тормозит GTA 5 RP на этом ПК')}
        />
      </header>

      <nav className="shrink-0 flex items-center gap-2 px-8 pt-3 pb-1">
        {tabs.map(item => {
          const isActive = item.id === tab;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => setTab(item.id)}
              style={{ outline: 'none' }}
              className={
                'inline-flex items-center gap-1.5 h-9 px-3.5 rounded-xl border ' +
                'text-[11.5px] font-bold uppercase tracking-[0.12em] ' +
                'transition-all duration-300 ease-smooth ' +
                (isActive
                  ? 'bg-white text-black border-white shadow-pill-active'
                  : 'bg-white/[0.04] border-white/[0.12] text-white/70 ' +
                    'hover:bg-white/[0.08] hover:text-white hover:border-white/25')
              }
            >
              {item.label}
              {item.count != null && item.count > 0 && (
                <span className={'text-[10px] font-semibold px-1.5 rounded ' +
                  (isActive ? 'bg-black/10' : 'bg-white/[0.08]')}>{item.count}</span>
              )}
            </button>
          );
        })}
        {rescanControls}
      </nav>

      <div className="flex-1 min-h-0 overflow-y-auto px-8 pb-8 pt-2">
        <div className="min-h-full flex flex-col">

          {scanning && !report && (
            <Island className="p-10 flex items-center justify-center">
              <AccentLoader />
            </Island>
          )}

          {error && !report && (
            <Island className="p-4">
              <div className="text-sm text-red-300">
                {t('pcdiag.error', 'Диагностика не отработала: {{msg}}', { msg: error })}
              </div>
            </Island>
          )}

          {report && (
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                key={tab}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.2, ease: EASE_DEPTH }}
                className="grow flex flex-col gap-3"
              >

                {tab === 'overview' && (
                  <>
                    <Island className="p-5">
                      <div className="flex items-center gap-4 flex-wrap">
                        <div className="min-w-0 flex-1">
                          <div className="text-2xl font-semibold tracking-tight">
                            {counts.Critical > 0
                              ? t('pcdiag.verdict.critical', 'Критичных: {{n}}', { n: counts.Critical })
                              : counts.Major > 0
                                ? t('pcdiag.verdict.major', 'Критичного нет')
                                : t('pcdiag.verdict.clean', 'Серьёзных проблем нет')}
                          </div>
                          <p className="text-[15px] text-text-secondary mt-1 truncate">
                            {ranked.length > 0
                              ? describeFinding(t, ranked[0]).title
                              : t('pcdiag.verdict.leadClean', 'Дальше кадры решают настройки самой игры.')}
                          </p>
                          <div className="flex items-center gap-2 flex-wrap mt-2.5">
                            {(['Critical', 'Major', 'Minor', 'Info'] as const).map(sev => counts[sev] > 0 && (
                              <span key={sev} className={'text-[11px] px-2 py-0.5 rounded-md bg-white/[0.05] ' + SEVERITY_STYLE[sev].text}>
                                {severityLabel(t, sev)}: {counts[sev]}
                              </span>
                            ))}
                            <span className="text-[11px] text-text-muted">
                              {t('pcdiag.findings.elapsed', 'снимок за {{ms}} мс', { ms: report.elapsedMs })}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center gap-2 shrink-0">
                          <button
                            onClick={() => setTab('fix')}
                            className="inline-flex items-center gap-1.5 px-3.5 h-9 rounded-xl text-sm
                                       bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1] transition-colors"
                          >
                            <Wrench size={14} />
                            {canApply ? t('pcdiag.go.fix', 'Исправления') : t('pcdiag.go.issues', 'Находки')}
                            {report.findings.length > 0 && (
                              <span className="text-[10px] font-semibold px-1.5 rounded bg-white/[0.08]">{report.findings.length}</span>
                            )}
                          </button>
                          <button
                            onClick={() => requestNavigate('settings')}
                            className="inline-flex items-center gap-1.5 px-3.5 h-9 rounded-xl text-sm
                                       bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1] transition-colors"
                          >
                            <Settings2 size={14} />
                            {gameSettingsFinding
                              ? t('pcdiag.go.gameGain', 'Настройки игры · до +{{gain}}%', { gain: gameSettingsFinding.data.gain })
                              : t('pcdiag.go.game', 'Настройки игры')}
                          </button>
                        </div>
                      </div>
                    </Island>

                    <div className="grid grid-cols-2 xl:grid-cols-3 gap-3 grow auto-rows-fr">
                      <StatTile
                        icon={Cpu}
                        label={t('pcdiag.snap.cpu', 'Процессор')}
                        value={report.cpuName}
                        badge={
                          <span className={'px-2 py-0.5 rounded-md border text-[11px] font-semibold ' + (TIER_STYLE[report.cpuTier] ?? TIER_STYLE.Unknown)}>
                            {report.cpuTier === 'Unknown'
                              ? t('pcdiag.tier.unknown', 'тир: ?')
                              : t('pcdiag.tier.label', 'GTA-тир {{tier}}', { tier: report.cpuTier })}
                          </span>
                        }
                        hint={<>
                          {t('pcdiag.snap.cpuMeta', '{{cores}} ядер / {{threads}} потоков · L3 {{l3}} МБ', { cores: report.cpuCores, threads: report.cpuThreads, l3: report.cpuL3Mb })}
                          {report.cpuX3D && <span className="ml-1.5 text-emerald-300">X3D</span>}
                          {report.cpuLaptop && <span className="ml-1.5">· {t('pcdiag.snap.laptop', 'ноутбук')}</span>}
                        </>}
                      />

                      <StatTile
                        icon={MemoryStick}
                        label={t('pcdiag.snap.ram', 'Память')}
                        value={t('pcdiag.snap.ramValue', '{{gb}} ГБ', { gb: report.ramTotalGb })}
                        badge={<TierBadge tier={report.ramTier} t={t} />}
                        hint={<>
                          {report.ramSticks.map((s, i) => (
                            <div key={i}>
                              {s.slot}: {s.capacityGb} ГБ {s.memType} · {t('pcdiag.snap.ramSpeed', 'факт {{mt}} МТ/с', { mt: s.configuredMt })}
                            </div>
                          ))}
                          {report.ramTierNote && (
                            <div className="text-text-secondary">{report.ramTierNote}</div>
                          )}
                          {report.ramSlotsTotal > 0 && report.ramSlotsTotal > report.ramSticks.length && (
                            <div>
                              {t('pcdiag.snap.ramSlots', 'слотов на плате: {{total}}, свободно {{free}}', {
                                total: report.ramSlotsTotal,
                                free: report.ramSlotsTotal - report.ramSticks.length,
                              })}
                            </div>
                          )}
                        </>}
                      />

                      <StatTile
                        icon={MonitorCog}
                        label={t('pcdiag.snap.gpu', 'Видеокарты')}
                        value={report.gpus[0]?.name ?? '?'}
                        hint={report.gpus.map((g, i) => (
                          <div key={i}>
                            {g.name}{g.isIntegrated ? ` (${t('pcdiag.snap.igpu', 'встройка')})` : g.vramGb > 0 ? ` · ${g.vramGb} ГБ` : ''}
                            {!g.isIntegrated && g.driverDate ? ` · ${t('pcdiag.snap.driver', 'драйвер от {{date}}', { date: g.driverDate })}` : ''}
                          </div>
                        ))}
                      />

                      <StatTile
                        icon={HardDrive}
                        label={t('pcdiag.snap.disks', 'Диски')}
                        value={report.disks[0]?.model ?? '?'}
                        badge={<TierBadge tier={report.diskTier} t={t} />}
                        hint={<>
                          {report.disks.map((d, i) => (
                            <div key={i}>{d.model} · {d.media === 'Hdd' ? 'HDD' : d.media === 'Ssd' ? 'SSD' : d.media} / {d.bus} · {d.sizeGb} ГБ</div>
                          ))}
                          {report.diskTierNote && (
                            <div className="text-text-secondary">{report.diskTierNote}</div>
                          )}
                          {report.gtaPath && (
                            <div>
                              {t('pcdiag.snap.gta', 'GTA: {{path}}', { path: report.gtaPath })}
                              {report.gtaDiskMedia === 'Ssd' && <span className="text-emerald-300"> · SSD</span>}
                              {report.gtaDiskMedia === 'Hdd' && <span className="text-red-300"> · HDD</span>}
                            </div>
                          )}
                        </>}
                      />

                      <StatTile
                        icon={Eye}
                        label={monitors.length > 1
                          ? t('pcdiag.snap.displaysNet', 'Экраны и сеть')
                          : t('pcdiag.snap.displayNet', 'Экран и сеть')}
                        value={report.displayCurrentHz > 0
                          ? t('pcdiag.snap.displayValue', '{{w}}x{{h}} @ {{hz}} Гц', { w: report.displayWidth, h: report.displayHeight, hz: report.displayCurrentHz })
                          : '?'}
                        badge={monitors.length > 1 ? (
                          <span className="text-[11px] px-2 py-0.5 rounded-md bg-white/[0.06] text-text-muted">
                            {t('pcdiag.snap.monitorCount', 'мониторов: {{n}}', { n: monitors.length })}
                          </span>
                        ) : undefined}
                        hint={<>
                          {monitors.length > 1 && monitors.map((m, i) => (
                            <div key={i}>
                              {m.name || m.deviceName}: {m.width}x{m.height} @ {m.currentHz} {t('pcdiag.snap.hz', 'Гц')}
                              {m.isPrimary && ' · ' + t('pcdiag.snap.primary', 'основной')}
                              {m.maxHz > m.currentHz && (
                                <span className="text-amber-300">
                                  {' · ' + t('pcdiag.snap.canDo', 'умеет {{max}} Гц', { max: m.maxHz })}
                                </span>
                              )}
                            </div>
                          ))}
                          {monitors.length === 1 && (monitors[0].name || monitors[0].adapter) && (
                            <div>{monitors[0].name || monitors[0].adapter}</div>
                          )}
                          {monitors.length <= 1 && report.displayMaxHz > report.displayCurrentHz && (
                            <div className="text-amber-300">
                              {t('pcdiag.snap.displayMaxLine', 'монитор умеет {{max}} Гц - стоит поднять', { max: report.displayMaxHz })}
                            </div>
                          )}
                          <div>
                            {report.netWired ? t('pcdiag.snap.wired', 'сеть: кабель') : report.netWireless ? t('pcdiag.snap.wifi', 'сеть: Wi-Fi') : '?'}
                            {report.netVpn && ' + VPN'}
                          </div>
                        </>}
                      />

                      <StatTile
                        icon={Layers3}
                        label={t('pcdiag.snap.os', 'Система')}
                        value={report.osCaption}
                        hint={<>
                          <div>{t('pcdiag.snap.power', 'питание: {{scheme}}', { scheme: report.powerScheme || '?' })}</div>
                          {report.vbsRunning && <div>{t('pcdiag.snap.vbs', 'VBS включён (цена ~5% кадров, выключается только вручную)')}</div>}
                          {report.gameDvrOn && <div>{t('pcdiag.snap.dvr', 'фоновая запись Game Bar включена')}</div>}
                        </>}
                      />
                    </div>

                    {ranked.length > 0 && (
                      <div className="flex flex-col gap-2">
                        <div className="flex items-center justify-between">
                          <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">
                            {t('pcdiag.top.title', 'Главные находки')}
                          </span>
                          <button
                            onClick={() => setTab('fix')}
                            className="text-[11px] text-text-muted hover:text-text-primary transition-colors inline-flex items-center gap-1"
                          >
                            {t('pcdiag.top.all', 'все {{n}}', { n: report.findings.length })}
                            <ArrowRight size={11} />
                          </button>
                        </div>
                        {ranked.slice(0, 3).map((f, i) => {
                          const st = SEVERITY_STYLE[f.severity];
                          const IconEl = st.icon;
                          const { title } = describeFinding(t, f);
                          return (
                            <Island key={f.id + i} className={'border-l-2 ' + st.border}>
                              <button
                                onClick={() => setTab('fix')}
                                style={{ outline: 'none' }}
                                className="w-full flex items-center gap-3 text-left px-4 py-2.5"
                              >
                                <IconEl size={15} className={'shrink-0 ' + st.text} />
                                <span className="text-[15px] min-w-0 flex-1 truncate">{title}</span>
                                {f.gainMinPercent != null && (
                                  <span className="text-[11px] px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-300 shrink-0">
                                    {t('pcdiag.findings.gain', 'до +{{max}}% кадров', { max: f.gainMaxPercent })}
                                  </span>
                                )}
                                <span className={'text-[11px] shrink-0 ' + st.text}>{severityLabel(t, f.severity)}</span>
                              </button>
                            </Island>
                          );
                        })}
                      </div>
                    )}

                    {report.background.length > 0 && (
                      <Island className="p-4">
                        <div className="flex items-center gap-2 text-sm font-medium mb-2">
                          <Layers3 size={15} className="text-accent" />
                          {t('pcdiag.snap.bg', 'Фон сейчас')}
                        </div>
                        <div className="flex flex-wrap gap-x-5 gap-y-1">
                          {report.background.map((b, i) => (
                            <span key={i} className="text-xs text-text-muted">
                              {b.name}: <span className="text-text-primary">{b.gb} ГБ</span>
                              {b.count > 1 ? ` · ${b.count} проц.` : ''}
                            </span>
                          ))}
                        </div>
                      </Island>
                    )}

                    {report.sensorErrors.length > 0 && (
                      <div className="text-xs text-text-muted">
                        {t('pcdiag.sensors.failed', 'Не отработали датчики: {{list}}. Отчёт собран без них.', { list: report.sensorErrors.join(', ') })}
                      </div>
                    )}
                  </>
                )}

                {tab === 'fix' && (
                  <>
                    {canApply && (
                      <div className="flex items-center gap-2 text-xs text-text-muted">
                        <Eye size={13} />
                        {t('pcdiag.hygiene', 'Перед первым изменением - точка восстановления. Каждое изменение в журнале, возврат кнопкой «Вернуть».')}
                      </div>
                    )}

                    {canApply && fixableIds.length > 0 && (
                      <Island className="p-4">
                        <div className="flex items-center gap-3 flex-wrap">
                          <Zap size={18} className="text-emerald-300 shrink-0" />
                          <div className="min-w-0 flex-1">
                            <div className="text-sm font-medium">
                              {t('pcdiag.applyAll.title', 'К применению: {{n}}', { n: fixableIds.length })}
                            </div>
                            <div className="text-xs text-text-muted mt-0.5">
                              {t('pcdiag.applyAll.sub', 'Эксперименты и твики, меняющие привычные ощущения, сюда не входят - они применяются вручную ниже.')}
                            </div>
                          </div>
                          <button
                            onClick={() => void applyAll(fixableIds)}
                            disabled={busyId !== null}
                            className="shrink-0 inline-flex items-center gap-2 px-4 h-9 rounded-xl text-sm font-medium
                                       bg-emerald-500/15 border border-emerald-400/30 text-emerald-200
                                       hover:bg-emerald-500/25 disabled:opacity-50 transition-colors"
                          >
                            {busyId !== null
                              ? <RefreshCw size={14} className="animate-spin" />
                              : t('pcdiag.applyAll.do', 'Применить все')}
                          </button>
                        </div>
                      </Island>
                    )}

                    {report.findings.length === 0 && (
                      <Island className="p-5 text-sm text-text-muted">
                        {t('pcdiag.findings.empty', 'Проблем не найдено.')}
                      </Island>
                    )}

                    {CATEGORY_ORDER.map(cat => {
                      const items = report.findings
                        .filter(f => f.category === cat)
                        .sort((a, b) => SEVERITY_RANK[a.severity] - SEVERITY_RANK[b.severity]);
                      if (items.length === 0) return null;
                      return (
                        <div key={cat} className="flex flex-col gap-2.5">
                          <div className="text-[11px] font-semibold uppercase tracking-wider text-text-muted mt-2">
                            {categoryLabel(t, cat)} · {items.length}
                          </div>
                          {items.map((f, i) => <FindingRow key={f.id + i} f={f} />)}
                        </div>
                      );
                    })}

                    {canApply && tweaks.some(tw => tw.state !== 'NotApplicable') && (
                      <div className="flex flex-col gap-2.5">
                        <div className="text-[11px] font-semibold uppercase tracking-wider text-text-muted mt-2">
                          {t('pcdiag.catalog.title', 'Твики')} · {tweaks.filter(tw => tw.state !== 'NotApplicable').length}
                        </div>
                        {tweaks.filter(tw => tw.state !== 'NotApplicable').map(tw => {
                          const { title, body } = catalogText(t, tw);
                          const chip = GRADE_CHIP[tw.grade];
                          const isApplied = appliedIds.has(tw.id);
                          const res = results[tw.id];
                          const canRevert = isApplied && tw.grade !== 'maintenance';
                          return (
                            <Island key={tw.id} className="p-4 border-l-2 border-l-white/[0.14]">
                              <div className="flex items-start gap-3">
                                <Zap size={16} className="mt-0.5 shrink-0 text-text-muted" />
                                <div className="min-w-0 flex-1">
                                  <div className="flex items-center gap-2 flex-wrap">
                                    <span className="text-[15px] font-semibold">{title}</span>
                                    <span className={'text-[10px] font-semibold px-1.5 py-0.5 rounded ' + chip.cls}>{chip.label}</span>
                                    {tw.state === 'Done' && !isApplied && (
                                      <span className="text-[11px] px-1.5 py-0.5 rounded bg-white/[0.05] text-text-muted">
                                        {t('pcdiag.catalog.alreadyDone', 'уже в нужном состоянии')}
                                      </span>
                                    )}
                                  </div>
                                  <p className="text-[13px] text-text-secondary mt-1 leading-relaxed">{body}</p>
                                  {res && (
                                    <p className={'text-xs mt-1.5 ' + (res.ok ? 'text-emerald-300' : 'text-red-300')}>{res.text}</p>
                                  )}
                                </div>
                                {(tw.state === 'Ready' || canRevert) && (
                                  <button
                                    onClick={() => void doApply(tw.id, canRevert)}
                                    disabled={busyId !== null}
                                    className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                                               bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1]
                                               disabled:opacity-50 transition-colors"
                                  >
                                    {busyId === tw.id
                                      ? <RefreshCw size={12} className="animate-spin" />
                                      : canRevert
                                        ? t('pcdiag.apply.revert', 'Вернуть')
                                        : tw.grade === 'maintenance'
                                          ? t('pcdiag.apply.clean', 'Очистить')
                                          : t('pcdiag.apply.do', 'Применить')}
                                  </button>
                                )}
                              </div>
                            </Island>
                          );
                        })}
                      </div>
                    )}
                  </>
                )}

                {tab === 'ai' && (() => {
                  const discreteGpu = report.gpus.find(g => !g.isIntegrated) ?? report.gpus[0];
                  const greeting = t('pcdiag.ai.hello',
                    'Привет. Вижу этот компьютер: **{{cpu}}**, {{ram}} ГБ памяти, **{{gpu}}**{{gta}}. Могу разобрать, что здесь режет кадры, - жми кнопку или спроси текстом.',
                    {
                      cpu: report.cpuName,
                      ram: report.ramTotalGb,
                      gpu: discreteGpu?.name ?? '?',
                      gta: report.gtaDiskMedia === 'Hdd' ? ', GTA на HDD'
                         : report.gtaDiskMedia === 'Ssd' ? ', GTA на SSD' : '',
                    });
                  return (
                    <Island className="flex flex-col h-[calc(100vh-190px)] min-h-[420px]">
                      <div className="flex items-center gap-2.5 px-5 py-3.5 border-b border-white/[0.07] shrink-0">
                        <Gauge size={17} className="text-accent" />
                        <span className="text-[15px] font-semibold">{t('pcdiag.ai.title', 'Разбор ИИ')}</span>
                        <span className="text-xs text-text-muted hidden md:inline">
                          {t('pcdiag.ai.note', 'цифры - только из отчёта диагностики')}
                        </span>
                        {aiHistory.length > 0 && (
                          <button
                            onClick={clearAi}
                            className="ml-auto text-xs text-text-muted hover:text-text-primary transition-colors"
                          >
                            {t('pcdiag.ai.newChat', 'Новый диалог')}
                          </button>
                        )}
                      </div>

                      <div className="flex-1 min-h-0 overflow-y-auto px-6 py-6 flex flex-col gap-5">
                        {aiHistory.length === 0 && (
                          <div className="flex-1 flex flex-col items-center justify-center text-center gap-5 py-6">
                            <div className="w-12 h-12 rounded-2xl bg-white/[0.06] border border-white/[0.1] flex items-center justify-center">
                              <Gauge size={22} className="text-accent" />
                            </div>
                            <AiText text={greeting} className="text-[17px] leading-[1.65] max-w-[640px] text-text-secondary" />
                            <button
                              onClick={() => void askAi(null)}
                              disabled={aiBusy}
                              className="inline-flex items-center gap-2 px-6 h-11 rounded-xl text-[15px] font-semibold
                                         bg-white text-black hover:bg-white/90 shadow-pill-active
                                         disabled:opacity-50 transition-colors"
                            >
                              {aiBusy ? <RefreshCw size={16} className="animate-spin" /> : null}
                              {aiBusy ? t('pcdiag.ai.thinking', 'Разбираю...') : t('pcdiag.ai.run', 'Разобрать этот ПК')}
                            </button>
                            <div className="flex items-center gap-2 flex-wrap justify-center max-w-[640px]">
                              {[
                                t('pcdiag.ai.q1', 'Что даст больше всего кадров?'),
                                t('pcdiag.ai.q2', 'Стоит ли менять видеокарту?'),
                                t('pcdiag.ai.q3', 'Почему фризит в центре города?'),
                              ].map(q => (
                                <button
                                  key={q}
                                  onClick={() => { if (!aiBusy) void askAi(q); }}
                                  disabled={aiBusy}
                                  className="px-3.5 h-9 rounded-full text-[13px] text-text-secondary
                                             bg-white/[0.04] border border-white/[0.1]
                                             hover:bg-white/[0.09] hover:text-text-primary
                                             disabled:opacity-50 transition-colors"
                                >
                                  {q}
                                </button>
                              ))}
                            </div>
                          </div>
                        )}
                        {aiHistory.map((m, i) => (
                          <div key={i} className={m.role === 'user'
                            ? 'self-end max-w-[75%] rounded-2xl px-4 py-2.5 bg-white/[0.08] text-[15px] leading-relaxed'
                            : 'max-w-[760px] text-text-secondary'}>
                            {m.role === 'user' ? m.text : <AiText text={m.text} />}
                          </div>
                        ))}
                        {aiBusy && aiHistory.length > 0 && (
                          <div className="flex items-center gap-2 text-xs text-text-muted">
                            <RefreshCw size={12} className="animate-spin" />
                            {t('pcdiag.ai.thinking', 'Разбираю...')}
                          </div>
                        )}
                        {aiError && (
                          <p className="text-xs text-red-300">{aiErrorText(t, aiError)}</p>
                        )}
                        <div ref={aiEndRef} />
                      </div>

                      <div className="px-5 py-4 border-t border-white/[0.07] shrink-0 flex gap-2.5">
                        <input
                          value={aiQuestion}
                          onChange={e => setAiQuestion(e.target.value)}
                          onKeyDown={e => { if (e.key === 'Enter' && aiQuestion.trim() && !aiBusy) void askAi(aiQuestion.trim()); }}
                          placeholder={t('pcdiag.ai.placeholder', 'Вопрос по этому ПК: «а если поставить вторую планку?»')}
                          className="flex-1 h-12 px-4 rounded-xl text-[15px] bg-white/[0.05] border border-white/[0.1]
                                     outline-none focus:border-white/[0.28] placeholder:text-text-muted"
                        />
                        <button
                          onClick={() => { if (aiQuestion.trim()) void askAi(aiQuestion.trim()); }}
                          disabled={aiBusy || !aiQuestion.trim()}
                          className="shrink-0 inline-flex items-center gap-1.5 px-6 h-12 rounded-xl text-[15px] font-semibold
                                     bg-white text-black hover:bg-white/90 shadow-pill-active
                                     disabled:opacity-40 disabled:bg-white/[0.08] disabled:text-text-muted
                                     disabled:shadow-none transition-colors"
                        >
                          {aiBusy ? <RefreshCw size={15} className="animate-spin" /> : t('pcdiag.ai.send', 'Спросить')}
                        </button>
                      </div>
                    </Island>
                  );
                })()}

                {tab === 'log' && (
                  <>
                    {appliedIds.size === 0 && (
                      <Island className="p-5 text-sm text-text-muted">
                        {t('pcdiag.journal.empty', 'Пока ничего не применено. Всё применённое появится здесь с кнопкой «Вернуть».')}
                      </Island>
                    )}

                    {fixedEntries.length > 0 && (
                      <div className="flex flex-col gap-2.5">
                        <div className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">
                          {t('pcdiag.journal.title', 'Применено')} · {fixedEntries.length}
                        </div>
                        {fixedEntries.map(e => (
                          <Island key={e.id} className="p-3.5 border-l-2 border-l-emerald-400">
                            <div className="flex items-center gap-3">
                              <Zap size={15} className="text-emerald-300 shrink-0" />
                              <div className="min-w-0 flex-1">
                                <span className="text-sm">{tweakTitle(t, e.id)}</span>
                                {results[e.id] && (
                                  <p className={'text-xs mt-0.5 ' + (results[e.id].ok ? 'text-emerald-300' : 'text-red-300')}>{results[e.id].text}</p>
                                )}
                              </div>
                              <button
                                onClick={() => void doApply(e.id, true)}
                                disabled={busyId !== null}
                                className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                                           bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1]
                                           disabled:opacity-50 transition-colors"
                              >
                                {busyId === e.id
                                  ? <RefreshCw size={12} className="animate-spin" />
                                  : t('pcdiag.apply.revert', 'Вернуть')}
                              </button>
                            </div>
                          </Island>
                        ))}
                      </div>
                    )}

                    {appliedTweaks.length > 0 && (
                      <div className="flex flex-col gap-2.5">
                        <div className="text-[11px] font-semibold uppercase tracking-wider text-text-muted mt-2">
                          {t('pcdiag.journal.tweaks', 'Твики из каталога')} · {appliedTweaks.length}
                        </div>
                        {appliedTweaks.map(tw => (
                          <Island key={tw.id} className="p-3.5 border-l-2 border-l-emerald-400">
                            <div className="flex items-center gap-3">
                              <Zap size={15} className="text-emerald-300 shrink-0" />
                              <span className="text-sm min-w-0 flex-1">{tweakTitle(t, tw.id)}</span>
                              {tw.grade !== 'maintenance' && (
                                <button
                                  onClick={() => void doApply(tw.id, true)}
                                  disabled={busyId !== null}
                                  className="shrink-0 inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[13px] font-medium
                                             bg-white/[0.06] border border-white/[0.1] hover:bg-white/[0.1]
                                             disabled:opacity-50 transition-colors"
                                >
                                  {busyId === tw.id
                                    ? <RefreshCw size={12} className="animate-spin" />
                                    : t('pcdiag.apply.revert', 'Вернуть')}
                                </button>
                              )}
                            </div>
                          </Island>
                        ))}
                      </div>
                    )}
                  </>
                )}

              </motion.div>
            </AnimatePresence>
          )}
        </div>
      </div>
    </div>
  );
}
