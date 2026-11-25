# FileConverter release verification

## Current review: source 1.1.6

Checked in the current development workspace:

- Python packaging scripts compile successfully.
- All 38 XAML, project, and application configuration XML files parse.
- `git diff --check` passes.
- The packaging test suite passes: 12 tests, including document layout/translation
  fixtures and cancellation, using PyMuPDF 1.26.7, lxml 6.1.3, and python-docx 1.2.0.
- PDF-to-PDF translation is covered in both English-to-French and French-to-English;
  PDF-to-DOCX translation, expansion into clear page space, continuation pages,
  and the scanned-PDF OCR error path are also regression-tested.
- The staged `.offline-build/release115/FileConverter` payload passes the current
  installer builder's required-file validation, including LibreOffice, the layout
  worker, translation models, and the VC++ redistributable.
- The .NET Framework 4.6 WPF Release build succeeds with the targeting references
  and markup compiler used for this package; the application and installer report
  version 1.1.6.

Still requires a Windows release machine:

- Install and launch the generated installer on clean Windows 10/11 x64, then
  verify translation, Office exports, cancellation, upgrade, and uninstallation.
- Exercise representative user PDFs and Office documents. Visual fidelity cannot
  be guaranteed for every font, layout, or unusually long translation.

Scanned PDFs without selectable text are detected and rejected with an OCR
instruction. The application does not currently bundle OCR, so users must run
OCR separately before translating those files. Complex fixed-layout PDF text may
still clip or shift after translation; choose DOCX when reflow is acceptable.

The remaining sections below are historical regression notes from versions 1.1.0
through 1.1.2; they are not a complete verification of the 1.1.5 Windows release.

## Historical verification (1.1.0)

Passed in the Linux development environment:

- .NET Framework 4.6 WPF Release compilation with C# 6, including XAML/BAML.
- Disconnected Office cleanup regression (0x80010108).
- Authenticated encryption + decryption round trip of a multi-file ZIP.
- Duplicate ZIP entry basenames retained as numbered entries.
- Intermediate monotonic byte progress for ZIP creation.
- Cancellation removes encryption/ZIP partial output, without publishing it.
- ZIP path traversal rejected; corrupt archive does not stop the next input.
- Existing outputs retained with numbered copies.
- Actual C# document runner parses page progress and kills its worker on cancel.
- Offline English/French document translation against the local Argos API.
- Copied development launcher resolves models from an installed runtime (both directions).
- DOCX translation: media, styles, numbering, package relationships byte-identical;
  paragraph styles, run properties, drawings, tables, page breaks preserved.
- PDF translation: all three page dimensions, original embedded image bytes and
  vector artwork retained, translated selectable French text, page counters kept.
- DOCX-to-PDF and RTF-to-RTF translation: two pages, headings, table and image kept.
- Editable PDF-to-DOCX: three-page sample with text, images and actual tables.
- 50-page PDF-to-DOCX: 50 page sections and 50 image placements.
- 50-page PDF-to-images: 50 JPEG outputs with page progress.
- PDF merge: 53 pages with page progress, original images retained.
- Atomic cancellation removes partially rendered page directories.
- Rendered translated PDF and DOCX-to-PDF visually inspected.

Not verified on Windows:

- Full GUI interactions, theme/caret appearance, scroll behavior and cancellation.
- New installer installation/upgrade/uninstallation and native Windows binaries.
- The user's own failing documents (not provided).

Limits:

- Office export APIs and some native image operations expose no continuous page
  progress. They show a busy indicator; cancellation can wait for API return.
- PDF is a presentation format. Layout reconstruction/font substitution/fitting
  may change complex documents. Translation now expands into detected clear space
  and appends continuation pages when necessary; this changes pagination for
  overflow paragraphs. DOCX retains structure but text can reflow.
- Scanned text needs OCR. Text-only export cannot retain images or styling.

## 1.1.1 conversion and translation follow-up

- PDF-to-Word uses the bounded worker pool instead of the Office COM queue. Two concurrent 50-page fixture conversions preserved 50 sections and images each.
- Translation queue uses its own scroll viewer, removing the nested queue scroll container. Windows scrolling/visual QA remains required.
- Cancelled conversion batches stop their busy animation. Late worker progress cannot revive completed/failed rows; merge progress only updates prepared inputs. Failures retain the last actual file percentage.
- Translation retries reset row progress. Source language now respects detection when a target is explicitly selected; same-language/same-format exports are byte-identical and require no translation server. Unknown detection retains manual EN/FR fallback.
- Translation preserves leading whitespace in text runs; regression tests cover Word headings, bold runs, tables, breaks, package assets, repeated phrases and page counters. Run with the document Python environment: `python -m unittest discover -s packaging/tests`. Translation engine responses in these unit tests are simulated; they test document handling, not linguistic quality.
- PDF-to-image worker produced all 50 fixture pages. VS2015-compatible C# 6/.NET 4.6 Release build passes.
- PDF-to-Word uses high-resolution page images and matching page dimensions to preserve visible appearance; the output page content is not directly editable as Word text. Windows installer execution, GUI interaction and user documents still require Windows validation.

## 1.1.2 interface and document preservation

- Conversion shows per-file progress plus an overall batch bar, as required by the conversion specification. Translation shows a progress bar and percentage on each document row, without a duplicate batch bar. Compression and encryption each show one operation progress bar. Cancel shares the action row; completion/cancellation clears processed queues after the result dialog, with saved paths retained in History. Encryption also clears passwords.
- Output paths wrap in controls with sufficient height. About moved into Settings with capabilities, local processing, limitations, component credits and actual application version.
- Compression exposes Compress/Decompress; multiple compression inputs automatically produce one ZIP. Translation offers PDF, DOCX and text-only TXT, with layout handling built into supported exports. Detection metadata and the separate original-layout choice are hidden.
- Merge uses a common canvas based on the largest visible dimensions, centering smaller pages without stretching/cropping. Tests cover rotated/cropped pages, cover color, form values, annotations and internal/external links. Original files remain unchanged.
- PDF translation keeps separate styled text areas instead of applying one dominant style to a whole block. Word/PDF exports request lossless PDF image compression. PowerPoint PDF export includes hidden slides. Image-to-PDF iterates all source frames/pages with cancellation checks and progress.
- Real local Argos tests passed for DOCX-to-DOCX/PDF, PDF-to-PDF/DOCX, RTF-to-DOCX and reverse French-to-English translation. Images, tables, headings and non-text DOCX package parts verified; translated PDF and Word-to-PDF pages visually reviewed.
- LibreOffice fixture exports passed for PowerPoint (cover plus hidden slide) and Excel (two worksheets). Core archive/encryption cancellation and document worker cancellation tests passed. TXT/DOCX/PDF text writer regression passed after cleaning prior test-only outputs.
- Windows GUI/installer execution, native Office fallback, and System.Drawing image operations have not been executed on Windows here. Complex PDF reconstruction, font substitution, long translated passages and scanned documents remain limitations; no claim of universal fidelity or a fully verified Windows interface.
