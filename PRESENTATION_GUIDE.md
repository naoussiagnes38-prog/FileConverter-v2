# FileConverter presentation guide

## Short introduction

FileConverter is a Windows desktop application for converting, translating, compressing, and protecting files. It is built with C# and WPF, and its screens are arranged as separate tools in one application. The user selects files locally, chooses an operation and destination, watches each file's status, then reviews the result in History. The offline installer bundles the application, its document workers, the English/French translation models, and the LibreOffice runtime.

The application works with files on the user's computer. Translation uses an application-managed local service and English/French models. A complete offline package does not require a cloud account or a terminal window.

## How the application is structured

The main window hosts the navigation sidebar and the selected screen. Each screen's XAML file defines its controls and layout; its C# code-behind validates input and starts the corresponding service. Shared services handle output naming, cancellation, progress, localization, theme selection, and history. Longer work runs away from the WPF UI thread so the window remains usable.

The processing flow is:

1. Add files with the file picker or drag and drop.
2. Validate file type and operation settings.
3. Select an output folder; the default is based on the input file or files.
4. Start an operation session and pass its cancellation token to the worker.
5. Write output to a temporary name. Publish it under the requested name only after success.
6. If that output name already exists, choose a numbered name so the existing file is preserved.
7. Update the file status and progress and record the outcome in History.

## Conversion screen

The user chooses a conversion method, adds one or more files, selects an output folder, and starts the job. The application checks that each file matches the selected method. A mismatch or unreadable file gets a failure status; other queued files can continue. Most batch conversions can process two files concurrently. Office automation fallback work is serialized because those APIs require a single-threaded apartment.

| Method | What happens | Output and format notes |
| --- | --- | --- |
| Word to PDF | LibreOffice Writer exports DOC or DOCX to PDF. If the bundled LibreOffice runtime is unavailable, Microsoft Word automation is used when installed. | Output is named from the input base name with `.pdf`. Fonts, pagination, and some Word-specific features depend on the renderer. |
| Excel to PDF | LibreOffice Calc exports XLS or XLSX to PDF, with Microsoft Excel automation as a fallback. | The workbook's print area, scaling, page breaks, and installed fonts affect the pages. |
| PowerPoint to PDF | LibreOffice Impress exports PPT or PPTX to PDF, with Microsoft PowerPoint automation as a fallback. | Slide content and theme are rendered by the selected Office engine. Hidden slides are included in the LibreOffice export configuration. |
| Image to PDF | Every frame of a supported image is placed on a PDF page. Page dimensions are derived from image dimensions and DPI, and the image data is embedded losslessly as PNG. | A source named `image1.jpeg` becomes `image1.pdf`. Multi-frame images can yield several pages. |
| PDF to Word | The PDF worker renders each page as a high-resolution image, then places each image on a Word page with matching dimensions. | Preserves visible page appearance and page count. Page content is image-based, so it is not directly editable as Word text. |
| PDF to images | PyMuPDF renders each PDF page at 150 DPI into a numbered JPEG file in an output folder. | The quality control sets JPEG quality. Each page becomes a separate image. |
| Merge into one PDF | Each supported PDF, Office document, or image is prepared as a PDF; the worker appends pages in queue order. Page canvases are normalized to the largest page dimensions, and smaller pages are centered without scaling. | The combined result is `merged.pdf` (or a numbered alternative). Page artwork is retained as much as the source conversion permits. |
| Image to JPG | A single-frame image is loaded and saved as JPEG with the selected quality. Transparency is flattened onto white. | JPEG compression can change pixels. A source named `photo.png` becomes `photo.jpg`. |
| Image to PNG | A single-frame image is encoded as PNG. | PNG is lossless for pixel data, though metadata and color profiles may not be retained. |
| Image to BMP | A single-frame image is encoded as BMP. | Metadata and color profiles may not be retained. |

The output name uses the input name without its old extension, followed by the new extension. For example, `image1.jpeg` converted to PDF becomes `image1.pdf`, not `image1.jpeg.pdf`. If `image1.pdf` already exists, the app chooses a numbered copy.

### Conversion fidelity and progress

Some conversions can retain the original page composition, but changing format cannot promise identical editable structure in every file. Office-to-PDF depends on an Office renderer. PDF-to-Word uses page images to preserve appearance rather than reconstructing editable paragraphs. JPG is lossy. PDF-to-images creates pictures rather than editable documents. Image-to-PDF keeps image appearance on pages. Merge preserves page content and normalizes the page canvas without stretching smaller pages.

