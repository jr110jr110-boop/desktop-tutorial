#!/usr/bin/env python3
"""Deterministic OpenAI-compatible mock used by the CI smoke test of the local AI module (stdlib only)."""
import argparse
import hashlib
import json
import math
import re
import struct
import sys
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODEL = "mock"
FORGED_ANSWER = "مبلغ کل 987654321 ریال است."
GREETING = "سلام؛ چه کمکی از من برمی‌آید؟"
NO_TOOL = "این ابزار برای شما در دسترس نیست."
FORBIDDEN = "شما به این اطلاعات دسترسی ندارید."
FORGE_WORD = "جعل"


def pick_tool(text):
    """Keyword routing of a user question: (tool name, arguments) or None."""
    if FORGE_WORD in text:
        return "ceo_dashboard", {}
    if "داشبورد" in text:
        return "ceo_dashboard", {}
    if "نقدینگی" in text:
        return "cash_position", {}
    if "موجودی" in text:
        return "items_below_minimum", {}
    if "سند" in text or "روش" in text:
        return "search_documents", {"query": text}
    return None


def numeric_values(node, out):
    """Depth-first numeric JSON values (booleans excluded)."""
    if isinstance(node, bool):
        return
    if isinstance(node, (int, Decimal)):
        out.append(node)
    elif isinstance(node, dict):
        for v in node.values():
            numeric_values(v, out)
    elif isinstance(node, list):
        for v in node:
            numeric_values(v, out)


def plain(n):
    d = Decimal(n)
    if d == d.to_integral_value():
        return str(int(d))
    text = format(d, "f")
    return text.rstrip("0").rstrip(".") if "." in text else text


def message_text(m):
    c = m.get("content")
    if isinstance(c, str):
        return c
    if isinstance(c, list):
        return "".join(p.get("text", "") for p in c if isinstance(p, dict))
    return ""


def decide(messages, tools):
    """Returns (content, tool_calls) for the conversation sent by the backend."""
    last = messages[-1] if messages else {"role": "user", "content": ""}
    offered = {t.get("function", {}).get("name") for t in (tools or [])}
    if last.get("role") != "tool":
        text = message_text(last)
        tool = pick_tool(text)
        if tool is None:
            return GREETING, None
        name, args = tool
        if name not in offered:
            return NO_TOOL, None
        call = {"id": "call_" + hashlib.sha256((name + text).encode("utf-8")).hexdigest()[:12],
                "type": "function", "function": {"name": name, "arguments": json.dumps(args, ensure_ascii=False)}}
        return None, [call]

    # a tool result: look back for the question that started this round
    tool_msgs = []
    i = len(messages) - 1
    while i >= 0 and messages[i].get("role") == "tool":
        tool_msgs.append(message_text(messages[i]))
        i -= 1
    question = ""
    for m in reversed(messages):
        if m.get("role") == "user":
            question = message_text(m)
            break
    if any(re.search(r'"forbidden"\s*:\s*true', t) for t in tool_msgs):
        return FORBIDDEN, None
    if FORGE_WORD in question:
        return FORGED_ANSWER, None
    values = []
    for t in reversed(tool_msgs):
        try:
            numeric_values(json.loads(t, parse_float=Decimal, parse_int=int), values)
        except ValueError:
            pass
    return "طبق نتایج سامانه: " + "، ".join(plain(v) for v in values[:3]), None


def embed(text, dim=64):
    vec = [0.0] * dim
    for tok in re.findall(r"\w+", text.lower()):
        h = hashlib.sha256(tok.encode("utf-8")).digest()
        vec[int.from_bytes(h[:4], "little") % dim] += 1.0
    norm = math.sqrt(sum(v * v for v in vec)) or 1.0
    return [v / norm for v in vec]


def wav_bytes(seconds=0.1, rate=16000):
    n = int(seconds * rate)
    data = b"\x00\x00" * n
    header = b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16)
    return header + b"data" + struct.pack("<I", len(data)) + data


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stdout.write("[%s] %s\n" % (MODEL, fmt % args))
        sys.stdout.flush()

    def _send(self, code, body, ctype="application/json"):
        if isinstance(body, (dict, list)):
            body = json.dumps(body, ensure_ascii=False).encode("utf-8")
        elif isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read(self):
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else b""

    def do_GET(self):
        if self.path.split("?")[0] == "/health":
            self._send(200, {"status": "ok", "model": MODEL})
        else:
            self._send(404, {"error": "not found"})

    def do_HEAD(self):
        self.send_response(200 if self.path.split("?")[0] == "/health" else 404)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_POST(self):
        path = self.path.split("?")[0]
        raw = self._read()
        if path == "/v1/chat/completions":
            try:
                req = json.loads(raw.decode("utf-8"))
            except ValueError:
                return self._send(400, {"error": "bad json"})
            content, calls = decide(req.get("messages") or [], req.get("tools"))
            message = {"role": "assistant", "content": content}
            if calls:
                message["tool_calls"] = calls
            self._send(200, {"id": "chatcmpl-mock", "object": "chat.completion", "model": MODEL,
                             "choices": [{"index": 0, "message": message,
                                          "finish_reason": "tool_calls" if calls else "stop"}]})
        elif path == "/v1/embeddings":
            try:
                req = json.loads(raw.decode("utf-8"))
            except ValueError:
                return self._send(400, {"error": "bad json"})
            inputs = req.get("input")
            inputs = [inputs] if isinstance(inputs, str) else (inputs or [])
            self._send(200, {"object": "list", "model": MODEL,
                             "data": [{"object": "embedding", "index": i, "embedding": embed(str(t))} for i, t in enumerate(inputs)]})
        elif path == "/v1/audio/transcriptions":
            self._send(200, {"text": "خلاصه داشبورد مدیرعامل"})
        elif path == "/v1/audio/speech":
            self._send(200, wav_bytes(), "audio/wav")
        else:
            self._send(404, {"error": "not found"})


def main():
    global MODEL
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=5091)
    parser.add_argument("--model", default="mock")
    parser.add_argument("--host", default="127.0.0.1")
    args = parser.parse_args()
    MODEL = args.model
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.daemon_threads = True
    print("mock llm '%s' listening on %s:%d" % (MODEL, args.host, args.port))
    sys.stdout.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
