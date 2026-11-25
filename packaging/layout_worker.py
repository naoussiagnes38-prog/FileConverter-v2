"""Offline layout-aware translation and PDF-to-DOCX conversion.

DOCX packages retain their non-text parts. PDF translation retains page geometry,
images and vector artwork, fitting translated selectable text into its text areas.
"""
import argparse
import copy
import html
import json
import logging
import math
import re
import subprocess
import sys
import tempfile
import urllib.request
from collections import OrderedDict, Counter
from pathlib import Path
from urllib.parse import urlparse
from zipfile import ZipFile

import pymupdf
from lxml import etree

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NS = {"w": W}
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"


def stage(text):
    print("stage " + text, flush=True)


def report(fraction):
    print("%d 10000" % min(10000, max(0, int(fraction * 10000))), flush=True)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError("The local translation engine redirected its request")


class Translator:
    def __init__(self, endpoint, languages):
        uri = urlparse(endpoint)
        if uri.scheme not in ("http", "https") or uri.hostname not in ("127.0.0.1", "localhost", "::1"):
            raise ValueError("Use the local translation engine")
        self.url = endpoint.rstrip("/") + "/translate"
        self.source, self.target = languages.split(",")
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        self.cache = {}

    def translate_all(self, texts, progress):
        if self.source == self.target:
            progress(1)
            return list(texts)
        # Split at word boundaries, not at DOCX run boundaries or PDF line wraps.
        units = []
        groups = []
        for text in texts:
            # Page counters are document structure, not prose. Keep their digits
            # and avoid model guesses such as translating "Page 1" as a word.
            if re.fullmatch(r"(?i)page\s+\d+(?:\s*(?:of|sur|/)\s*\d+)?", text.strip()):
                self.cache[text.strip()] = text.strip().replace(" of ", " sur ") if self.target == "fr" else text.strip().replace(" sur ", " of ")
            pieces = []
            for match in re.finditer(r"\S+(?:\s+|$)", text):
                part = match.group()
                if pieces and len(pieces[-1]) + len(part) <= 1800:
                    pieces[-1] += part
                else:
                    pieces.append(part)
            if pieces:
                pieces[0] = text[:len(text) - len(text.lstrip())] + pieces[0]
            else:
                pieces = [text]
            groups.append(pieces)
            units.extend(piece.strip() for piece in pieces if any(c.isalpha() for c in piece))
        unique = list(dict.fromkeys(unit for unit in units if unit not in self.cache))
        total = max(1, sum(map(len, unique)))
        done = 0
        offset = 0
        while offset < len(unique):
            batch = []
            length = 0
            while offset < len(unique) and len(batch) < 8:
                text = unique[offset]
                if batch and length + len(text) > 2400:
                    break
                batch.append(text)
                length += len(text)
                offset += 1
            data = json.dumps({"q": batch, "source": self.source, "target": self.target, "format": "text"}).encode("utf-8")
            request = urllib.request.Request(self.url, data=data, headers={"Content-Type": "application/json"})
            with self.http.open(request, timeout=180) as response:
                result = json.load(response)["translatedText"]
            if not isinstance(result, list) or len(result) != len(batch) or any(not text.strip() for text in result):
                raise ValueError("The translation engine returned an incomplete batch")
            self.cache.update(zip(batch, result))
            done += length
            progress(done / total)
        output = []
        for pieces in groups:
            translated = []
            for piece in pieces:
                stripped = piece.strip()
                if stripped in self.cache:
                    leading = piece[:len(piece) - len(piece.lstrip())]
                    trailing = piece[len(piece.rstrip()):]
                    translated.append(leading + self.cache[stripped] + trailing)
                else:
                    translated.append(piece)
            output.append("".join(translated))
        progress(1)
        return output


