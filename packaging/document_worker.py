"""Offline PDF operations using the PDF library shipped with FileConverter."""
import sys
from pathlib import Path
import pymupdf


def main():
    operation, source, destination = sys.argv[1:4]
    if operation == "merge":
        paths = Path(source).read_text(encoding="utf-8-sig").splitlines()
        total = 0
        width = height = 0
        for path in paths:
            with pymupdf.open(path) as part:
                if part.needs_pass: raise ValueError("Unlock password-protected PDFs before merging")
                total += len(part)
                for page in part:
                    width = max(width, page.rect.width)
                    height = max(height, page.rect.height)
        done = 0
        with pymupdf.open() as merged:
            for path in paths:
                with pymupdf.open(path) as part:
                    offset = len(merged)
                    # Copy the whole source together so internal links to other
                    # pages, outlines in content and form relationships survive.
                    merged.insert_pdf(part)
                    for index in range(len(part)):
                        page = merged[offset + index]
                        page.remove_rotation()
                        # Keep vector artwork, text, links and form fields. Extend
                        # the page canvas equally on each side, without scaling.
                        visible = page.rect
                        crop, media = page.cropbox, page.mediabox
                        raw = pymupdf.Rect(crop.x0, media.y1 - crop.y1,
                                           crop.x1, media.y1 - crop.y0)
                        if abs(visible.width - width) > .01 or abs(visible.height - height) > .01:
                            # Clip to the original visible page before extending it,
                            # so hidden objects outside a crop box stay hidden.
                            page.wrap_contents()
                            before = merged.get_new_xref()
                            merged.update_object(before, "<<>>")
                            merged.update_stream(before, ("q %g %g %g %g re W n\n" %
                                (raw.x0, raw.y0, raw.width, raw.height)).encode())
                            after = merged.get_new_xref()
                            merged.update_object(after, "<<>>")
                            merged.update_stream(after, b"\nQ")
                            refs = [before] + page.get_contents() + [after]
                            merged.xref_set_key(page.xref, "Contents", "[" + " ".join("%d 0 R" % ref for ref in refs) + "]")
                            dx, dy = (width - visible.width) / 2, (height - visible.height) / 2
                            page.set_mediabox(raw + (-dx, -dy, dx, dy))
                        done += 1
                        print("%d %d" % (done, max(1, total)), flush=True)
            merged.save(destination, garbage=3, deflate=True)
        return
    with pymupdf.open(source) as document:
        if document.needs_pass:
            raise ValueError("This PDF is password protected. Unlock it before processing.")
        if operation in ("text", "sample"):
            with open(destination, "w", encoding="utf-8") as output:
                written = 0
                page_count = max(1, len(document))
                for page_index, page in enumerate(document):
                    text = page.get_text(sort=True) + "\n"
                    if operation == "sample": text = text[:max(0, 16000 - written)]
                    output.write(text)
                    written += len(text)
                    if operation == "sample":
                        print("%d %d" % (page_index + 1, page_count), flush=True)
                    if operation == "sample" and written >= 16000: break
        elif operation == "images":
            quality = int(sys.argv[4])
            folder = Path(destination)
            folder.mkdir(parents=True, exist_ok=True)
            for index, page in enumerate(document):
                image = page.get_pixmap(matrix=pymupdf.Matrix(150 / 72, 150 / 72), alpha=False)
                image.save(str(folder / ("page-%04d.jpg" % (index + 1))), jpg_quality=quality)
                print("%d %d" % (index + 1, len(document)), flush=True)
        else:
            raise ValueError("Unsupported PDF operation")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
