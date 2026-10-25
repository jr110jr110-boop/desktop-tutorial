#!/usr/bin/env bash
# Starts the two mock LLM instances used by the CI smoke test and waits until both answer /health.
# primary 5091 (mock-primary), fallback 5092 (mock-fallback). Logs go to $MOCK_LLM_LOG_DIR (default: ./smoke-output).
set -u
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG="${MOCK_LLM_LOG_DIR:-$PWD/smoke-output}"
mkdir -p "$LOG"
python3 "$DIR/mock_llm.py" --port 5091 --model mock-primary > "$LOG/mock-llm-5091.log" 2>&1 &
python3 "$DIR/mock_llm.py" --port 5092 --model mock-fallback > "$LOG/mock-llm-5092.log" 2>&1 &
for port in 5091 5092; do
  up=0
  for i in $(seq 1 50); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health")" = "200" ]; then up=1; break; fi
    sleep 0.2
  done
  if [ "$up" != 1 ]; then echo "mock llm on port $port did not start" >&2; exit 1; fi
done
echo "mock llm ready on 5091 and 5092"
