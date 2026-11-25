# FileConverter application guide

**Source reviewed:** FileConverter 1.1.5 source tree
**Application type:** Windows desktop application (WPF / C#)  
**Intended framework:** .NET Framework 4.6; project files target Visual Studio 2015 tooling

## 1. What the application does

FileConverter is a desktop utility for preparing local files. It provides document and image conversion, English/French document translation, ZIP compression and extraction, password based file encryption/decryption, and an operation history. The application also has a manual and settings for its language and appearance.

It is intended for a user who needs to convert or protect files on their Windows computer without sending them to a hosted conversion service. Translation runs through a local LibreTranslate/Argos runtime when that packaged runtime is available. The UI can start and stop the bundled local translation server itself; users should not need to keep a terminal open when using a complete offline package.

## 2. Main interfaces

### Convert

The Convert screen supports file picking and drag-and-drop, multiple selected files, a chosen output folder, conversion selection, output opening, per-file state and progress, overall progress, cancellation, and history recording. The source enumerates these conversion methods:

- Word to PDF
- Excel to PDF
- PowerPoint to PDF
- Image to PDF
- PDF to Word
- PDF to images
- Image format conversion (JPG/PNG/BMP options)
- Merge multiple PDF files into one PDF

Image quality is configurable for the conversion paths that expose it. Ordinary conversions run with a concurrency limit of two; Office COM work is serialized on its own STA thread. A merge needs at least two source files. Existing output names are handled by the batch output helper, which selects a numbered name rather than overwriting the source output.

The output format does not always mean pixel-identical appearance. PDF-to-Word prioritizes visual fidelity by placing a high-resolution image of each original page on a Word page with matching dimensions; the page content is not directly editable as Word text. Translated PDF-to-Word follows the same approach after translating the PDF page content. TXT is plain text and cannot retain images, fonts, or page layout. Conversion capabilities also depend on the selected conversion engine/runtime included in the release.

### Translate

The Translate screen accepts multiple PDF, DOCX, RTF, and TXT documents. It shows file size and detected language, permits the user to choose English or French as the target, and allows output as PDF, DOCX, or TXT. RTF remains a supported input format; it is not offered as an output format. Source language detection is automatic where enough readable text is available; a user can select a language when detection is unavailable or incorrect. The output folder can be selected, and each queued document shows status/progress. Translation can be cancelled, and results are recorded in History.

The translation engine is local English↔French Argos via the app-managed LibreTranslate-compatible service. The app checks availability/model directions and can start its packaged server. A full installer must include its `TranslationRuntime`, models, and document workers for this to work independently of another computer installation.

Layout preservation varies by format and operation:

- DOCX translation edits document text while retaining the package structure and non-text assets such as styles, tables, images, and relationships where supported. Translated text can still reflow.
- PDF-to-PDF translation replaces selectable text in page layout while retaining page artwork and images where supported. Long translated text, unavailable fonts, or tight boxes can change text fitting.
- PDF-to-DOCX preserves the visible page composition with high-resolution page images; the page content is not directly editable.
- PDF-to-TXT necessarily drops page layout and images because TXT stores plain text only.
- TXT contains text only and cannot preserve images, styling, or page layout.
- Scanned/image-only PDFs need OCR before their text can be detected or translated; OCR is not implemented by the current source path.

### Compress

Compress accepts files through the picker or drag-and-drop and displays names, sizes, paths, and operation status. One input is archived as a ZIP; multiple selected inputs are placed together into one ZIP archive. Decompress extracts a ZIP to the chosen destination. The service tracks byte-copy progress and checks for cancellation. It rejects ZIP entries that attempt to write outside the selected extraction folder. Duplicate file names inside a new archive are made unique with numbered names.

### Encrypt

Encrypt supports both encryption and decryption, multiple input files, password and confirmation fields for encryption, output-folder selection, progress, and cancellation. The implementation uses AES with a random salt and IV and an authentication tag; it derives encryption/authentication keys from the password using PBKDF2. Decryption verifies the authentication tag before publishing the restored file, so a wrong password or modified encrypted file fails. Existing destinations are not silently overwritten.

Combined-file/individual behavior is controlled in the interface workflow: where files are grouped into an encrypted archive, the encrypted output is an archive that can subsequently be decrypted and decompressed. Keep the password safe; the app cannot recover it.

### History

History displays recorded operations with counts, search, status filters, and input/output paths. The history screen loads and saves XML records under the current user's application data area (the service uses `%AppData%\FileConverter\history.xml`). It records operation outcomes and file paths; it does not archive the original files themselves.

### Manual

Manual provides searchable help topics and step-by-step articles for getting started, conversion, history, compression, encryption, and settings. It is built into the application rather than downloaded.

### Settings and About

Settings contains light/dark appearance, English/French UI language, and About information. On first launch, theme and language managers inspect system preferences and use those as defaults; an explicit saved user preference takes precedence. The UI strings live in the English and French resource dictionaries. About describes the product, capabilities, offline/local processing and format limitations.

## 3. Typical workflow

1. Open the required tool from the left navigation.
2. Add one or more files with **Choose Files** or drag them into the drop area.
3. Review the file list, detected language (Translate), and selected operation.
4. Choose the output location and other settings such as image quality or password.
5. Start the operation. Use the progress display and **Cancel** while it runs.
6. Open the saved output from the Convert results when available, or use History to find the recorded output path.

The source uses temporary output paths and publishes completed results after work succeeds. Failed/cancelled work should not be treated as a completed output. Inputs are not meant to be modified in place.

Convert, Translate, Compress, and Encrypt screen instances are retained by the main window after first use. Moving to another screen while an operation is running therefore does not create a fresh queue for the screen you return to. When an operation finishes, the relevant screen clears its processed queue as part of its completion flow; saved output paths remain available from the completion message and History.

## 4. Technology used and how the application is put together

### Desktop and user interface

The interface is a Windows Presentation Foundation (WPF) application written in C#. WPF uses XAML for windows, controls, layout, resource dictionaries, event wiring, and data binding. Each major screen has a XAML file for presentation and a C# code-behind file for actions such as file selection and starting an operation. The main window hosts one screen at a time and the left sidebar switches between screens.

The project targets .NET Framework 4.6 and uses the Visual Studio 2015 style of project files. It is not a web application: it is launched as a Windows executable and reads/writes local files. The app uses asynchronous tasks for long-running work, cancellation tokens for cancellation, and observable/bindable models so the screen can refresh statuses and progress.

### Main libraries and engines

- **PdfPig 0.1.8** reads text and page information from PDFs in the C# application.
- **System.Drawing** performs raster image loading and JPG/PNG/BMP encoding.
- **UglyToad.PdfPig.Writer** creates PDFs from image pages.
- **LibreOffice** is the first choice for Office file exports in the packaged runtime. If unavailable, Word/Excel/PowerPoint exports can use installed Microsoft Office automation (COM), depending on the method.
- **PyMuPDF (Python package `pymupdf`)** reads, merges, and rasterizes PDFs in the packaged worker.
- **PyMuPDF** renders fixed-layout PDF pages for high-fidelity Word output and performs layout-preserving PDF translation.
- **python-docx / lxml** are used by the document worker to edit DOCX package XML while leaving unrelated package parts intact.
- **Argos Translate** supplies the English↔French offline translation models. The app communicates with the local service over loopback HTTP, not with a public translation endpoint.
- **NSIS** packages a staged Windows payload into a setup executable. The installer build is separate from compiling the Visual Studio solution.

These tools are not all contained in the C# source. A distributable offline package must include their compatible Windows binaries, Python libraries, models, worker scripts, license notices, and any required runtime prerequisites. A source checkout can therefore compile while still lacking the runtime files needed for particular document conversions or offline translation.

### How a file moves through the application

1. A screen receives paths from the file picker or Windows drag-and-drop.
2. It checks that each file exists and that its extension is supported for the selected operation. Translation also reads the file's size and text for the language preview.
3. The screen validates operation-specific inputs: for example, a merge needs at least two PDFs, translation needs a target direction, and encryption needs a confirmed password.
4. The screen starts an operation session and passes its cancellation token to the processing service.
5. The processing service selects the local library, external worker, LibreOffice, or Microsoft Office path appropriate to the operation. Worker processes have their output/error streams observed, and the C# service translates worker page/stage reports into UI progress.
6. Output is written to a temporary file or directory. Once the operation succeeds, the batch helper moves it into the destination folder. If the preferred output name already exists, a numbered copy is selected so an existing file is preserved.
7. The UI reports success/failure and records the result in History. A cancellation or exception prevents incomplete temporary output from being published as a successful result.

Operations can run asynchronously so the window can remain interactive. Progress precision depends on the underlying tool: byte-copying, translation batches, and rendered pages can report measured progress; a native Office export may only be able to report that it is busy until Office returns. Cancellation is cooperative and can take until a native API call returns. This is why the app should not promise a continuously changing percentage for every engine and every file.

## 5. Conversion screen: methods and processing details

The conversion method is selected first. Then the user adds one or more files. The method choice is applied to every selected file; the screen checks each extension against the method before processing. For a mixed selection, files that do not match the selected method are reported as failures instead of being silently passed to an unrelated converter. Ordinary file conversions are run with up to two conversion slots, while Office COM automation is sent through a dedicated single-threaded apartment queue because those APIs require thread affinity. The merge method is a single grouped operation over all queued PDFs.

| Selected method | Accepted source | What the application does | Output and fidelity notes |
| --- | --- | --- | --- |
| Word → PDF | `.doc` / `.docx` | Calls bundled LibreOffice headlessly using its Writer-to-PDF export filter. If that runtime is unavailable, it uses installed Microsoft Word automation. Documents are opened read-only, and macros/link updates are suppressed where the API allows. | A PDF is written. Page layout depends on the office engine, fonts installed, and document features. |
| Excel → PDF | `.xls` / `.xlsx` | Uses LibreOffice Calc PDF export first, then installed Excel automation as a fallback. The workbook opens read-only, with external link updates and macros disabled. | Workbook sheets are exported through the Office engine. Print areas, scaling, page breaks, and fonts affect the resulting pages. |
| PowerPoint → PDF | `.ppt` / `.pptx` | Uses LibreOffice Impress PDF export first, otherwise Microsoft PowerPoint automation. The Office export path asks PowerPoint to save as PDF. | The packaged LibreOffice export enables hidden slide export. Presentation geometry/theme depend on the source and rendering engine. |
| Image → PDF | JPG/JPEG, PNG, BMP, GIF, TIF/TIFF | Opens the image, enumerates its frames, adds one PDF page per frame, sizes the page using the image pixel dimensions and DPI, and embeds each frame as PNG data. | Lossless PNG embedding avoids adding JPEG encoding artifacts. Animated or multipage image formats can produce multiple PDF pages. |
| PDF → Word | `.pdf` | Starts the layout worker's `to-word` path. The worker renders every PDF page as a lossless, high-resolution image and places it on a Word page with matching dimensions. | Preserves visible page composition and page count. Text and tables are not directly editable as Word content. |
| PDF → images | `.pdf` | In a complete package, the PDF worker renders each page at 150 DPI using PyMuPDF and writes sequential JPEGs. If that worker is unavailable, the C# service can call Poppler `pdftoppm.exe` (a configured path can be set as `PdfToPpmPath`). | Output is a folder containing one JPEG per page. The quality slider controls JPEG quality. Rendering resolution is currently fixed at 150 DPI. |
| Merge PDFs | Two or more `.pdf` files | The document worker inserts source PDF pages in the selected order. It finds the widest and tallest visible page dimensions across the inputs, centers smaller pages on that common canvas without scaling, and retains vector/text content, images, links and form data where supported. | One PDF. Page sizes are normalized to the common canvas; smaller pages are centered, not stretched. Password-protected PDFs must be unlocked first. |
| Image → JPG | Single-frame image | Loads the image, draws it on a white-backed 24-bit bitmap, and encodes JPEG with the selected quality value. | JPEG is lossy; transparency is flattened to white. Multi-frame input is rejected on this path. |
| Image → PNG / BMP | Single-frame image | Loads the image and asks System.Drawing to encode it as PNG or BMP. | This path rejects multi-frame images. Format encoding can affect metadata, color profile, and alpha behavior. |

At completion, each converted file row gets a status, output name, and open-output action (where applicable); the overall indicator summarizes the rows. The merge output is based on the queued input set. History records the input/output path and whether that conversion succeeded. The output folder control can use the default folder derived from the inputs or a user-selected folder.

### What “keep the original layout” means for conversion

Some conversions can preserve the same visible page dimensions, but changing file formats cannot always preserve every editable object exactly. Office-to-PDF relies on an Office renderer. PDF-to-Word uses page images to retain visual composition, so the page content is not directly editable as text. PDF-to-TXT necessarily contains text only. Image conversion necessarily changes the encoded representation, and JPG compression may alter pixels. PDF-to-images produces page pictures rather than editable text. The merge worker specifically preserves page artwork while normalizing page canvas dimensions; it does not rescale smaller pages.

## 6. Translation screen: detection, translation and format handling

### What happens when a file is added

The screen accepts `.docx`, `.pdf`, `.txt`, and `.rtf`. Duplicate paths already in the queue are ignored; unsupported extensions are counted and shown to the user. For each accepted file the screen displays its extension and file size, then extracts readable text and samples up to 16,000 characters for language detection. A detector built from the embedded character n-gram profiles classifies text such as English or French. It needs enough letters and words; a very short, empty, image-only, or otherwise unreadable document is shown as unknown/not detected.

PDF extraction uses selectable PDF text. DOCX detection reads text from `word/document.xml`; RTF detection uses WPF's RichText `TextRange` loader on an STA thread; TXT is read as UTF-8 with a Windows-1252 fallback. Detection does not translate the document. It only suggests the source direction and the opposite English/French target. If the detector cannot choose, the user can select a target manually. When the source was unknown, the selected target determines the assumed opposite source (e.g., choosing French means the app assumes English source). This can be wrong if the document is actually in another language.

### What happens when Translate is pressed

1. The app validates the selected files and output folder, then creates a cancellable session.
2. For files with extractable text, it obtains the complete text needed by the chosen operation. The queue caches extracted text while file length and modified timestamp remain unchanged, so repeated work need not re-read unchanged input.
3. If source and target differ, it checks the local translation service. If the bundled local runtime exists, it starts it automatically and waits for English↔French models to be ready. It also supports a configured or installed LibreTranslate executable as a fallback. No terminal needs to stay open when a complete bundled runtime is installed.
4. Text is divided into bounded batches, identical fragments can be served from a cache, and progress is reported as batches/characters complete. Requests are sent only to the configured local loopback address.
5. Depending on source and requested output format, the document worker either updates an existing layout-aware format or converts/rebuilds first, then writes the requested PDF, DOCX, or TXT result.
6. The result is moved from a temporary path into the selected output directory, the status and History are updated, and the UI presents a completion summary.

For layout-worker document paths, if source and target are the same and the output extension is the same as the input extension, the worker copies the document unchanged. Text-only paths still extract and write text into the requested result format. If no text can be extracted from a PDF, the operation reports that OCR is required; it does not send page images to the language model.

### Translation format matrix

| Input | Output | Processing path | What can remain the same / limitations |
| --- | --- | --- | --- |
| DOCX | DOCX | Translates text in paragraphs across body, headers, footers, footnotes and endnotes, grouping text by run style; then writes edited XML entries back into the original ZIP package. | Unedited package files (images, styles, numbering, relationships and other parts) are retained. Run/paragraph formatting, drawings and tables are preserved. Changed text may wrap differently. |
| DOCX | PDF | Translates to DOCX, then exports with the bundled office engine. | Source document features are retained in the intermediate DOCX; PDF pagination/fonts can differ after rendering. |
| DOCX | TXT | Extracts document text, translates it, then writes plain text. | Text only. Word styling, images, headers and page layout are not represented in TXT. |
| PDF | PDF | Extracts selectable text by styled page regions, translates it, redacts original glyphs, and inserts translated text into the same page areas. Original page dimensions and artwork are retained; overlapping image placements are restored. | Most page art and images remain in place. Text is fitted into its original area, so long translations may fail rather than clip; rotated text is rejected. Some font substitution or text-size reduction may occur. |
| PDF | DOCX | Translates selectable PDF text in place, then renders each translated page as a high-resolution image on a matching Word page. | Preserves translated page appearance; page text is not directly editable. Password-protected or scanned PDFs need unlocking or OCR first. |
| PDF | TXT | Extracts selectable text, translates, and writes plain text. | No images, typography, page geometry, or layout. |
| RTF | DOCX / PDF | Uses the bundled office engine to convert RTF to DOCX, translates its content, then either returns DOCX or exports PDF. | Formatting can be retained through the office conversion but may change according to renderer behavior. RTF is input only. |
| RTF | TXT | Reads RTF as formatted text through WPF, translates the extracted text, and writes plain text. | Text only. |
| TXT | TXT / DOCX / PDF | Reads text, translates it, then writes the chosen output format. DOCX/PDF are newly generated text documents. | Since TXT has no layout, there is no original image, page design, or paragraph styling to carry over. |

The translator sends text fragments, not the full binary document, to the local translation API. Its interface is `LanguageDetectionService`; the page-aware layout work is handled by `layout_worker.py`. The app refuses non-loopback endpoints in its service validation, so this design is intended for local/offline processing. In addition to a bundled runtime, startup can use a path configured as `LibreTranslatePath`, an executable beside the app, the user's `libretranslate-env` installation, or an executable found on `PATH`. Missing runtime/model files, a stopped server, unsupported PDF text, corrupt input, or a translation timeout is reported on the affected queue row; the batch can continue to other files where the UI's loop permits.

## 7. Compression and encryption: detailed behavior

### Compression

The Compress screen displays file name, size and path before processing. The operation dropdown has Compress and Decompress. Compression uses `CompressionLevel.Fastest` to favor speed. With one file, it creates `<input-name-without-extension>.zip` next to the input or under the selected folder; with multiple files, it creates one `archive.zip` containing all selected files. Decompress extracts each selected archive into `<archive-name-without-extension>.extracted`. Duplicate basenames in a combined ZIP receive suffixes such as `(2)` to keep every entry.

The service copies archive data in 81,920-byte chunks and updates progress as bytes are copied. During extraction it normalizes entry paths, confirms each resulting path remains under the chosen extraction root, and creates each file with `CreateNew` so an existing extracted file is not silently overwritten. Invalid or traversal-style archive entries fail safely. Cancellation is checked between chunks and entries; the batch runner catches a per-file error and can continue with subsequent selected files.

### Encryption and decryption

The user can encrypt each file separately or choose a single encrypted archive. Individual encryption creates `<original-name>.<original-extension>.encrypted`; keeping the original extension in this encrypted suffix lets decryption restore the original filename. Individual decryption removes `.encrypted` and restores the source filename. For a combined encrypted archive, the app first creates a temporary ZIP of all selected files, then encrypts the ZIP to `files.zip.encrypted`, and deletes the temporary ZIP afterward. Decrypting that file restores the ZIP; use Compress → Decompress to extract its contents.

The encrypted file format stores the `FCENC002` header, a random 16-byte salt, a random 16-byte IV, AES ciphertext, and a 32-byte HMAC-SHA256 authentication tag. A 200,000-iteration PBKDF2 derivation creates separate encryption and authentication keys. Decryption authenticates the data before writing plaintext. Wrong passwords, altered contents, truncated data, unknown headers, input/output path collisions, and existing output names fail instead of silently replacing content. Key buffers are cleared when work completes. The UI requires at least eight characters and a matching confirmation during encryption, but password strength and safe password storage remain the user's responsibility.

## 8. Progress, cancellation, output, history and preferences

### Progress and cancellation

Each view has a shared operation session and cancellation token. The Convert screen aggregates per-file progress; compression and encryption report byte-based progress; Python PDF workers report completed pages; translation reports batches/text or pages and includes stage labels such as reading, translating, reconstructing, or exporting. Some setup work is indeterminate (for example, waiting for the local translation engine). If an underlying Office COM call does not expose progress, the interface can only show a busy state until it returns.

Cancel requests cancellation; the active service checks that request at safe points and stops managed worker processes where possible. For a native library or Office operation that cannot be interrupted while inside a call, cancellation can be delayed. An operation completed before the cancellation request may still remain as a valid output; an unfinished temporary output is cleaned up.

### Output naming and history

The output picker provides a default derived from the input folder and a custom folder option. Batch operations write to temporary names, then move the completed item into place. If the chosen final name already exists, the app tries numbered alternatives such as `report (2).pdf`. This preserves previous output files and also prevents output from overwriting the selected source.

History is a lightweight XML log, not a backup. It records input name, input and output formats, input/output paths, local date/time, and completed/failed state. It stores no copy of the documents and does not provide file recovery. Settings are saved separately in `%AppData%\FileConverter\settings.xml`, including theme and interface language. The detector chooses system language from the Windows current UI culture, and the theme detector reads the current-user `AppsUseLightTheme` registry value. Explicit selections override those defaults. The current process uses local files and a loopback endpoint for translation; it does not upload documents to a cloud service.

## 9. Build and installer relationship

There are two different deliverables:

1. **Visual Studio build:** compiles the C# WPF application and core library into `FileConverter.exe` and supporting DLLs. This build by itself does not necessarily include the large offline Python runtime, translation models, LibreOffice, Poppler, or Visual C++ prerequisite.
2. **Offline Windows installer:** a release staging step gathers the compiled app, Python runtime, pinned packages, Argos language models, worker scripts, Office runtime and prerequisite/license files; `build_offline_release.py` creates the portable payload, then `build_windows_installer.py` validates that payload and invokes NSIS. The resulting setup can start translation without the user opening a terminal, as long as it includes the complete runtime and the computer meets the Windows requirements.

The packaging documentation includes Windows x64 staging/build commands and a release checklist. It explicitly requires installation, upgrade, uninstall and offline translation checks on Windows before distribution. Linux compilation or component-level tests do not prove the installer or WPF interface runs correctly on Windows.

## 10. Project structure and file inventory

### Solution and project files

| File | Purpose |
| --- | --- |
| `FileConverter.sln` | Visual Studio solution containing the WPF application and its core class library. |
| `FileConverter/FileConverter.csproj` | WPF executable project: target framework, icon, references, XAML and C# compile/resource entries. |
| `FileConverter.Core/FileConverter.Core.csproj` | Core class library project and embedded language-profile resource. |
| `FileConverter/App.config` | Runtime settings, including local translation endpoint and optional tool paths; declares .NET Framework 4.6. |
| `FileConverter/packages.config` | NuGet dependencies for the app, including PdfPig and System.ValueTuple. |
| `FileConverter.Core/Core14.profile.xml` | Embedded n-gram language profiles used for lightweight English/French text detection. |
| `FileConverter/AppIcon.ico` | Executable/window icon asset. |

### Application startup, navigation and shared controls

| File | What it does / key functions |
| --- | --- |
| `FileConverter/App.xaml` | Declares the WPF application and initial light/input style dictionaries. |
| `FileConverter/App.xaml.cs` | Initializes theme and language, forwards mouse-wheel scrolling to a parent scroll viewer at nested-view boundaries, then cancels operations/stops workers and local translation on exit. |
| `FileConverter/MainWindow.xaml` | Main window shell, navigation sidebar, page host, and settings/navigation buttons. |
| `FileConverter/MainWindow.xaml.cs` | `NavigateTo` changes the displayed view; sidebar handlers navigate to Convert, Translate, History, Manual, Compress, Encrypt, and Settings; manages collapsed sidebar and language refresh. |
| `FileConverter/Controls/OutputLocationPicker.xaml` | Shared output folder selector with default/custom location controls. |
| `FileConverter/Controls/OutputLocationPicker.xaml.cs` | `TryGetFolder` validates/returns the chosen destination; `SetSources` derives a default folder from inputs; browse and selection handlers refresh its display. |
| `FileConverter/Services/OperationSession.cs` | Per-view cancellation state. `Start`, `Finish`, `IsCurrent`, and `CancelAll` manage cancellation tokens and stale progress; exposes a WPF Cancel command. |
| `FileConverter/Services/FileProgressState.cs` | Reusable bindable progress percentage/status state for file rows. |
| `FileConverter/Services/BatchOperations.cs` | Runs file batches with progress/cancellation, assigns safe unique output names, writes to a temporary path, and publishes output after success. |
| `FileConverter/Services/RuntimePaths.cs` | Locates bundled runtime folders and worker scripts in development or packaged layouts. |
| `FileConverter/Properties/AssemblyInfo.cs` | App assembly title, product, COM visibility, WPF theme metadata and version (1.1.5.0). |
| `FileConverter/Properties/Resources.resx` | Strongly typed application resource container. |
| `FileConverter/Properties/Resources.Designer.cs` | Generated accessor for resources in `Resources.resx`. |
| `FileConverter/Properties/Settings.settings` | Visual Studio user-settings schema; currently contains no declared settings. |
| `FileConverter/Properties/Settings.Designer.cs` | Generated settings accessor. |

### User interface views

Each view has a `.xaml` layout file and a `.xaml.cs` code-behind file. XAML defines controls, binding, styles and event wiring; code-behind performs UI validation, file selection, operation orchestration and status updates.

| View files | Main responsibilities and key handlers |
| --- | --- |
| `FileConverter/Views/ConvertView.xaml` and `.xaml.cs` | Conversion choices, quality, drag/drop, multi-file list, per-file/overall progress, output folder and Cancel. `Method_Checked` selects a method; `ConvertFiles_Click` validates and starts a batch; `ConvertAsync` and `MergeAsync` run conversion/merge; `PrepareMergeAsync` stages merge inputs; `RunConversionAsync` dispatches work; `ValidateInput` checks formats; `Finish` updates result/status; picker/drop/remove/clear/open-output handlers manage files. |
| `FileConverter/Views/TranslateView.xaml` and `.xaml.cs` | File upload, size/language metadata, target language, PDF/DOCX/TXT output, progress, output folder and cancellation. RTF is accepted only as input. `AddFilesAsync` validates and inspects files; `InspectDocumentAsync` extracts text and detects language; `TranslateFiles_Click` ensures local engine, translates, writes output and records results; `ReadRtfAsync`, `ReadDocument`, `ReadText`, and `ReadPdf` extract text; `ShowDocumentProgress` updates operation progress; `TranslationFileItem` provides bindable file/language/status state. |
| `FileConverter/Views/CompressView.xaml` and `.xaml.cs` | Compression/decompression controls, upload list, file sizes, destination, progress and cancellation. `AddFiles`, drag/drop and selection handlers populate the queue; `UpdateInterface` and output-location handlers refresh enabled/display state; `CompressFiles_Click` executes the selected archive action. |
| `FileConverter/Views/EncryptView.xaml` and `.xaml.cs` | Encrypt/decrypt controls, password fields, file list, output settings, progress and cancellation. `ProcessFilesAsync` performs each file operation; `EncryptButton_Click`/`DecryptButton_Click` select direction; `DecryptedName` derives a restored filename; picker/drop/remove/clear handlers manage inputs. |
| `FileConverter/Views/HistoryView.xaml` and `.xaml.cs` | History table, counts, search and status filters. `Record` persists operation results; `LoadHistory` reads XML; `FilterHistory`/`SearchTextBox_TextChanged` filter records; `HistoryItem` exposes display properties. |
| `FileConverter/Views/ManualView.xaml` and `.xaml.cs` | Topic cards, help search, article display and back navigation. `LoadTopics` and `InitializeTopicCards` construct help data; `DisplayArticle` renders a topic; search/topic/back handlers navigate manual content. |
| `FileConverter/Views/SettingsView.xaml` and `.xaml.cs` | Appearance, language and About panel. `LoadSettings` reads saved/default choices; `SaveSettings` persists settings; `ApplyInitialSelections`, theme and language handlers apply them to the app. |

### Shared visual/localization services

| File | Purpose |
| --- | --- |
| `FileConverter/Themes/LightTheme.xaml` | Light color and control resources. |
| `FileConverter/Themes/DarkTheme.xaml` | Dark color and control resources. |
| `FileConverter/Themes/InputStyles.xaml` | Shared input/control styling used across views. |
| `FileConverter/Resources/Languages/English.xaml` | English UI string dictionary. |
| `FileConverter/Resources/Languages/French.xaml` | French UI string dictionary. |
| `FileConverter/Services/ThemeManager.cs` | `Initialize` chooses saved/system theme; `ApplyTheme` swaps theme resources; `GetSystemTheme` reads the OS preference. |
| `FileConverter/Services/LocalizationManager.cs` | Detects system language, loads English/French dictionaries, applies explicit preferences, reads/writes user config and supplies translated UI strings. |

### Conversion, translation and document processing services

| File | Purpose / key functions |
| --- | --- |
| `FileConverter/Services/DesktopConversionService.cs` | Desktop conversion engine. `Convert` dispatches conversion methods; `MergePdf` combines PDF pages; `ImageToPdf` embeds image pages; `ReadPdfText` extracts selectable PDF text; `RunDocumentWorker`/`RunTool` launch packaged document workers and relay progress/cancel; `RenderPdf` creates PDF page images; Office helpers create/close COM applications and export PowerPoint/Word/Excel. |
| `FileConverter/Services/TranslatedDocumentWriter.cs` | Writes translated text into TXT, DOCX or PDF output for text-oriented paths; includes Word package writing and PDF line wrapping/page writing. Layout-aware document translations are delegated to the document worker. |
| `FileConverter/Services/LocalTranslationServer.cs` | `EnsureReadyAsync` checks the local endpoint and starts the bundled server if needed; `Stop` closes the process started by this app; builds process startup settings and locates the runtime executable. |
| `FileConverter.Core/Services/LanguageDetectionService.cs` | HTTP client for local LibreTranslate-compatible `/languages` and `/translate`; verifies both English↔French model directions, batches text, reports progress, handles errors and cancellation. |
| `FileConverter.Core/Services/DocumentLanguageDetector.cs` | Uses embedded character n-gram profiles to identify language from extracted text; returns null when the sample is too short to classify. |
| `FileConverter.Core/Services/ConversionService.cs` | Core image conversion through System.Drawing for PNG, JPG and BMP; rejects unsupported output names and multi-frame sources. |

### Compression, security and core data/contracts

| File | Purpose |
| --- | --- |
| `FileConverter.Core/Services/CompressionService.cs` | Creates one ZIP from one or more input files and extracts ZIP archives. Reports byte progress, checks cancellation, uniquifies duplicate archive names, and blocks unsafe extraction paths. |
| `FileConverter.Core/Services/FileEncryptionService.cs` | Encrypts/decrypts files using AES and HMAC authentication; derives keys from passwords, streams data with progress, rejects existing output, detects tampering/wrong passwords, and removes partial output on failure/cancel. |
| `FileConverter.Core/Services/FileDecryptionService.cs` | **Empty placeholder class**; decryption is implemented by `FileEncryptionService.DecryptFile`, not here. |
| `FileConverter.Core/Services/HistoryService.cs` | **Empty placeholder class**; active history persistence is implemented by `HistoryView`. |
| `FileConverter.Core/Interfaces/IFileConverter.cs` | Contract exposing `ConvertImage(input, output, format)`. |
| `FileConverter.Core/Interfaces/IFileCompressionService.cs` | Contract for single-file compression. The concrete service additionally has batch, progress, cancellation and decompression methods. |
| `FileConverter.Core/Interfaces/IFileCompressor.cs` | **Not an interface yet**: this file contains an empty internal class named `IFileCompressor`. |
| `FileConverter.Core/Models/FileItem.cs` | Bindable file metadata model: ID, source/output names, extension, size, status and formatted size. |
| `FileConverter.Core/Models/OperationRecord.cs` | **Empty placeholder class**; current history rows are represented by `HistoryItem` in `HistoryView.xaml.cs`. |
| `FileConverter.Core/Enums/ConversionStatus.cs` | Status values: Pending, Validating, Waiting, Converting, Completed, Failed and Cancelled. |
| `FileConverter.Core/Enums/OperationType.cs` | **Empty placeholder class**, despite its filename. |
| `FileConverter.Core/Properties/AssemblyInfo.cs` | Core assembly metadata and GUID; core version is 1.0.0.0. |

### Packaging, runtime and release files

| File | Purpose |
| --- | --- |
| `packaging/BUILD.md` | Documents staging of Windows x64 Python/Argos/Office dependencies, portable release creation, NSIS setup creation, and platform requirements. |
| `packaging/FileConverter.nsi` | NSIS script for installing the staged application, shortcuts and uninstaller. |
| `packaging/build_offline_release.py` | Assembles the offline portable payload from the app build, embedded Python, dependencies, models, Office runtime and worker scripts. |
| `packaging/build_windows_installer.py` | Validates a staged payload, creates the exact uninstall manifest and invokes NSIS to produce setup. |
| `packaging/run_libretranslate.py` | Starts the local translation API backed by the bundled Argos models. |
| `packaging/document_worker.py` | Python document worker for conversion/extraction workflows using the staged document libraries. |
| `packaging/layout_worker.py` | Python worker for layout-aware DOCX/PDF translation and reconstruction tasks. |
| `packaging/translation-win-x64.lock.txt` | Pinned Windows x64 translation/document Python dependency versions. |
| `packaging/Offline Translation Test.cmd` | Windows command script for checking packaged offline translation components. |
| `packaging/START HERE.txt` | Short end-user notes shipped with an offline release. |
| `packaging/RELEASE-CHECKS.md` | Records component/test coverage, platform checks still needed, and known conversion/translation limits. |
| `packaging/tests/test_layout_worker.py` | Python regression tests for document layout worker behavior. |
| `packaging/tests/test_server_cancellation.py` | Python regression tests for local translation server cancellation. |
| `prompt file converter.txt` | A project prompt/reference text file; it is not compiled into the application. |

## 11. Important dependencies and runtime requirements

- The WPF app and core library target .NET Framework 4.6. The project was authored with Visual Studio 2015-era MSBuild settings.
- PdfPig is used to read PDF text and System.Drawing handles image operations.
- Desktop document conversion/translation workers use packaged Python libraries and Office/PDF tooling when those runtime files are present.
- Offline translation requires the bundled Windows-compatible Python runtime, LibreTranslate/Argos packages, both English↔French model files, and worker scripts. Source code alone does not include those large model/runtime binaries.
- The offline installer instructions target Windows x64 and call for .NET Framework 4.6 or later and the Microsoft Visual C++ runtime.

## 12. Known implementation gaps and limits

This guide documents the checked-in source; it is not a certification that every operation has been exercised on every Windows version or with every user document.

- Four core files are placeholders: `FileDecryptionService.cs`, `HistoryService.cs`, `OperationRecord.cs`, and `OperationType.cs`; the active behavior resides in other files as described above. `IFileCompressor.cs` is also an empty class rather than an interface.
- Exact visual preservation cannot be guaranteed for every conversion. PDF page-image output preserves appearance but limits text editing. Translated PDF text may not fit its original area and causes that file to fail rather than silently clip; complex Office layout, font substitution, scanned PDFs, and unsupported embedded objects remain risk areas.
- Automatic language detection needs enough extractable text and only has packaged language profiles; the document translation UI's supported source/target workflow is English/French.
- The release checklist explicitly distinguishes automated/component verification from Windows GUI, installer and user-document validation. Native Office behavior and Windows-specific interface behavior should be verified on the actual target computer before broad distribution.

