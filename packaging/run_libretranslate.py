"""Run FileConverter's bundled translation service and verify its local models."""

import os
import sys
from pathlib import Path

runtime = Path(__file__).resolve().parent
# Development builds copy this launcher beside the app but reuse the installed
# Python runtime. Models belong to that runtime, not to the copied script.
if not (runtime / "models").is_dir():
    runtime = Path(sys.executable).resolve().parent
data = Path(os.environ["LOCALAPPDATA"]) / "FileConverter" / "Translation"
data.mkdir(parents=True, exist_ok=True)
os.chdir(data)
os.environ["XDG_DATA_HOME"] = str(data / "data")
os.environ["XDG_CONFIG_HOME"] = str(data / "config")
os.environ["XDG_CACHE_HOME"] = str(data / "cache")
os.environ["ARGOS_PACKAGES_DIR"] = str(runtime / "models")
os.environ["ARGOS_CHUNK_TYPE"] = "MINISBD"
os.environ["ARGOS_MODEL_PROVIDER"] = "OPENNMT"
os.environ["ARGOS_DEVICE_TYPE"] = "cpu"
os.environ["ARGOS_DEBUG"] = "0"
os.environ.pop("LT_UPDATE_MODELS", None)
os.environ.pop("LT_FORCE_UPDATE_MODELS", None)

# A desktop launch has no console. Keep native/import/startup errors in a
# writable per-user file, including when installed under Program Files.
log = open(data / "engine.log", "a", encoding="utf-8", buffering=1)
if sys.argv[1:] != ["--self-test"]:
    sys.stdout = log
    sys.stderr = log
import faulthandler
faulthandler.enable(log)

from argostranslate.package import get_installed_packages

required = {("en", "fr"), ("fr", "en")}
available = {
    (package.from_code, package.to_code)
    for package in get_installed_packages()
    if (package.package_path / "minisbd" / (package.from_code + ".onnx")).is_file()
}
if not required <= available:
    sys.exit("The bundled English and French translation or sentence models are missing.")

if sys.argv[1:] == ["--self-test"]:
    from argostranslate.translate import translate

    for source, target, text in (
        ("en", "fr", "Hello, how are you today?"),
        ("fr", "en", "Bonjour, comment allez-vous ?"),
    ):
        translated = translate(text, source, target)
        if not translated.strip() or translated.strip().casefold() == text.casefold():
            sys.exit(f"Translation self-test failed for {source} to {target}.")
        print(f"{source} -> {target}: {translated}")
else:
    # FileConverter only needs two local JSON endpoints. Load Argos directly:
    # no web UI, online model installer, API-key database or cloud dependency.
    import argparse
    import json
    import threading
    import select
    import socket
    import traceback
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
    from argostranslate.translate import translate

    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1", choices=["127.0.0.1", "localhost"])
    parser.add_argument("--port", type=int, default=5000)
    args, _ = parser.parse_known_args()
    translation_lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def respond(self, code, value):
            body = json.dumps(value, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path.rstrip("/") == "/languages":
                self.respond(200, [
                    {"code": "en", "name": "English", "targets": ["fr"]},
                    {"code": "fr", "name": "French", "targets": ["en"]},
                ])
            else:
                self.respond(404, {"error": "Unknown endpoint"})

        def disconnected(self):
            try:
                readable, _, _ = select.select([self.connection], [], [], 0)
                return bool(readable) and not self.connection.recv(1, socket.MSG_PEEK)
            except OSError:
                return True

        def do_POST(self):
            if self.path.rstrip("/") != "/translate":
                self.respond(404, {"error": "Unknown endpoint"})
                return
            try:
                length = int(self.headers.get("Content-Length", 0))
                if not 0 < length <= 1048576:
                    raise ValueError("Invalid request size")
                payload = json.loads(self.rfile.read(length))
                source, target = payload["source"], payload["target"]
                if (source, target) not in required:
                    raise ValueError("Select English to French or French to English")
                texts = payload["q"]
                single = isinstance(texts, str)
                if single:
                    texts = [texts]
                if not isinstance(texts, list) or len(texts) > 32 or any(not isinstance(t, str) for t in texts):
                    raise ValueError("Invalid translation batch")
                with translation_lock:
                    results = []
                    for text in texts:
                        # A cancelled document closes its HTTP connection. Do not
                        # continue the remaining batch or delay the next document.
                        if self.disconnected(): return
                        results.append(translate(text, source, target))
                    if self.disconnected(): return
                self.respond(200, {"translatedText": results[0] if single else results})
            except (ValueError, KeyError) as error:
                self.respond(400, {"error": str(error)})
            except Exception as error:
                traceback.print_exc()
                self.respond(500, {"error": str(error)})

    print("Starting FileConverter offline translation engine", flush=True)
    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()