Each queued file has its own progress display, plus a batch progress bar. PDF rendering and merging report completed pages; image-to-PDF reports completed frames. A native Office export does not provide a reliable percentage through the selected automation interface, so that file shows an activity indicator until the export completes. The batch bar reflects the reported progress and completed files; it does not invent a percentage for engines that do not expose one.

## Translation screen

The user can add PDF, DOCX, TXT, and RTF files. RTF is accepted as an input format only. The list shows the file name, extension, size, and detected language. Detection is automatic when the extracted text is long enough and recognizable. If detection fails, the user can still choose the target language; the application assumes the source is the opposite language. The available translation direction is English to French or French to English.

The output choices are PDF, DOCX, and TXT:

- **DOCX:** The worker edits text in the DOCX package and retains unrelated package entries such as images, style definitions, numbering, and relationships. Existing run and paragraph styling is retained where text is replaced. Translated wording may wrap differently.
- **PDF:** For PDF input, the worker translates selectable text within its page area and retains the page dimensions and artwork. If text does not fit, it first uses verified empty space below the text, then adds continuation pages. Scanned PDFs need OCR before translation. For DOCX or RTF input, the Office engine exports the translated document to PDF.
- **TXT:** Text is extracted, translated, and saved as plain text. TXT cannot represent the input's images, fonts, tables, or page layout.

When the user starts translation, the app inspects the document, ensures the local translation engine is ready, sends bounded text batches to the loopback service, reconstructs or exports the requested document format, and publishes the output after success. Identical text fragments can be reused from a cache. The UI shows each file's percentage and current stage, such as preparing, translating, reconstructing, or saving. Translation worker progress is based on completed text batches or PDF pages. Waiting for the local model to start is shown as a preparation stage.

The translation display name is based on the original name plus the target language and selected output extension, for example `report.fr.pdf`. Output files do not retain the previous extension as an extra suffix. A cancellation stops work at supported checkpoints and prevents incomplete temporary files from being published as successful results.

## Compress screen

Compress accepts one or more files and creates ZIP archives. A single file named `notes.txt` becomes `notes.zip`; selecting multiple files creates one `archive.zip` for the group. Decompress extracts a file named `backup.zip` into a `backup.extracted` folder. PowerPoint PPTX is already a presentation package and is not treated as a ZIP archive by this screen. The application displays input size and path, transfers data in chunks, reports byte-based progress, and checks cancellation between chunks and entries. It rejects unsafe archive paths that could escape the destination folder.

## Encrypt screen

The user selects Encrypt or Decrypt, adds files, enters a password where needed, chooses whether selected files are handled individually or packaged together, and selects a destination. An encrypted file keeps the original extension before `.encrypted` (for example, `notes.txt.encrypted`) so decryption can restore the original filename. Encryption uses AES with a random salt and IV; PBKDF2 derives the cryptographic keys and HMAC authenticates the encrypted content. Decryption checks authentication before it publishes plaintext. A wrong password or altered encrypted file fails safely. Processing is streamed in chunks, so per-file progress reflects data read and written. Passwords cannot be recovered by the application.

## History screen

History is the operation log. It records the input name and paths, output format and path when successful, date/time, status, and originating interface. Search can filter by file, format/path, interface, or status. Status filters include completed, failed, cancelled, and skipped where applicable. History stores metadata only; it is not a backup of the user's documents.

## Manual screen

Manual is the built-in help area. Users search topics and open step-by-step guidance for conversion, translation, compression, encryption, history, and settings. It works without an internet connection.

## Settings screen

Settings controls the English/French interface language and light/dark appearance, and includes About information. On first launch, the application uses the operating system's language and theme as defaults. A choice saved by the user takes precedence later. These settings affect the interface and do not alter the source file's own colors or theme.

## Useful points to mention during a demonstration

1. The application accepts files locally and has separate screens for distinct tasks.
2. Conversion and translation offer explicit output formats and destinations.
3. File names use the new extension cleanly, and existing outputs are kept by creating a numbered copy.
4. Each file shows its own status and progress; the batch indicator summarizes the set.
5. Cancel requests stop supported work and incomplete temporary outputs are not treated as successful files.
6. History identifies the operation result and which interface performed it.
7. Document fidelity depends on the source format and renderer. Fixed-layout PDF translation can use nearby blank space or add continuation pages when text grows; DOCX allows text to reflow; TXT contains text only.
8. The installer bundles the translation models and document engines for offline use, but a Windows installation should still be checked with representative files and the Office/LibreOffice features the user relies on.