def docx_groups(root):
    """Keep drawings, fields, tabs, hyperlinks and run/paragraph formatting intact."""
    barriers = {"tab", "br", "cr", "drawing", "pict", "fldChar", "instrText"}
    groups = []
    for paragraph in root.iter("{%s}p" % W):
        current = []
        previous_style = None
        for node in paragraph.iter():
            if node is paragraph:
                continue
            if next(node.iterancestors("{%s}p" % W), None) is not paragraph:
                continue
            local = etree.QName(node).localname
            if local in barriers:
                if current:
                    groups.append(current)
                current, previous_style = [], None
            if node.tag != "{%s}t" % W:
                continue
            run = next(node.iterancestors("{%s}r" % W), None)
            properties = run.find("{%s}rPr" % W) if run is not None else None
            link = next(node.iterancestors("{%s}hyperlink" % W), None)
            style = (etree.tostring(properties) if properties is not None else b"", id(link) if link is not None else None)
            if current and style != previous_style:
                groups.append(current)
                current = []
            current.append(node)
            previous_style = style
        if current:
            groups.append(current)
    return groups


def docx_cover_text_nodes(root):
    """Protect first-page text when a short, explicitly separated title page is present."""
    body = root.find("{%s}body" % W)
    if body is None:
        return set()
    paragraphs = list(body.iter("{%s}p" % W))
    first_page = []
    boundary_found = False
    for paragraph in paragraphs:
        first_page.append(paragraph)
        boundary = any(node.tag == "{%s}br" % W and node.get("{%s}type" % W) == "page"
                       for node in paragraph.iter())
        boundary = boundary or any(node.tag == "{%s}lastRenderedPageBreak" % W for node in paragraph.iter())
        boundary = boundary or paragraph.find("{%s}pPr/{%s}sectPr" % (W, W)) is not None
        if boundary:
            boundary_found = True
            break
    if not boundary_found:
        return set()
    text = " ".join("".join(node.itertext()) for node in first_page)
    title_style = any(
        (paragraph.find("{%s}pPr/{%s}pStyle" % (W, W)) is not None and
         any(token in paragraph.find("{%s}pPr/{%s}pStyle" % (W, W)).get("{%s}val" % W, "").lower()
             for token in ("title", "subtitle", "cover")))
        for paragraph in first_page
    )
    has_art = any(paragraph.find(".//{%s}drawing" % W) is not None or
                  paragraph.find(".//{%s}pict" % W) is not None for paragraph in first_page)
    if len(text) > 2400 or not (title_style or has_art):
        return set()
    return {node for paragraph in first_page for node in paragraph.iter("{%s}t" % W)}


def translate_docx(source, destination, translator, progress):
    stage("Reading document...")
    parts = {}
    groups = []
    parser = etree.XMLParser(resolve_entities=False, no_network=True, remove_blank_text=False)
    with ZipFile(source) as archive:
        for entry in archive.infolist():
            name = entry.filename
            if re.fullmatch(r"word/(document|header\d+|footer\d+|footnotes|endnotes)\.xml", name):
                root = etree.fromstring(archive.read(entry), parser)
                parts[name] = root
                part_groups = docx_groups(root)
                if name == "word/document.xml":
                    cover_nodes = docx_cover_text_nodes(root)
                    part_groups = [group for group in part_groups if not any(node in cover_nodes for node in group)]
                groups.extend(part_groups)
        texts = ["".join(node.text or "" for node in group) for group in groups]
        stage("Translating...")
        translations = translator.translate_all(texts, progress)
        for group, translated in zip(groups, translations):
            group[0].text = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", "", translated)
            group[0].set(XML_SPACE, "preserve")
            for node in group[1:]:
                node.text = ""
        # Only edited XML parts change: media, styles, numbering, relationships,
        # section properties, shapes and other package data remain byte-identical.
        with ZipFile(destination, "w") as output:
            for entry in archive.infolist():
                data = etree.tostring(parts[entry.filename], xml_declaration=True, encoding="UTF-8", standalone=True) if entry.filename in parts else archive.read(entry)
                output.writestr(entry, data)


