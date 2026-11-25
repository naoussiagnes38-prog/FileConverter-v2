"""Run with the document runtime: python -m unittest discover -s packaging/tests."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

spec = importlib.util.spec_from_file_location('layout_worker', Path(__file__).resolve().parents[1] / 'layout_worker.py')
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class Response:
    def __init__(self, data): self.data = data
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self): return json.dumps(self.data).encode()


class Engine:
    def __init__(self): self.requests = []
    def open(self, request, timeout):
        payload = json.loads(request.data)
        texts = payload['q']
        self.requests.extend(texts)
        if payload['source'] == 'en' and payload['target'] == 'fr':
            translated = ['Bonjour' if t == 'Hello' else 'Bonjour le monde' if t == 'Hello world' else
                          ' '.join(['Bonjour'] + ['mot'] * 70) if t == 'Long source' else 'FR ' + t for t in texts]
        elif payload['source'] == 'fr' and payload['target'] == 'en':
            translated = ['Hello' if t == 'Bonjour' else 'Hello world' if t == 'Bonjour le monde' else 'EN ' + t for t in texts]
        else:
            translated = texts
        return Response({'translatedText': translated})


class LayoutTests(unittest.TestCase):
    def translator(self, languages='en,fr'):
        result = worker.Translator('http://127.0.0.1:5000', languages)
        result.http = Engine()
        return result

    def test_whitespace_deduplication_and_counters(self):
        translator = self.translator()
        progress = []
        result = translator.translate_all(['  Hello  ', '\tHello\n', '123', '   ', 'Page 2 of 7'], progress.append)
        self.assertEqual(result, ['  Bonjour  ', '\tBonjour\n', '123', '   ', 'Page 2 sur 7'])
        self.assertEqual(translator.http.requests, ['Hello'])
        self.assertEqual(progress[-1], 1)
        self.assertEqual(progress, sorted(progress))

    def test_same_language_needs_no_engine(self):
        translator = self.translator('fr,fr')
        source = ['  Bonjour\n', 'Page 2 sur 7']
        self.assertEqual(translator.translate_all(source, lambda _: None), source)
        self.assertFalse(translator.http.requests)

    def test_docx_preserves_media_styles_structure_and_spaces(self):
        import docx
        from lxml import etree
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source.docx', Path(directory) / 'translated.docx'
            document = docx.Document()
            document.add_heading('Hello', 1)
            paragraph = document.add_paragraph()
            paragraph.add_run('  Hello  ').bold = True
            paragraph.add_run().add_break()
            paragraph.add_run('Hello')
            document.add_table(rows=2, cols=2).cell(0, 0).text = 'Hello'
            document.save(source)
            with ZipFile(source, 'a') as package:
                package.writestr('word/media/retained-image.bin', b'image-content')
            worker.translate_docx(source, target, self.translator(), lambda _: None)
            with ZipFile(source) as before, ZipFile(target) as after:
                for name in before.namelist():
                    if name != 'word/document.xml':
                        self.assertEqual(before.read(name), after.read(name), name)
                root = etree.fromstring(after.read('word/document.xml'))
                text = root.xpath('//w:t/text()', namespaces=worker.NS)
                self.assertIn('  Bonjour  ', text)
                self.assertEqual(len(root.xpath('//w:tbl', namespaces=worker.NS)), 1)
                self.assertEqual(len(root.xpath('//w:br', namespaces=worker.NS)), 1)
                self.assertTrue(root.xpath('//w:pStyle[@w:val="Heading1"]', namespaces=worker.NS))
                self.assertTrue(root.xpath('//w:rPr/w:b', namespaces=worker.NS))

    def test_docx_title_cover_is_left_unchanged_when_page_break_is_marked(self):
        import docx
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source.docx', Path(directory) / 'translated.docx'
            document = docx.Document()
            document.add_heading('Hello', 0)
            document.add_page_break()
            document.add_paragraph('Hello world')
            document.save(source)
            worker.translate_docx(source, target, self.translator(), lambda _: None)
            translated = docx.Document(target)
            self.assertEqual(translated.paragraphs[0].text, 'Hello')
            self.assertIn('Bonjour le monde', '\n'.join(p.text for p in translated.paragraphs))

    def test_pdf_mixed_styles_are_separate_translation_areas(self):
        import pymupdf
        with pymupdf.open() as document:
            page = document.new_page()
            page.insert_text((50, 70), "Red heading", fontsize=20, color=(1, 0, 0))
            page.insert_text((50, 110), "Blue body", fontsize=12, color=(0, 0, 1))
            units = worker.pdf_units(page)
            self.assertEqual(len(units), 2)
            self.assertEqual({span['color'] for unit in units for span in unit['spans']}, {0xff0000, 0x0000ff})

    def test_pdf_to_pdf_translates_both_directions_and_keeps_page_artwork(self):
        import pymupdf
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'source.pdf'
            french = Path(directory) / 'french.pdf'
            english = Path(directory) / 'english.pdf'
            with pymupdf.open() as document:
                page = document.new_page(width=420, height=600)
                page.draw_rect(pymupdf.Rect(25, 25, 395, 150), color=(0, 0, 1), fill=(0.9, 0.9, 1))
                page.insert_text((45, 75), 'Hello world', fontsize=16, color=(0, 0, 1), fontname='helv')
                document.save(source)

            worker.translate_pdf(source, french, self.translator('en,fr'))
            with pymupdf.open(source) as before, pymupdf.open(french) as after:
                self.assertEqual(after[0].rect, before[0].rect)
                self.assertIn('Bonjour le monde', ' '.join(after[0].get_text().split()))
                self.assertTrue(after[0].get_drawings())

            worker.translate_pdf(french, english, self.translator('fr,en'))
            with pymupdf.open(english) as after:
                self.assertEqual(after[0].rect, (0, 0, 420, 600))
                self.assertIn('Hello world', ' '.join(after[0].get_text().split()))
                self.assertTrue(after[0].get_drawings())

    def test_illustrated_cover_is_left_unchanged_while_body_is_translated(self):
        import pymupdf
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source.pdf', Path(directory) / 'translated.pdf'
            with pymupdf.open() as document:
                cover = document.new_page(width=300, height=400)
                pixmap = pymupdf.Pixmap(pymupdf.csRGB, pymupdf.IRect(0, 0, 240, 320), False)
                pixmap.clear_with(0x336699)
                cover.insert_image(cover.rect, pixmap=pixmap)
                cover.insert_text((75, 200), 'Hello', fontsize=24, color=(1, 1, 1))
                body = document.new_page(width=300, height=400)
                body.insert_text((40, 60), 'Hello world', fontsize=12)
                document.save(source)
            worker.translate_pdf(source, target, self.translator())
            with pymupdf.open(source) as original, pymupdf.open(target) as result:
                self.assertEqual(len(result), 2)
                self.assertEqual(original[0].get_text(), result[0].get_text())
                self.assertEqual(original[0].get_pixmap().samples, result[0].get_pixmap().samples)
                self.assertIn('Bonjour le monde', ' '.join(result[1].get_text().split()))

    def test_pdf_translation_expands_text_into_clear_page_space(self):
        import pymupdf
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source.pdf', Path(directory) / 'translated.pdf'
            with pymupdf.open() as document:
                page = document.new_page(width=420, height=600)
                page.insert_text((45, 65), 'Long source', fontsize=12)
                document.save(source)
            worker.translate_pdf(source, target, self.translator())
            with pymupdf.open(target) as result:
                self.assertEqual(len(result), 1)
                self.assertIn('Bonjour', ' '.join(result[0].get_text().split()))
                self.assertIn('mot', result[0].get_text())

    def test_pdf_translation_moves_overflow_to_a_continuation_page(self):
        import pymupdf
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source.pdf', Path(directory) / 'translated.pdf'
            with pymupdf.open() as document:
                page = document.new_page(width=420, height=300)
                page.insert_text((45, 65), 'Long source', fontsize=12)
                page.insert_text((45, 100), 'Blocker', fontsize=12)
                document.save(source)
            worker.translate_pdf(source, target, self.translator())
            with pymupdf.open(target) as result:
                self.assertGreaterEqual(len(result), 2)
                translated = ' '.join(page.get_text() for page in result)
                self.assertEqual(translated.split().count('mot'), 70)
                self.assertIn('Bonjour', translated)
                self.assertIn('Suite de la page 1', result[-1].get_text())
                self.assertNotIn('Long source', result[0].get_text())

    def test_scanned_pdf_reports_ocr_requirement_without_corrupting_output(self):
        import pymupdf
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'scan.pdf', Path(directory) / 'translated.pdf'
            with pymupdf.open() as document:
                page = document.new_page(width=420, height=600)
                page.draw_rect(pymupdf.Rect(20, 20, 100, 100), fill=(0.5, 0.5, 0.5))
                document.save(source)
            with self.assertRaisesRegex(ValueError, 'OCR is required'):
                worker.translate_pdf(source, target, self.translator())
            self.assertFalse(target.exists())

    def test_pdf_to_images_renders_each_page(self):
        import pymupdf
        import subprocess
        import sys
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source.pdf'
            with pymupdf.open() as document:
                document.new_page(width=300, height=400)
                document.new_page(width=600, height=700)
                document.save(source)
            output = root / 'pages'
            subprocess.run([sys.executable, str(Path(__file__).resolve().parents[1] / 'document_worker.py'),
                'images', str(source), str(output), '90'], check=True, capture_output=True)
            images = sorted(output.glob('page-*.jpg'))
            self.assertEqual([path.name for path in images], ['page-0001.jpg', 'page-0002.jpg'])
            self.assertEqual(pymupdf.Pixmap(str(images[0])).width, 625)

    def test_pdf_to_word_builds_one_visual_page_per_pdf_page(self):
        import pymupdf
        import docx
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, translated, target = root / 'source.pdf', root / 'translated.pdf', root / 'pages.docx'
            with pymupdf.open() as document:
                first = document.new_page(width=300, height=400)
                first.insert_text((40, 60), 'Hello')
                second = document.new_page(width=600, height=700)
                second.insert_text((40, 60), 'Hello world')
                document.save(source)
            worker.translate_pdf(source, translated, self.translator('en,fr'))
            with pymupdf.open(translated) as pdf:
                self.assertIn('Bonjour', ' '.join(pdf[0].get_text().split()))
                self.assertIn('Bonjour le monde', ' '.join(pdf[1].get_text().split()))
            worker.pdf_to_visual_docx(translated, target, lambda _: None)
            with ZipFile(target) as package:
                page_images = [name for name in package.namelist() if name.startswith('word/media/')]
                self.assertEqual(len(page_images), 2)
            document = docx.Document(target)
            self.assertEqual(len(document._element.body.xpath('.//w:drawing')), 2)

    def test_merge_equal_canvas_preserves_cover_form_and_page_links(self):
        import pymupdf
        import subprocess
        import sys
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with pymupdf.open() as source:
                source.new_page(width=400, height=600)
                source.new_page(width=800, height=700)
                page = source[0]
                page.insert_text((40, 60), "Cover", color=(0, 0, 1))
                page.insert_link({'kind': pymupdf.LINK_GOTO, 'from': pymupdf.Rect(30, 30, 100, 70), 'page': 1, 'to': pymupdf.Point(0, 0)})
                widget = pymupdf.Widget()
                widget.field_name = 'Name'
                widget.field_type = pymupdf.PDF_WIDGET_TYPE_TEXT
                widget.field_value = 'Elvira'
                widget.rect = pymupdf.Rect(40, 100, 180, 130)
                page.add_widget(widget)
                source.save(root / 'source.pdf')
            (root / 'inputs.txt').write_text(str(root / 'source.pdf'))
            subprocess.run([sys.executable, str(Path(__file__).resolve().parents[1] / 'document_worker.py'),
                'merge', str(root / 'inputs.txt'), str(root / 'output.pdf')], check=True, capture_output=True)
            with pymupdf.open(root / 'output.pdf') as output:
                self.assertEqual(len(output), 2)
                self.assertEqual(output[0].rect, output[1].rect)
                self.assertIn('Cover', output[0].get_text())
                self.assertEqual(list(output[0].widgets())[0].field_value, 'Elvira')
                self.assertEqual(output[0].get_links()[0]['page'], 1)


if __name__ == '__main__': unittest.main()
