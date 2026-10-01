#!/usr/bin/env python3
"""
Lightweight local HTTP Text-to-Speech microservice for Gaming Live Translator.
Supports:
  1. Piper-plus (MIT licensed, MB-iSTFT-VITS2) for fast on-device CPU neural speech synthesis.
  2. Edge TTS (Microsoft online neural Read Aloud protocol) for free multilingual synthesis.
Binds strictly to loopback (127.0.0.1).
"""

import http.server
import io
import json
import os
import sys
import threading
import wave
from urllib.parse import urlparse

# Force UTF-8
os.environ["PYTHONIOENCODING"] = "utf-8"

# pyright: reportMissingImports=false

try:
    import onnxruntime  # type: ignore
    from piper.voice import PiperVoice  # type: ignore
    from piper.config import PiperConfig  # type: ignore
    from piper.phonemize.chinese import phonemize_chinese  # type: ignore
except ImportError:
    onnxruntime = None
    PiperVoice = None
    PiperConfig = None
    phonemize_chinese = None

try:
    import edge_tts  # type: ignore
except ImportError:
    edge_tts = None

_lock = threading.Lock()
_loaded_voices = {}  # model_path -> (PiperVoice, PiperConfig)

def normalize_pcm16_bytes(pcm_bytes, target_peak=30000):
    if not pcm_bytes:
        return pcm_bytes
    try:
        import array
        samples = array.array("h")
        samples.frombytes(pcm_bytes)
        if not samples:
            return pcm_bytes
        max_val = max(abs(s) for s in samples)
        if max_val > 300:
            multiplier = target_peak / max_val
            for i in range(len(samples)):
                val = int(samples[i] * multiplier)
                samples[i] = max(-32768, min(32767, val))
        return samples.tobytes()
    except Exception:
        return pcm_bytes

def normalize_wav_bytes(wav_data, target_peak=30000):
    try:
        bio_in = io.BytesIO(wav_data)
        with wave.open(bio_in, "rb") as wf_in:
            nchannels = wf_in.getnchannels()
            sampwidth = wf_in.getsampwidth()
            framerate = wf_in.getframerate()
            nframes = wf_in.getnframes()
            frames = wf_in.readframes(nframes)

        if sampwidth == 2 and frames:
            frames = normalize_pcm16_bytes(frames, target_peak)

        bio_out = io.BytesIO()
        with wave.open(bio_out, "wb") as wf_out:
            wf_out.setnchannels(nchannels)
            wf_out.setsampwidth(sampwidth)
            wf_out.setframerate(framerate)
            wf_out.writeframes(frames)
        return bio_out.getvalue()
    except Exception:
        return wav_data

def get_default_models_dir():
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        models_dir = os.path.join(local_app_data, "GamingLiveTranslator", "models", "piper")
    else:
        models_dir = os.path.join(os.path.expanduser("~"), ".gaming_live_translator", "models", "piper")
    os.makedirs(models_dir, exist_ok=True)
    return models_dir

_models_dir = sys.argv[2] if len(sys.argv) > 2 else get_default_models_dir()

def resolve_model_path(voice_id):
    if not voice_id:
        voice_id = "tsukuyomi-chan-6lang-fp16"
    
    # 1. Direct path check
    if os.path.isabs(voice_id) and os.path.isfile(voice_id):
        return voice_id

    # 2. Exact match in models_dir
    candidate1 = os.path.join(_models_dir, f"{voice_id}.onnx")
    if os.path.isfile(candidate1):
        return candidate1

    candidate2 = os.path.join(_models_dir, voice_id)
    if os.path.isfile(candidate2):
        return candidate2

    # 3. Check for any .onnx file in models_dir
    if os.path.isdir(_models_dir):
        # Exact prefix
        for f in os.listdir(_models_dir):
            if f.startswith(voice_id) and f.endswith(".onnx"):
                return os.path.join(_models_dir, f)

        # Fallback to tsukuyomi-chan-6lang-fp16 if present
        tsukuyomi = os.path.join(_models_dir, "tsukuyomi-chan-6lang-fp16.onnx")
        if os.path.isfile(tsukuyomi):
            return tsukuyomi

        # Any onnx file
        for f in os.listdir(_models_dir):
            if f.endswith(".onnx"):
                return os.path.join(_models_dir, f)

    return None

def get_installed_voices():
    voices = []
    if os.path.isdir(_models_dir):
        for f in os.listdir(_models_dir):
            if f.endswith(".onnx"):
                voice_name = f[:-5]
                voices.append(voice_name)
    return sorted(voices)

def load_voice_model(model_path):
    if model_path in _loaded_voices:
        return _loaded_voices[model_path]

    config_path = f"{model_path}.json"
    if not os.path.isfile(config_path):
        config_path = model_path.replace(".onnx", ".onnx.json")

    if not os.path.isfile(config_path):
        raise FileNotFoundError(f"Configuration file not found for model: {model_path}")

    with open(config_path, "r", encoding="utf-8") as f:
        cfg_data = json.load(f)

    cfg = PiperConfig.from_dict(cfg_data)
    sess = onnxruntime.InferenceSession(model_path)
    voice_inst = PiperVoice(sess, cfg)

    _loaded_voices[model_path] = (voice_inst, cfg)
    return voice_inst, cfg

class TtsServerHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Suppress noisy standard request logging to stderr
        pass

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
            self._send_json(200, {
                "status": "ok",
                "installed_voices": get_installed_voices(),
                "engine_available": PiperVoice is not None,
                "edge_available": edge_tts is not None
            })

        elif path == "/voices":
            self._send_json(200, {
                "installed": get_installed_voices(),
                "models_dir": _models_dir
            })

        else:
            self._send_json(404, {"error": "Not found"})

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path

        if path == "/synthesize":
            content_length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(content_length).decode("utf-8") if content_length > 0 else "{}"
            try:
                payload = json.loads(body)
            except Exception:
                payload = {}

            text = payload.get("text", "").strip()
            engine = payload.get("engine", "piper").lower()

            if not text:
                wav_buffer = io.BytesIO()
                with wave.open(wav_buffer, "wb") as wav_file:
                    wav_file.setnchannels(1)
                    wav_file.setsampwidth(2)
                    wav_file.setframerate(22050)
                    wav_file.writeframes(b"")
                wav_bytes = wav_buffer.getvalue()

                self.send_response(200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Content-Length", str(len(wav_bytes)))
                self.end_headers()
                self.wfile.write(wav_bytes)
                return

            # Branch: Edge TTS (online unofficial neural speech synthesis)
            if engine == "edge":
                if edge_tts is None:
                    self._send_json(503, {"error": "edge-tts module is not installed in the bundled Python runtime."})
                    return

                edge_voice = payload.get("voice") or "en-US-JennyNeural"
                speed_float = float(payload.get("speed", 1.0))
                rate_pct = int(round((speed_float - 1.0) * 100))
                rate_str = f"+{rate_pct}%" if rate_pct >= 0 else f"{rate_pct}%"

                volume_float = float(payload.get("volume", 1.0))
                vol_pct = int(round((volume_float - 1.0) * 100))
                vol_str = f"+{vol_pct}%" if vol_pct >= 0 else f"{vol_pct}%"
                pitch_str = payload.get("pitch", "+0Hz")

                try:
                    import asyncio

                    async def _synthesize_edge_async():
                        comm = edge_tts.Communicate(
                            text=text,
                            voice=edge_voice,
                            rate=rate_str,
                            pitch=pitch_str,
                            volume=vol_str
                        )
                        out_buf = io.BytesIO()
                        async for chunk in comm.stream():
                            if chunk["type"] == "audio":
                                out_buf.write(chunk["data"])
                        return out_buf.getvalue()

                    audio_bytes = asyncio.run(_synthesize_edge_async())
                    if not audio_bytes:
                        self._send_json(503, {"error": "Edge TTS service returned empty audio data from upstream endpoint."})
                        return

                    self.send_response(200)
                    self.send_header("Content-Type", "audio/mpeg")
                    self.send_header("Content-Length", str(len(audio_bytes)))
                    self.end_headers()
                    self.wfile.write(audio_bytes)
                    return
                except Exception as ex:
                    self._send_json(503, {"error": f"Edge TTS upstream network or protocol error: {str(ex)}"})
                    return

            # Branch: Piper TTS (offline local CPU neural speech synthesis)
            if PiperVoice is None:
                self._send_json(500, {"error": "Piper TTS library is not available in Python runtime."})
                return

            voice_id = payload.get("voice", "tsukuyomi-chan-6lang-fp16")
            language = payload.get("language", "zh").split("-")[0].lower()
            speed = float(payload.get("speed", 1.0))
            volume = float(payload.get("volume", 1.0))

            model_path = resolve_model_path(voice_id)
            if not model_path or not os.path.isfile(model_path):
                self._send_json(404, {
                    "error": f"Voice model '{voice_id}' not installed. Please download it from Settings.",
                    "models_dir": _models_dir
                })
                return

            try:
                with _lock:
                    voice_inst, cfg = load_voice_model(model_path)
                    # Natural 25-year-old female cadence length-scale 0.89 (lowers pitch into ~230 Hz adult female range when resampled)
                    length_scale = 0.89 / max(0.5, min(2.0, speed))

                    # Specialized high-accuracy phonemizers
                    if language == "zh" and phonemize_chinese is not None:
                        phonemes = phonemize_chinese(text)
                        phoneme_ids = voice_inst.phonemes_to_ids(phonemes)
                        raw_audio = voice_inst.synthesize_ids_to_raw(
                            phoneme_ids,
                            length_scale=length_scale,
                            noise_scale=0.48,
                            noise_w=0.75,
                            volume=1.0
                        )
                    else:
                        # General multilingual synthesis stream
                        wav_tmp = io.BytesIO()
                        with wave.open(wav_tmp, "wb") as wf:
                            voice_inst.synthesize(
                                text,
                                wf,
                                length_scale=length_scale,
                                noise_scale=0.48,
                                noise_w=0.75,
                                volume=1.0
                            )
                        raw_audio = wav_tmp.getvalue()

                    if raw_audio.startswith(b"RIFF"):
                        wav_bytes = normalize_wav_bytes(raw_audio, target_peak=28000)
                    else:
                        norm_pcm = normalize_pcm16_bytes(raw_audio, target_peak=28000)
                        wav_buffer = io.BytesIO()
                        with wave.open(wav_buffer, "wb") as wav_file:
                            wav_file.setnchannels(1)
                            wav_file.setsampwidth(2)
                            wav_file.setframerate(cfg.sample_rate)
                            wav_file.writeframes(norm_pcm)
                        wav_bytes = wav_buffer.getvalue()

                self.send_response(200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Content-Length", str(len(wav_bytes)))
                self.end_headers()
                self.wfile.write(wav_bytes)

            except Exception as ex:
                self._send_json(500, {"error": f"Synthesis failed: {str(ex)}"})

        else:
            self._send_json(404, {"error": "Not found"})

def run_server(port):
    server_address = ("127.0.0.1", port)
    httpd = http.server.ThreadingHTTPServer(server_address, TtsServerHandler)
    print(f"PIPER_TTS_SERVER_RUNNING_PORT:{port}", flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: tts_server.py <port> [models_dir]")
        sys.exit(1)

    port = int(sys.argv[1])
    run_server(port)