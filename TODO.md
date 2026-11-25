# FileConverter TODO and project summary

This file summarizes the application work completed through version 1.1.6 and the
checks that remain before calling the Windows release fully verified.

## Completed

### Application shell and interfaces

- [x] Built the desktop application with C#, WPF, and .NET Framework 4.6.
- [x] Added navigation for Convert, Translate, Compress, Encrypt, Manual, History,
  and Settings.
- [x] Added English/French interface resources and light/dark theme resources.
- [x] Moved application information into Settings.
- [x] Added responsive layout behavior: compact top navigation for narrow or
  portrait windows, side navigation for wider windows, and smaller margins on
  narrow views.
- [x] Made the Manual topic cards and History summary cards reflow on narrow
  views; made primary action rows wrap instead of clipping.
- [x] Kept page content scrollable while operations run.

### Conversion

- [x] Added Word-to-PDF, PowerPoint-to-PDF, Excel-to-PDF, image-to-PDF,
  PDF-to-Word, PDF-to-images, and merge-to-PDF methods.
- [x] Added image export choices and image quality configuration.
- [x] Added multiple-file selection, drag and drop, per-file status, output
  location selection, progress reporting, cancellation, and numbered output names
  when a destination already exists.
- [x] Added local Office conversion support through the bundled LibreOffice
  runtime, with the existing Microsoft Office automation path where available.
- [x] Updated output naming to remove the old extension (for example,
  `image1.jpeg` becomes `image1.pdf`).
- [x] Merge-to-PDF uses a common page canvas so merged pages have consistent
  dimensions.

### Translation

- [x] Added automatic English/French document-language detection with a manual
  language fallback when detection is uncertain.
- [x] Added local, offline English-to-French and French-to-English translation
  using the bundled translation runtime and models.
- [x] Added PDF, DOCX, and TXT output choices; removed RTF from the output choices.
- [x] Preserved DOCX package media and styles while translating text.
- [x] Added PDF handling that preserves page artwork and geometry where possible,
  fits translated text, and creates continuation pages when text cannot fit.
- [x] Added progress reporting, cancellation, multiple-file handling, file sizes,
  and output location selection.

### Compression, encryption, and history

- [x] Added ZIP compression and decompression, with multi-file compression
  collected into one archive.
- [x] Added file-size display, output selection, progress, cancellation, and
  multiple-file support to compression.
- [x] Added encryption and decryption, duplicate-safe output names, progress,
  cancellation, and output location selection.
- [x] Added operation history with status and originating interface, including
  cancelled operations.

### Release and verification work

- [x] Set the source assembly and installer version to 1.1.6.
- [x] Rebuilt the Release WPF application with the responsive layout changes.
- [x] Built the offline Windows installer:
  [FileConverter-Setup-1.1.6.exe](FileConverter-Setup-1.1.6.exe).
- [x] Confirmed the staged application executable matches the Release executable
  and reports assembly version 1.1.6.0.
- [x] Validated the installer payload and checked the generated installer as a
  Windows PE/NSIS setup executable.
- [x] Passed the 12 existing packaging/document regression tests, including PDF
  translation/layout cases and translation server cancellation.
- [x] Checked XAML/config/project XML parsing and `git diff --check`.

## Remaining TODOs

- [ ] Install and launch version 1.1.6 on a clean Windows 10/11 x64 computer.
- [ ] Verify install, desktop and Start Menu shortcuts, upgrade over an older
  version, and uninstall.
- [ ] Exercise each interface manually on Windows, including drag and drop,
  output path selection, theme/language changes, scroll behavior, cancellation,
  and removing individual queued files.
- [ ] Check the responsive layout on portrait and landscape displays and at small
  window sizes, including all pages and both themes.
- [ ] Run representative PowerPoint, Word, Excel, image, PDF, ZIP, and encrypted
  files through the installed application; verify outputs and history entries.
- [ ] Test the bundled English/French models with real documents in both
  directions. The automated worker tests mock translation responses and do not
  measure translation quality.
- [ ] Confirm the Visual C++ runtime prerequisite installs correctly and that
  translation starts without a terminal window on a clean computer.
- [ ] Decide whether to bundle OCR for scanned PDFs. Currently they are detected
  and rejected with an OCR instruction.
- [ ] Review PDF output for complex layouts, unusual fonts, long translated text,
  forms, annotations, and multi-column pages.
- [ ] Consider reducing the installer size; it is about 668 MB because it includes
  LibreOffice, Python dependencies, and offline translation models.

## Known limitations to explain to users

- PDF is a fixed-layout format. Translation can change line breaks or pagination;
  continuation pages are used when translated text exceeds the available page
  space. Exact visual preservation cannot be guaranteed for every PDF.
- PDF-to-Word prioritizes preserving page appearance by placing page images in
  Word. That output is not equivalent to fully editable extracted text.
- DOCX can retain images and styles, but translated text may reflow within the
  document.
- Scanned PDFs need OCR before translation; OCR is not currently bundled.
- Some Office exports and image operations do not expose fine-grained native
  progress, even though the interface reports operation progress.
- Automated tests do not replace Windows GUI testing or validation with the
  user's own documents.