def office_convert(source, directory, extension, office):
    stage("Exporting document...")
    executable = Path(office) / "program" / "soffice.com"
    if not executable.is_file():
        # Only for the Linux regression environment.
        executable = Path(office) if Path(office).is_file() else executable
    if not executable.is_file():
        raise ValueError("The bundled office engine is missing; install the complete offline package")
    with tempfile.TemporaryDirectory(prefix="FileConverter-office-") as profile:
        filters = {"pdf": "writer_pdf_Export", "docx": "Office Open XML Text"}
        selected_filter = extension + ":" + filters[extension]
        if extension == "pdf":
            selected_filter += ":" + json.dumps({
                "UseLosslessCompression": {"type": "boolean", "value": "true"},
                "IsSkipEmptyPages": {"type": "boolean", "value": "false"},
                "ExportBookmarks": {"type": "boolean", "value": "true"}})
        result = subprocess.run([str(executable), "-env:UserInstallation=" + Path(profile).as_uri(),
                                 "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to",
                                 selected_filter, "--outdir", str(directory), str(source)],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=900,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        output = Path(directory) / (Path(source).stem + "." + extension)
        if result.returncode or not output.is_file():
            raise ValueError("The office engine could not export this document. " + result.stderr.decode(errors="replace")[-1500:])
        return output


def pdf_to_visual_docx(source, destination, progress):
    """Build one exact-size Word page per PDF page using lossless page art.

    This mode prioritizes the PDF's visible layout over editable text. PDF is
    fixed-layout; reconstructing it as flowing Word paragraphs cannot preserve
    every font, image, form, or positioned object exactly.
    """
    import docx
    from docx.enum.section import WD_SECTION
    from docx.oxml import OxmlElement
    from docx.oxml.ns import qn
    from docx.shared import Inches, Pt
    from copy import deepcopy

    stage("Preserving page appearance...")
    dpi = 240
    with pymupdf.open(source) as pdf:
        if pdf.needs_pass:
            raise ValueError("Unlock this password-protected PDF before converting it")
        if len(pdf) == 0:
            raise ValueError("This PDF contains no pages")
        output = docx.Document()
        for index, page in enumerate(pdf):
            width = float(page.rect.width) / 72.0
            height = float(page.rect.height) / 72.0
            # Word does not support page dimensions above 22 inches.
            if width > 22 or height > 22:
                raise ValueError("Page %d exceeds Word's 22-inch page limit. Keep PDF output to preserve its exact size." % (index + 1))
            section = output.sections[0] if index == 0 else output.add_section(WD_SECTION.NEW_PAGE)
            section.page_width = Inches(width)
            section.page_height = Inches(height)
            section.top_margin = Inches(0)
            section.bottom_margin = Inches(0)
            section.left_margin = Inches(0)
            section.right_margin = Inches(0)
            section.header_distance = Inches(0)
            section.footer_distance = Inches(0)

            image = page.get_pixmap(matrix=pymupdf.Matrix(dpi / 72.0, dpi / 72.0),
                                    alpha=False, annots=True).tobytes("png")
            paragraph = output.add_paragraph()
            paragraph.paragraph_format.space_before = Pt(0)
            paragraph.paragraph_format.space_after = Pt(0)
            paragraph.paragraph_format.line_spacing = Pt(1)
            run = paragraph.add_run()
            run.add_picture(__import__("io").BytesIO(image), width=Inches(width), height=Inches(height))

            # Make the image a page-anchored background, so it does not push
            # the next section onto an extra page or get reflowed as text.
            drawing = run._r.drawing_lst[0]
            inline = drawing.find(qn("wp:inline"))
            anchor = OxmlElement("wp:anchor")
            for name, value in (("distT", "0"), ("distB", "0"), ("distL", "0"), ("distR", "0"),
                                ("simplePos", "0"), ("relativeHeight", "0"), ("behindDoc", "1"),
                                ("locked", "0"), ("layoutInCell", "1"), ("allowOverlap", "1")):
                anchor.set(name, value)
            simple = OxmlElement("wp:simplePos")
            simple.set("x", "0")
            simple.set("y", "0")
            anchor.append(simple)
            for tag in ("wp:positionH", "wp:positionV"):
                position = OxmlElement(tag)
                position.set("relativeFrom", "page")
                offset = OxmlElement("wp:posOffset")
                offset.text = "0"
                position.append(offset)
                anchor.append(position)
            anchor.append(deepcopy(inline.extent))
            effect = OxmlElement("wp:effectExtent")
            for edge in ("l", "t", "r", "b"):
                effect.set(edge, "0")
            anchor.append(effect)
            anchor.append(OxmlElement("wp:wrapNone"))
            anchor.append(deepcopy(inline.docPr))
            anchor.append(deepcopy(inline.find(qn("wp:cNvGraphicFramePr"))))
            anchor.append(deepcopy(inline.graphic))
            inline.addprevious(anchor)
            inline.getparent().remove(inline)
            progress((index + 1) / len(pdf))
        output.save(str(destination))


def pdf_units(page):
    cells = []
    try:
        for table in page.find_tables().tables:
            cells.extend(pymupdf.Rect(cell) for cell in table.cells if cell)
    except (ValueError, RuntimeError):
        pass
    groups = OrderedDict()
    previous = {}
    for block_index, block in enumerate(page.get_text("dict", sort=True, flags=pymupdf.TEXTFLAGS_DICT & ~pymupdf.TEXT_PRESERVE_IMAGES)["blocks"]):
        if block["type"] != 0:
            continue
        for line_index, line in enumerate(block["lines"]):
            if abs(line["dir"][1]) > 0.01:
                raise ValueError("This page contains rotated text. Choose Word (DOCX) output for layout reconstruction.")
            bounds = pymupdf.Rect(line["bbox"])
            center = (bounds.tl + bounds.br) / 2
            cell = next((index for index, rect in enumerate(cells) if center in rect), None)
            base = ("cell", cell) if cell is not None else ("block", block_index)
            for span in line["spans"]:
                if not span["text"]:
                    continue
                style = (span["font"], round(span["size"], 2), span["flags"], span["color"])
                last_style, segment = previous.get(base, (None, -1))
                if style != last_style:
                    segment += 1
                previous[base] = (style, segment)
                key = base + (segment,)
                group = groups.setdefault(key, {"text": [], "spans": [], "bbox": None,
                    "cell": cells[cell] if cell is not None else None, "base": base, "line": None})
                line_key = (block_index, line_index)
                if group["line"] == line_key:
                    group["text"][-1] += span["text"]
                else:
                    group["text"].append(span["text"])
                group["line"] = line_key
                group["spans"].append(span)
                box = pymupdf.Rect(span["bbox"])
                group["bbox"] = box if group["bbox"] is None else group["bbox"] | box
    # Mixed styles in a table cell must each use their own text area, otherwise
    # replacing each segment in the entire cell would overlap the others.
    counts = Counter(group["base"] for group in groups.values())
    for group in groups.values():
        if counts[group["base"]] > 1:
            group["cell"] = None
    return list(groups.values())


def html_text(text):
    return "<div>" + html.escape(text).replace("\n", "<br>") + "</div>"


def html_fits(width, height, rectangle, body, css, scale_low):
    """Check a text box without changing the document being translated."""
    with pymupdf.open() as scratch:
        page = scratch.new_page(width=width, height=height)
        try:
            spare, _ = page.insert_htmlbox(rectangle, body, css=css, scale_low=scale_low)
            return spare >= 0
        except Exception:
            # Some PDFs contain embedded fonts that MuPDF cannot reuse in HTML.
            # The caller retries with a standard font before declaring overflow.
            return False


def expanded_text_rect(page, unit, rectangle):
    """Use clear space below a text unit without covering later page content."""
    if unit.get("cell") is not None:
        bottom = min(page.rect.y1 - 18, unit["cell"].y1 - 2)
    else:
        bottom = page.rect.y1 - 24
        bounds = unit["bbox"]
        for block in page.get_text("dict", sort=True, flags=pymupdf.TEXTFLAGS_DICT & ~pymupdf.TEXT_PRESERVE_IMAGES)["blocks"]:
            if block["type"] != 0:
                continue
            for line in block["lines"]:
                for span in line["spans"]:
                    box = pymupdf.Rect(span["bbox"])
                    if box.y0 >= bounds.y1 - 0.5 and box.x1 > bounds.x0 and box.x0 < bounds.x1:
                        bottom = min(bottom, box.y0 - 2)
        for image in page.get_image_info(xrefs=True):
            box = pymupdf.Rect(image["bbox"])
            if box.y0 >= bounds.y1 - 0.5 and box.x1 > bounds.x0 and box.x0 < bounds.x1:
                bottom = min(bottom, box.y0 - 2)
        for drawing in page.get_drawings():
            box = pymupdf.Rect(drawing["rect"])
            if box.y0 >= bounds.y1 - 0.5 and box.x1 > bounds.x0 and box.x0 < bounds.x1:
                bottom = min(bottom, box.y0 - 2)
    if bottom <= rectangle.y1 + 3:
        return None
    return pymupdf.Rect(rectangle.x0, rectangle.y0, rectangle.x1, bottom)


def continuation_chunks(text, width, height, css):
    """Split unusually long overflow paragraphs across readable PDF pages."""
    remaining = re.sub(r"\s+", " ", text).strip()
    chunks = []
    minimum_scale = 0.45
    while remaining:
        low, high, best = 1, len(remaining), 0
        while low <= high:
            middle = (low + high) // 2
            end = middle
            if end < len(remaining):
                boundary = remaining.rfind(" ", 0, end + 1)
                if boundary > 0:
                    end = boundary
            candidate = remaining[:end].rstrip()
            if candidate and html_fits(width, height, pymupdf.Rect(0, 0, width, height),
                                       html_text(candidate), css, minimum_scale):
                best = len(candidate)
                low = middle + 1
            else:
                high = middle - 1
        if best == 0:
            # A very long URL or unbroken token has no word boundary. Split it
            # at a Unicode codepoint so it cannot make translation fail outright.
            end = min(120, len(remaining))
            while end > 1 and not html_fits(width, height, pymupdf.Rect(0, 0, width, height),
                                            html_text(remaining[:end]), css, minimum_scale):
                end //= 2
            if end <= 1:
                raise ValueError("A translated word is too large to place on a continuation page")
            best = end
        chunks.append(remaining[:best].strip())
        remaining = remaining[best:].lstrip()
    return chunks


def is_cover_page(page, page_index):
    """Recognize a likely illustrated cover so translation leaves it untouched."""
    if page_index != 0:
        return False
    text = page.get_text("text", sort=True).strip()
    if not text:
        return False
    page_area = max(1, page.rect.width * page.rect.height)
    image_area = 0.0
    for image in page.get_image_info(xrefs=True):
        box = pymupdf.Rect(image["bbox"]) & page.rect
        image_area += max(0, box.width) * max(0, box.height)
    # A large illustration, logo or background paired with a short title block
    # is a strong cover signal. Do not skip ordinary text-only first pages.
    if image_area / page_area >= 0.22 and len(text) <= 1800:
        return True
    blocks = [block for block in page.get_text("dict", sort=True)["blocks"] if block["type"] == 0]
    spans = [span for block in blocks for line in block["lines"] for span in line["spans"] if span["text"].strip()]
    if len(text) <= 450 and len(spans) <= 16 and spans:
        largest = max(span["size"] for span in spans)
        average = sum(span["size"] for span in spans) / len(spans)
        # Sparse, title-sized centered text is a common text-only cover.
        centered = all(abs(((span["bbox"][0] + span["bbox"][2]) / 2) - page.rect.width / 2) < page.rect.width * 0.28
                       for span in spans)
        if largest >= 20 and average >= 14 and centered:
            return True
    return False


def translate_pdf(source, destination, translator):
    with pymupdf.open(source) as document:
        if document.needs_pass:
            raise ValueError("Unlock this password-protected PDF before translating it")
        page_count = max(1, len(document))
        readable = 0
        overflow = []
        for page_index, page in enumerate(document):
            if is_cover_page(page, page_index):
                stage("Preserving cover page...")
                report(0.94 * (page_index + 1) / page_count)
                continue
            units = pdf_units(page)
            readable += len(units)
            stage("Translating...")
            texts = [" ".join(unit["text"]) for unit in units]
            translations = translator.translate_all(texts, lambda fraction: report(0.94 * (page_index + 0.85 * fraction) / page_count))
            replacements = []
            redactions = []
            page_fonts = []
            for font in page.get_fonts(full=True):
                try:
                    extracted = document.extract_font(font[0])
                    if extracted and extracted[3]:
                        # Font xrefs are integers; compare the PDF font's name
                        # with the text span's font name instead of testing an
                        # integer for membership in a string.
                        page_fonts.append((str(font[3]), extracted[3]))
                except (RuntimeError, ValueError):
                    pass
            for unit, translated in zip(units, translations):
                if translated.strip() == " ".join(unit["text"]).strip():
                    continue
                span = max(unit["spans"], key=lambda item: len(item["text"]))
                size = max(6, span["size"])
                family = "monospace" if span["flags"] & 8 else "serif" if span["flags"] & 4 else "sans-serif"
                fallback_family = family
                span_font = str(span.get("font", ""))
                font_data = next((data for name, data in page_fonts
                                  if name and (name in span_font or span_font in name)), None)
                font_css = ""
                if font_data is not None:
                    font_css = "@font-face { font-family: 'FCEmbedded'; src: url(data:font/ttf;base64,%s); }" % __import__("base64").b64encode(font_data).decode("ascii")
                    family = "FCEmbedded"
                css = font_css + " * { margin:0; padding:0; } div { font-family:'%s'; font-size:%spt; color:#%06x; font-weight:%s; font-style:%s; line-height:1.05; }" % (
                    family, size, span["color"], "bold" if span["flags"] & 16 else "normal", "italic" if span["flags"] & 2 else "normal")
                rectangle = pymupdf.Rect(unit["bbox"])
                # Restrict the background restoration and new glyphs to this
                # text region. Broad block boxes can cover a cover image or color.
                rectangle = rectangle + (-0.7, -0.8, 0.9, 1.1)
                rectangle &= page.rect
                body = html_text(translated)
                # First preserve the source box and font size as closely as
                # possible. Then allow readable shrinking down to 4 pt.
                minimum_scale = min(1.0, max(0.22, 4 / size))
                moved_to_overflow = False
                if not html_fits(page.rect.width, page.rect.height, rectangle, body, css, minimum_scale):
                    # Retry with a standard font if the source PDF embeds a font
                    # format that MuPDF cannot use for HTML text.
                    fallback_css = "* { margin:0; padding:0; } div { font-family:'%s'; font-size:%spt; color:#%06x; font-weight:%s; font-style:%s; line-height:1.05; }" % (
                        fallback_family, size, span["color"], "bold" if span["flags"] & 16 else "normal", "italic" if span["flags"] & 2 else "normal")
                    if css != fallback_css and html_fits(page.rect.width, page.rect.height,
                                                        rectangle, body, fallback_css, minimum_scale):
                        css = fallback_css
                    else:
                        larger = expanded_text_rect(page, unit, rectangle)
                        if larger is not None and html_fits(page.rect.width, page.rect.height,
                                                            larger, body, css, minimum_scale):
                            # Let text use blank space up to the next item in
                            # the same column, without overlapping it.
                            rectangle = larger
                        elif larger is not None and html_fits(page.rect.width, page.rect.height,
                                                              larger, body, fallback_css, minimum_scale):
                            rectangle, css = larger, fallback_css
                        else:
                            overflow.append((page_index + 1, page.rect.width, page.rect.height,
                                            translated, fallback_css, fallback_css))
                            moved_to_overflow = True
                if not moved_to_overflow:
                    replacements.append((unit, rectangle, body, css, minimum_scale))
                redactions.append(unit)
            links = page.get_links()
            for unit in redactions:
                for span in unit["spans"]:
                    page.add_redact_annot(pymupdf.Rect(span["bbox"]), fill=None, cross_out=False)
            if redactions:
                covered_images = [info for info in page.get_image_info(xrefs=True)
                                  if any(pymupdf.Rect(info["bbox"]).intersects(rectangle)
                                         for _, rectangle, _, _, _ in replacements)]
                preserved_images = []
                for info in covered_images:
                    xref = info.get("xref", 0)
                    if xref:
                        preserved_images.append((xref, pymupdf.Rect(info["bbox"]), info.get("transform")))
                page.apply_redactions(images=0, graphics=0)
                # PyMuPDF retains image objects during redaction; restore image
                # placements in case the source text overlaps a photo or logo.
                for xref, box, transform in preserved_images:
                    page.insert_image(box, xref=xref, overlay=False, keep_proportion=False)
                for _, rectangle, body, css, minimum in replacements:
                    spare, _ = page.insert_htmlbox(rectangle, body, css=css, scale_low=minimum)
                    if spare < 0:
                        overflow.append((page_index + 1, page.rect.width, page.rect.height,
                                         html.unescape(body[5:-6]), css, css))
                remaining = page.get_links()
                for link in links:
                    if not any(item.get("uri") == link.get("uri") and item.get("from") == link.get("from") for item in remaining):
                        link.pop("xref", None)
                        link.pop("id", None)
                        page.insert_link(link)
            report(0.94 * (page_index + 1) / page_count)
        if not readable:
            raise ValueError("This PDF contains no selectable text. OCR is required for scanned pages")
        # Overflow is appended after the original pages so page references in
        # the source remain stable. Original page artwork and dimensions remain
        # unchanged; only the affected translated paragraphs move to these pages.
        # Count the pages before publishing so progress stays monotonic when
        # several source pages produce overflow.
        all_continuations = []
        for source_page, width, height, text, css, fallback_css in overflow:
            stage("Adding continuation pages...")
            margin = 36
            content_width = max(40, width - 2 * margin)
            content_height = max(40, height - margin - 52)
            chunks = continuation_chunks(text, content_width, content_height, css)
            title = ("Suite de la page %d" if translator.target == "fr" else "Continued from page %d") % source_page
            all_continuations.extend((width, height, title, part, len(chunks), chunk, css, fallback_css)
                                     for part, chunk in enumerate(chunks, 1))
        for index, (width, height, title, part, total, chunk, selected_css, fallback_css) in enumerate(all_continuations):
            continuation = document.new_page(width=width, height=height)
            heading = title + (" (%d/%d)" % (part, total) if total > 1 else "")
            continuation.insert_text((36, 30), heading, fontsize=11, fontname="helv", color=(0.25, 0.34, 0.42))
            rectangle = pymupdf.Rect(36, 52, width - 36, height - 36)
            spare, _ = continuation.insert_htmlbox(rectangle, html_text(chunk), css=selected_css, scale_low=0.45)
            if spare < 0 and selected_css != fallback_css:
                spare, _ = continuation.insert_htmlbox(rectangle, html_text(chunk), css=fallback_css, scale_low=0.45)
            if spare < 0:
                raise ValueError("Translated overflow could not fit on a continuation page")
            report(0.94 + 0.06 * (index + 1) / max(1, len(all_continuations)))
        document.save(str(destination), garbage=3, deflate=True)
        report(1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("translate", "to-word"))
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--format", default="docx", choices=("pdf", "docx"))
    parser.add_argument("--endpoint", default="")
    parser.add_argument("--languages", default="en,fr")
    parser.add_argument("--office", default="")
    args = parser.parse_args()
    logging.getLogger().setLevel(logging.ERROR)
    if args.operation == "to-word":
        pdf_to_visual_docx(args.source, args.destination, report)
        return
    translator = Translator(args.endpoint, args.languages)
    extension = args.source.suffix.lower()
    if translator.source == translator.target and extension == "." + args.format:
        # Exporting to the same language and format must not rewrite the document.
        import shutil
        shutil.copyfile(args.source, args.destination)
        report(1)
        return
    if extension == ".pdf" and args.format == "pdf":
        translate_pdf(args.source, args.destination, translator)
        return
    with tempfile.TemporaryDirectory(prefix="FileConverter-layout-") as temporary:
        work = Path(temporary)
        source = args.source
        if extension == ".pdf" and args.format == "docx":
            translated_pdf = work / "translated.pdf"
            if translator.source == translator.target:
                import shutil
                shutil.copyfile(args.source, translated_pdf)
            else:
                translate_pdf(args.source, translated_pdf, translator)
            visual_docx = work / "visual-pages.docx"
            pdf_to_visual_docx(translated_pdf, visual_docx, lambda fraction: report(0.94 + 0.06 * fraction))
            import shutil
            shutil.copyfile(visual_docx, args.destination)
            report(1)
            return
        if extension == ".rtf":
            source = office_convert(args.source, work, "docx", args.office)
        elif extension != ".docx":
            raise ValueError("Layout-preserving translation supports DOCX, PDF and RTF")
        translated = work / "translated.docx"
        translate_docx(source, translated, translator, lambda fraction: report(0.2 + 0.7 * fraction))
        if args.format == "docx":
            import shutil
            shutil.copyfile(translated, args.destination)
        else:
            exported = office_convert(translated, work, args.format, args.office)
            import shutil
            shutil.copyfile(exported, args.destination)
        report(1)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr, flush=True)
        sys.exit(1)
