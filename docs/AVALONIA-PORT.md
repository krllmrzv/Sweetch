# План: Sweetch на Avalonia — Windows, Linux и macOS

Цель — один код интерфейса на трёх системах. Сейчас интерфейс написан на WPF и системных вызовах Windows; ядро конвертации уже кроссплатформенное. Переводим интерфейс на [Avalonia](https://avaloniaui.net) (так сделан Filee), системное — за интерфейсы с реализацией под каждую ОС.

Отметки: ✅ сделано · 🔄 в работе · ⬜ не начато.

## Что есть сейчас

| Часть | Строк | Перенос |
| --- | --- | --- |
| `Sweetch.Core` — все конвертации, форматы, каталог операций | ~4 300 | Как есть: `net10.0`, без Windows. Magick.NET, PDFium, SkiaSharp, ClosedXML, MimeKit — есть под Linux и macOS (x64 и arm64) |
| `Sweetch.App/UI`, `Wheel`, `Jobs`, `Actions` — окна и колесо (WPF, всё в коде, без XAML) | ~4 400 | Переписать на Avalonia. API близкий: Canvas, Path, геометрия, Grid, DockPanel, Border, TextBlock — почти один в один |
| `Input`, `Shell`, `Native`, `Ocr` — хуки, трей, Проводник, реестр, OCR | ~1 700 | Своя реализация на каждую ОС за общим интерфейсом |
| `SelfTest` — снимки всех экранов и проверки | ~500 | На Avalonia.Headless: те же снимки на трёх ОС в CI |

## Архитектура после порта

```
src/
  Sweetch.Core/               как сейчас — конвертации
  Sweetch.Services/           без UI: настройки, очередь задач, отслеживаемые папки,
                              загрузка доп. программ, обновления, покрытие форматов
  Sweetch.Platform/           интерфейсы ОС (список ниже)
  Sweetch.Platform.Windows/   хуки WH_*_LL, реестр, COM Проводника, Windows OCR, Inno Setup
  Sweetch.Platform.MacOS/     CGEventTap, pasteboard перетаскивания, LaunchAgent, Vision OCR
  Sweetch.Platform.Linux/     X11 (XInput2), порталы Wayland, D-Bus, .desktop-файлы, Tesseract
  Sweetch.UI/                 Avalonia: колесо, настройки, задачи, полка, диалоги, тема, шрифты
  Sweetch.App/                точка входа: собирает UI и платформу текущей ОС
```

Интерфейсы платформы:

| Интерфейс | Windows (есть) | macOS | Linux |
| --- | --- | --- | --- |
| `IGlobalInput` — перетаскивание + нажатие клавиши, колёсико, Esc | WH_KEYBOARD_LL / WH_MOUSE_LL | CGEventTap (нужно разрешение «Мониторинг ввода»); начало перетаскивания — по `NSPasteboard(.drag)` | X11: XInput2 raw events + владелец `XdndSelection`. Wayland: глобально нельзя — см. ниже |
| `IHotkey` — сочетание без мыши | RegisterHotKey | `RegisterEventHotKey` (Carbon) | X11: XGrabKey. Wayland: портал GlobalShortcuts (KDE, GNOME 48+) |
| `IFileSelection` — выбранные файлы в файловом менеджере | COM Shell Проводника | AppleScript к Finder (разрешение «Автоматизация») | Нет стандарта: буфер обмена и пункт в меню файлового менеджера |
| `IShellIntegration` — пункт «Sweetch…» в контекстном меню | реестр | Quick Action / Services в Info.plist | Nautilus scripts, Dolphin servicemenu, Thunar uca.xml, Nemo actions |
| `IAutostart` | реестр Run | LaunchAgent или `SMAppService` | `~/.config/autostart/*.desktop` |
| `INotifications` | уведомления трея | `UNUserNotificationCenter` | D-Bus `org.freedesktop.Notifications` |
| `IOcr` — текст с картинки | Windows.Media.Ocr | Vision (`VNRecognizeTextRequest`) | Tesseract (доп. программа) |
| `IShare` — «Поделиться» | системное окно Windows | `NSSharingServicePicker` | нет — пункт скрыт |
| `IUpdateInstaller` | тихий установщик / ZIP | заменить `Sweetch.app` из ZIP | AppImage заменяет сам себя; .deb — открыть страницу релиза |
| `IToolLocations` — где искать и куда качать FFmpeg, Pandoc, LibreOffice | как сейчас | Homebrew, `/Applications/LibreOffice.app`; статические сборки | PATH, пакеты дистрибутива, статические сборки FFmpeg и Pandoc |

Трей — встроенный `TrayIcon` Avalonia: Windows, строка меню macOS, StatusNotifierItem на Linux (KDE из коробки; GNOME — с расширением AppIndicator). Одиночный экземпляр — как сейчас (Mutex + именованный канал: в .NET на Unix это сокет).

## Wayland

Wayland запрещает приложениям видеть глобальный ввод и ставить окно в координаты курсора, поэтому жест «тащу файл + жму Shift» там невозможен в принципе (так же у Filee). Вместо него:

- **Зона сброса**: маленькое плавающее окно-полка; при наведении перетаскиваемого файла на неё раскрывается колесо, клавиша-модификатор во время перетаскивания внутри своего окна видна.
- **Контекстное меню** файлового менеджера → колесо по центру экрана.
- **Сочетание клавиш** через портал GlobalShortcuts → колесо для файлов из буфера обмена.
- На X11 (и XWayland-сессиях) — полный жест, как на Windows.

## Этапы

### 0. Подготовка в текущем WPF-приложении ⬜ (≈1 неделя)

Без видимых изменений, Windows-версия продолжает выходить.

- ⬜ Вынести из `Sweetch.App` всё без UI в `Sweetch.Services`: `AppSettings`, `JobRunner` (логика очереди), `FolderWatcher`, `ToolInstaller`, `Updater`, `ToolCoverage`-кэш
- ⬜ Завести `Sweetch.Platform` с интерфейсами выше; нынешний код — `Sweetch.Platform.Windows`
- ⬜ Колесо: отделить геометрию и раскладку подписей (секторы, `FitScale`, переворот сторон) от WPF — это чистая математика, она переедет без изменений и получит юнит-тесты

### 1. Avalonia на Windows — паритет с WPF ⬜ (≈3–4 недели)

Сначала Windows: есть что сравнивать один в один.

- ⬜ Тема (палитры, низкий контраст, тёмная/светлая), шрифты Onest и иконки, тонкая полоса прокрутки — стилями Avalonia
- ⬜ Колесо: прозрачное окно поверх всех, без активации, обе стороны, подписи, центр; тень — `BoxShadow` вместо эффекта (дешевле)
- ⬜ Настройки: все страницы, узкая раскладка, плашка доп. программ, поиск по форматам
- ⬜ Окно задач, полка, диалоги шаблонов, папок и параметров
- ⬜ Self-test на Avalonia.Headless: те же снимки (en, ru, светлая, тёмная, низкий контраст, узкое окно); сравнить с WPF-снимками глазами
- ⬜ Замерить: открытие колеса ≤ 10 мс, память в фоне ≈ как у WPF (~60 МБ), размер сборки
- ⬜ Выпустить Windows-версию на Avalonia вместо WPF (одна кодовая база дальше)

### 2. macOS ⬜ (≈2–3 недели)

- ⬜ `IGlobalInput`: CGEventTap для клавиш и колёсика; начало перетаскивания — по смене `changeCount` у pasteboard перетаскивания (так делают Dropover и Yoink)
- ⬜ Экран первого запуска: объяснить и открыть настройки разрешений «Мониторинг ввода» и «Универсальный доступ»; без них — колесо по сочетанию и из Finder
- ⬜ Строка меню вместо трея, автозапуск через `SMAppService`, Quick Action «Sweetch» в Finder
- ⬜ Выделение в Finder через AppleScript, OCR через Vision
- ⬜ Доп. программы: искать в Homebrew и `/Applications/LibreOffice.app`; качать статические FFmpeg и Pandoc для arm64 и x64
- ⬜ Пакет: `Sweetch.app` в DMG, две сборки (Apple Silicon и Intel); подпись и нотаризация (нужен Apple Developer ID — 99 $/год; без него Gatekeeper предупреждает, как у Filee)

### 3. Linux ⬜ (≈2–3 недели)

- ⬜ X11: XInput2 + XDND для жеста, XGrabKey для сочетания
- ⬜ Wayland: зона сброса, портал GlobalShortcuts, определение сессии и понятная подсказка в настройках
- ⬜ Пункты меню для Nautilus, Dolphin, Thunar, Nemo (ставятся галочкой в настройках, как контекстное меню на Windows)
- ⬜ Уведомления через D-Bus, автозапуск через `.desktop`, OCR через Tesseract как доп. программу
- ⬜ Доп. программы: сначала PATH и пакеты дистрибутива, иначе статические сборки FFmpeg и Pandoc
- ⬜ Пакеты: AppImage (обновляется сам) и .deb; Flatpak позже — его песочница мешает глобальному вводу и запуску доп. программ
- ⬜ Проверить на Ubuntu 24.04 (GNOME, Wayland и X11), Kubuntu/Fedora KDE, Linux Mint (Cinnamon)

### 4. Выпуск ⬜ (≈1 неделя)

- ⬜ CI: матрица windows-latest, ubuntu-latest, macos-latest (arm64) и macos-13 (x64); тесты ядра и self-test на каждой
- ⬜ Релиз собирает установщик Windows, ZIP, DMG ×2, AppImage, .deb; у каждого — SHA-256
- ⬜ Обновлялка выбирает свой файл по ОС и архитектуре
- ⬜ README: раздел для каждой ОС, что работает на Wayland; снимки с macOS и Linux

## Риски и вопросы

- **SkiaSharp**: Avalonia и PDFtoImage тянут свои версии Skia — версии придётся согласовать.
- **MsgReader** зависит от `System.Drawing.Common`, который на Linux и macOS работает не везде; проверить чтение MSG там в первую очередь, при необходимости заменить на OpenMcdf + свой разбор.
- **Распознавание текста** на Linux требует Tesseract с языковыми пакетами (rus+eng) — ещё одна доп. программа.
- **Нотаризация macOS** — решить, заводить ли аккаунт Apple Developer.
- **Скорость колеса**: у WPF сейчас ~5 мс до появления; у Avalonia держать «запасное» окно так же, как сейчас.

Итого ≈ 9–12 недель работы до релиза на трёх ОС; после этапа 1 Windows-версия уже на новой основе, и следующие этапы её не ломают.
