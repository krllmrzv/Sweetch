# План: все форматы Filee в Sweetch

Ориентир — список форматов [KnifeLemon/Filee](https://github.com/KnifeLemon/Filee). Делаем этапами: сначала то, что уже умеют встроенные движки (Magick.NET, FFmpeg, Pandoc), потом свой небольшой код, в конце — новые движки.

Редкие форматы, которыми почти никто не пользуется, — в «Долгом ящике» в самом конце: за них берёмся, только когда всё остальное готово.

Правило для каждого этапа: новые входы только расширяют списки в `Formats.cs`; новый сектор на колесе — только если он реально нужен многим. Редкие выходы уходят на «вторую сторону» колеса (этап 6), чтобы не дробить круг.

Отметки: ✅ сделано · 🔄 в работе · ⬜ не начато.

## Этап 1. Картинки через Magick.NET ✅

Движок уже внутри, нужно только научить списки.

- ✅ Чтение: PSD/PSB, TGA, PPM/PGM/PBM/PNM, PCX, QOI, DDS, JPEG XL, JPEG 2000 (JP2/J2K), OpenEXR, Radiance HDR, CUR, APNG, MNG, GIMP XCF, DICOM
- ✅ Чтение вектора: SVGZ, EMF, WMF (Magick.NET читает их сам)
- ✅ Выходы: BMP, GIF, JPEG XL

Проверено на Magick.NET 15.0: всё из списка читается и пишется, кроме XCF, DICOM, EMF, WMF и HEIC — эти только читаются.
- ⬜ Live Photo / Motion Photo → видео и обратно (вырезать MP4 из JPEG / собрать пару HEIC+MOV)
- ⬜ Трассировка в SVG (potrace-подобный код или готовая библиотека)

## Этап 2. Аудио и видео через FFmpeg ✅

- ✅ Чтение видео: OGV, VOB, M2TS, MXF, DIVX, ASF, RM/RMVB, F4V
- ✅ Чтение аудио: AMR, APE, WavPack, TTA, DSF/DFF (DSD), DTS, AC3, E-AC3, MKA, CAF, AU, VOC, W64
- ✅ Видео → MP4 HEVC (H.265, на видеокарте, если умеет) и → M4A
- ✅ Извлечение субтитров из видео (SRT)
- ✅ Выходы аудио: ALAC (M4A), WavPack, TTA, AC3 — на обратной стороне колеса
- ✅ Видео → AVI, APNG — на обратной стороне
- ⬜ Видео → AV1 (кодировщик есть не во всех сборках FFmpeg)

## Этап 3. Разметка через Pandoc ✅

Pandoc уже скачивается по требованию; добавляем читателей.

- ✅ LaTeX, reStructuredText, Org, AsciiDoc, Typst, Jupyter (IPYNB), FictionBook (FB2), Textile, MediaWiki, DocBook, OPML, Djot
- ✅ Markdown и другие документы → слайды PPTX (обратная сторона)
- ✅ Выходы: LaTeX, rST, Typst, AsciiDoc — на обратной стороне

## Этап 4. Данные и субтитры (свой код, без движков) ✅

- ✅ JSON ↔ YAML ↔ TOML ↔ XML ↔ JSON Lines (System.Text.Json + YamlDotNet + Tomlyn)
- ✅ Списки записей → CSV / XLSX (вложенные поля — колонки через точку), таблица → JSON
- ✅ vCard ↔ CSV / XLSX (в том числе quoted-printable из старых Android, колонки Google и на русском)
- ✅ Субтитры: ASS/SSA, SAMI (SMI), LRC, TTML/DFXP, MicroDVD (SUB) → SRT, VTT, ASS, LRC, TXT
- ✅ TXT с таймкодами («[00:01:02] текст»); «Субтитры» из видео дают SRT и такой TXT
- ✅ Любая кодировка на входе (UTF-8/16, Windows-1251, 1252), всегда UTF-8 на выходе; инструмент «UTF-8» на колесе Ctrl

## Этап 5. Документы, таблицы, презентации, почта ✅

Через LibreOffice (уже поддержан как доп. программа).

- ✅ Word: DOCM, DOTX, DOTM, WPS, WPD, Pages, FODT, OTT
- ✅ Таблицы: XLT, XLTX, XLTM, OTS, FODS, Numbers, ET; выходы JSON, HTML
- ✅ Презентации: PPTM, POTX, POTM, PPSX, PPS, OTP, FODP, Keynote; выход DOCX (слайд картинкой на страницу)
- ✅ Почта: EML и MSG → HTML/PDF, MSG → EML, вложения в папку (MimeKitLite, MsgReader)
- ✅ MHTML (сохранённые страницы) → один HTML / PDF
- ✅ PDF → многостраничный TIFF, CBZ; инструмент «Страницы» (1-3, 5, 8-)
- ⬜ PDF → SVG: PDFium не умеет, нужен MuPDF — в этап 7

## Этап 6. Вторая сторона колеса ✅

Как в Filee: Tab или колесо мыши переворачивает круг к редким форматам и инструментам.

- ✅ У операции флаг «редкая»; лицевая сторона — частые, обратная — редкие
- ✅ В настройках колёс — перенос любого сектора между сторонами и превью обеих сторон
- ✅ Колёсико мыши переворачивает меню (и при перетаскивании); подсказка «Колёсико: ещё N» под центром, обратная сторона другого цвета

## Этап 7. Порт на Avalonia: Linux и macOS ⬜

План — в [AVALONIA-PORT.md](AVALONIA-PORT.md).

## Этап 8. Новые движки по требованию ⬜ (отложен до порта)

Каждый — скачивается при первом использовании, как Pandoc (закреплённая версия + SHA-256).

- ⬜ calibre (ebook-convert): MOBI, AZW3/AZW/AZW4, KEPUB, LIT, LRF, CHM, PDB → EPUB/PDF/DOCX
- ⬜ Комиксы без движков: CBZ/CB7/CBR → PDF и обратно (SharpCompress уже есть)
- ⬜ Ghostscript: EPS, PS, AI → PDF/PNG; PDF → EPS
- ⬜ MuPDF (mutool): PDF → SVG по страницам
- ⬜ Вектор: EMF/WMF → PNG/PDF (System.Drawing на Windows), SVG ↔ SVGZ, CDR/VSD/CGM/ODG через LibreOffice
- ⬜ Шрифты: TTF ↔ OTF ↔ WOFF ↔ WOFF2 ↔ EOT (свой код + Brotli для WOFF2)
- ⬜ Архивы: ISO, CAB, DMG — чтение; выход 7Z (7-Zip по требованию)
- ⬜ CAD: DWG ↔ DXF, → PDF/SVG/PNG (ACadSharp)
- ⬜ 3D: GLB, glTF, STL, OBJ, 3MF, PLY, FBX → друг в друга и PNG-превью (SharpGLTF / Assimp)
- ⬜ Геоданные: Shapefile, GeoJSON, KML/KMZ, GPX ↔ CSV/XLSX (NetTopologySuite)

## Долгий ящик ⬜

Редкое: только когда всё выше сделано или кто-то попросит.

- ⬜ HWP/HWPX (корейский офис) и архивы ALZ, EGG
- ⬜ Кодировка CP949 (корейские субтитры)
- ⬜ SCC — телевизионные субтитры CEA-608 (байтовые коды)
- ⬜ OFD — китайские электронные счета
- ⬜ JPEG XR (WDP/JXR): нет кодека в Magick.NET, только через WIC
- ⬜ ICNS — иконки macOS (свой разбор PNG-блоков)
- ⬜ MacWrite, ClarisWorks, T602 и прочие древние текстовые форматы

## Порядок работы на каждом этапе

1. Форматы в `Formats.cs`, операции в `Catalog.cs`.
2. Тест в `tests/Sweetch.Tests`: хотя бы один файл каждого нового формата проходит конвертацию.
3. Self-test рендерит колесо для нового типа — проверить, что секторов не слишком много.
4. Обновить таблицу форматов в README и `THIRD-PARTY-NOTICES.md` для новых библиотек.
