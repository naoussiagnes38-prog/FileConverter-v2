"""Exercise the actual request handler without loading large language models."""
import ast
from pathlib import Path
import json
import select
import socket
import threading
import time
import traceback
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class CancellationTests(unittest.TestCase):
    def test_disconnect_stops_remaining_translation_batch(self):
        tree = ast.parse((Path(__file__).resolve().parents[1] / 'run_libretranslate.py').read_text())
        handler = next(node for node in ast.walk(tree) if isinstance(node, ast.ClassDef) and node.name == 'Handler')
        started, finished = threading.Event(), threading.Event()
        calls = []
        def translate(text, source, target):
            calls.append(text)
            started.set()
            time.sleep(.2)
            finished.set()
            return 'translated'
        scope = dict(BaseHTTPRequestHandler=BaseHTTPRequestHandler, json=json, select=select,
                     socket=socket, traceback=traceback, required={('en', 'fr'), ('fr', 'en')},
                     translation_lock=threading.Lock(), translate=translate)
        exec(compile(ast.Module(body=[handler], type_ignores=[]), '<actual Handler>', 'exec'), scope)
        server = ThreadingHTTPServer(('127.0.0.1', 0), scope['Handler'])
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            body = json.dumps({'q': ['first', 'second', 'third'], 'source': 'en', 'target': 'fr'}).encode()
            connection = socket.create_connection(server.server_address)
            connection.sendall(b'POST /translate HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: ' + str(len(body)).encode() + b'\r\n\r\n' + body)
            self.assertTrue(started.wait(3))
            connection.shutdown(socket.SHUT_RDWR)
            connection.close()
            self.assertTrue(finished.wait(3))
        finally:
            server.shutdown()
            server.server_close()
            thread.join(3)
        self.assertEqual(calls, ['first'])
