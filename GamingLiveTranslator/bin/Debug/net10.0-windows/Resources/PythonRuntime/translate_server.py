#!/usr/bin/env python3
"""
Lightweight local HTTP translation microservice for Gaming Live Translator.
Uses Argos Translate directly (MIT/CC0 licensed) via Python's standard library http.server.
Binds strictly to loopback (127.0.0.1).
"""

import http.server
import json
import os
import sys
import threading
from urllib.parse import parse_qs, urlparse

# Force UTF-8 and MiniSBD (lightweight ONNX-based sentence boundary detection)
os.environ["PYTHONIOENCODING"] = "utf-8"
os.environ["ARGOS_CHUNK_TYPE"] = "MINISBD"

# pyright: reportMissingImports=false

# Dynamic safety patch for stanza if not installed
try:
    import argostranslate.sbd as _sbd  # type: ignore
    if not hasattr(_sbd, "_patched"):
        if getattr(_sbd, "stanza", None) is None:
            class _DummyStanza:
                class Pipeline:
                    def __init__(self, *a, **k): pass
                    def __call__(self, text):
                        class _Doc:
                            class _Sent:
                                def __init__(self, t): self.text = t
                            sentences = [_Sent(text)]
                        return _Doc()
            _sbd.stanza = _DummyStanza()
            _sbd._patched = True
except Exception:
    pass

import argostranslate.package  # type: ignore
import argostranslate.translate  # type: ignore

_lock = threading.Lock()

def normalize_lang_code(code: str) -> str:
    if not code:
        return "en"
    code = code.strip().lower()
    if code.startswith("zh"):
        return "zh"
    if "-" in code:
        return code.split("-")[0]
    return code


class TranslateServerHandler(http.server.BaseHTTPRequestHandler):
    def _send_json(self, status_code, data):
        response_bytes = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(response_bytes)))
        self.end_headers()
        self.wfile.write(response_bytes)

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == "/health":
            try:
                installed = argostranslate.package.get_installed_packages()
                models = [f"{p.from_code}->{p.to_code}" for p in installed]
                self._send_json(200, {
                    "status": "ok",
                    "installed_models": models
                })
            except Exception as ex:
                self._send_json(200, {"status": "ok", "error": str(ex)})

        elif path == "/packages":
            try:
                with _lock:
                    installed = argostranslate.package.get_installed_packages()
                    installed_pairs = {f"{p.from_code}_{p.to_code}": getattr(p, "package_version", "1.0") for p in installed}

                self._send_json(200, {
                    "installed": list(installed_pairs.keys()),
                    "status": "ready"
                })
            except Exception as ex:
                self._send_json(500, {"error": str(ex)})

        else:
            self._send_json(404, {"error": "Not found"})

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path

        content_length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_length).decode("utf-8") if content_length > 0 else "{}"
        try:
            payload = json.loads(body)
        except Exception:
            payload = {}

        if path == "/translate":
            q = payload.get("q", "")
            source = normalize_lang_code(payload.get("source", "en"))
            target = normalize_lang_code(payload.get("target", "hi"))

            if not q:
                self._send_json(200, {"translatedText": ""})
                return

            try:
                with _lock:
                    from_lang = argostranslate.translate.get_language_from_code(source)
                    to_lang = argostranslate.translate.get_language_from_code(target)

                    if from_lang is None or to_lang is None:
                        missing_code = source if from_lang is None else target
                        self._send_json(404, {
                            "error": f"Language package not installed for '{missing_code}'. Please download the model in Settings > Offline Language Packages."
                        })
                        return

                    translation = from_lang.get_translation(to_lang)
                    if translation is None:
                        self._send_json(404, {
                            "error": f"Language package not installed for '{source}' -> '{target}'. Please download the model in Settings > Offline Language Packages."
                        })
                        return

                    translated = translation.translate(q)
                self._send_json(200, {"translatedText": translated})
            except Exception as ex:
                self._send_json(500, {"error": str(ex)})

        elif path == "/packages/install":
            from_code = normalize_lang_code(payload.get("from_code", ""))
            to_code = normalize_lang_code(payload.get("to_code", ""))

            if not from_code or not to_code:
                self._send_json(400, {"error": "from_code and to_code are required"})
                return

            try:
                with _lock:
                    try:
                        argostranslate.package.update_package_index()
                    except Exception as uex:
                        print(f"Index update warning: {uex}", flush=True)

                    available = argostranslate.package.get_available_packages()
                    target_pkg = next((p for p in available if p.from_code == from_code and p.to_code == to_code), None)
                    if target_pkg is None:
                        self._send_json(404, {"error": f"Package {from_code}->{to_code} not found in index"})
                        return

                    download_path = target_pkg.download()
                    argostranslate.package.install_from_path(download_path)
                    try:
                        if download_path and os.path.exists(download_path):
                            os.remove(download_path)
                    except Exception:
                        pass

                self._send_json(200, {
                    "status": "installed",
                    "package": f"{from_code}->{to_code}"
                })
            except Exception as ex:
                self._send_json(500, {"error": str(ex)})

        else:
            self._send_json(404, {"error": "Not found"})

    def log_message(self, format, *args):
        # Keep subprocess stdio clean
        pass


def run_server(port):
    server = http.server.HTTPServer(("127.0.0.1", port), TranslateServerHandler)
    print(f"ArgosTranslate server listening on 127.0.0.1:{port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5000
    run_server(port)
