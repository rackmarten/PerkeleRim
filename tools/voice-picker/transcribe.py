# /// script
# requires-python = ">=3.10"
# dependencies = [
#     "faster-whisper>=1.1,<2",
#     "av>=15,<16",  # faster-whisper's decoder passes an argument PyAV 16 dropped
#     "nvidia-cublas-cu12>=12.4,<13",
#     "nvidia-cudnn-cu12>=9.1,<10",
# ]
# ///
"""Transcribes My Summer Car's clips for the voice picker, on your own GPU.

Runs faster-whisper (large-v3) twice over each clip VoiceExport wrote to tools/out/voices/audio/: once
in Finnish (what is said) and once translated to English. Writes tools/out/voices/transcripts.json
and transcripts.js, which the picker page loads from file://. Each clip gets its Finnish text, the
English translation and a confidence (the model's mean token probability, 0 to 1). The text is
Amistech's, so it stays in the gitignored tools/out/ folder like the audio.

Rerunnable: clips already in transcripts.json are skipped, and progress is saved as it goes, so an
interrupted run picks up where it stopped. Pass --redo to start over.

Usage (scripts/voice-picker.ps1 -Transcribe runs it):
    uv run tools/voice-picker/transcribe.py [--model large-v3] [--cpu] [--redo] [--groups Hangover,Drunk]
uv builds a private environment for it (faster-whisper plus CUDA 12's cuBLAS and cuDNN); nothing is
installed globally. The model (about 3 GB) downloads once into the Hugging Face cache.
"""

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

here = Path(__file__).resolve().parent
out = here.parent / "out" / "voices"
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")  # Windows without Developer Mode: the cache copies instead


def add_cuda_dlls():
    # The nvidia-* wheels put their DLLs under site-packages/nvidia/*/bin; CTranslate2 loads them
    # by name, so they go on the DLL search path and PATH before faster_whisper is imported.
    try:
        import nvidia
    except ImportError:
        return
    for root in nvidia.__path__:
        for lib in ("cublas", "cudnn", "cuda_nvrtc"):
            for sub in ("bin", "lib"):
                d = Path(root) / lib / sub
                if d.is_dir():
                    if hasattr(os, "add_dll_directory"):
                        os.add_dll_directory(str(d))
                    os.environ["PATH"] = str(d) + os.pathsep + os.environ.get("PATH", "")


def save(result):
    text = json.dumps(result, ensure_ascii=False, indent=1)
    for name, body in (("transcripts.json", text), ("transcripts.js", f"window.transcripts = {text};\n")):
        tmp = out / (name + ".tmp")
        tmp.write_text(body, encoding="utf-8", newline="\n")
        tmp.replace(out / name)


def run(model, path, task):
    # Each clip is one short line: no carried-over prompt (it makes Whisper repeat itself) and
    # no VAD (it cuts off grunts and one-word shouts).
    segments, _ = model.transcribe(str(path), language="fi", task=task, beam_size=5,
                                   condition_on_previous_text=False, without_timestamps=True, vad_filter=False)
    segments = list(segments)
    text = " ".join(s.text.strip() for s in segments).strip()
    logprob = sum(s.avg_logprob for s in segments) / len(segments) if segments else -10.0
    no_speech = max((s.no_speech_prob for s in segments), default=1.0)
    return text, math.exp(logprob), no_speech


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="large-v3")
    ap.add_argument("--cpu", action="store_true", help="run on the CPU (slow) instead of CUDA")
    ap.add_argument("--redo", action="store_true", help="ignore existing transcripts")
    ap.add_argument("--groups", default="", help="comma-separated groups to do (default: all)")
    args = ap.parse_args()

    index_path = out / "index.json"
    if not index_path.exists():
        sys.exit(f"No {index_path}: run scripts/voice-picker.ps1 first")
    rows = json.loads(index_path.read_text(encoding="utf-8-sig"))["rows"]
    wanted = {g.strip().lower() for g in args.groups.split(",") if g.strip()}

    path = out / "transcripts.json"
    result = {"model": args.model, "clips": {}}
    if path.exists() and not args.redo:
        result = json.loads(path.read_text(encoding="utf-8"))
        result.setdefault("clips", {})
    done = result["clips"]

    files = {}
    for r in rows:
        if r["file"] and r["id"] not in done and (not wanted or r["group"].lower() in wanted):
            files.setdefault(r["id"], out / r["file"])
    print(f"{len(done)} clips already transcribed, {len(files)} to go")
    if not files:
        save(result)
        return

    if not args.cpu:
        add_cuda_dlls()
    from faster_whisper import WhisperModel

    # int8_float16 keeps it to about 2 GB of VRAM, next to a running game.
    device, compute = ("cpu", "int8") if args.cpu else ("cuda", "int8_float16")
    model = WhisperModel(args.model, device=device, compute_type=compute)
    result["model"] = args.model

    start = time.time()
    for i, (key, p) in enumerate(files.items(), 1):
        text, confidence, no_speech = run(model, p, "transcribe")
        english = run(model, p, "translate")[0] if text and no_speech < 0.9 else ""
        done[key] = {"text": text, "english": english, "confidence": round(confidence, 3), "noSpeech": round(no_speech, 3)}
        if i % 50 == 0 or i == len(files):
            save(result)
            rate = i / (time.time() - start)
            print(f"{i}/{len(files)}  {rate:.1f} clips/s, {(len(files) - i) / rate / 60:.1f} min left", flush=True)
    print(f"Done in {(time.time() - start) / 60:.1f} min: {path}")


if __name__ == "__main__":
    main()
