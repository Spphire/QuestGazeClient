from __future__ import annotations

import argparse
import json
import socket
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from socket import timeout as SocketTimeout


def main() -> int:
    parser = argparse.ArgumentParser(description="Receive Quest recording telemetry UDP JSON lines.")
    parser.add_argument("--host", default="0.0.0.0", help="Local address to bind. Default: 0.0.0.0")
    parser.add_argument("--port", type=int, default=9100, help="UDP port. Default: 9100")
    parser.add_argument("--output", type=Path, help="Optional JSONL output path.")
    parser.add_argument("--print-samples", action="store_true", help="Print full sample JSON instead of compact status lines.")
    parser.add_argument("--max-messages", type=int, help="Exit after receiving this many valid JSON messages.")
    parser.add_argument("--timeout", type=float, help="Exit with status 2 if no datagram arrives for this many seconds.")
    parser.add_argument(
        "--wait-for-requirements",
        action="store_true",
        help=(
            "When requirement flags are set, keep receiving until all are satisfied "
            "or --timeout seconds elapse overall. With this flag, --max-messages is "
            "treated as the earliest success point, not an early failure point."
        ),
    )
    parser.add_argument("--require-gaze3d", action="store_true", help="Exit with status 3 unless at least one sample contains gazePoint3DWorld.")
    parser.add_argument("--require-left-controller-pose", action="store_true", help="Exit with status 3 unless at least one sample contains leftController.hasPose=true.")
    parser.add_argument("--require-right-controller-pose", action="store_true", help="Exit with status 3 unless at least one sample contains rightController.hasPose=true.")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    if args.timeout is not None:
        sock.settimeout(args.timeout)
    sock.bind((args.host, args.port))
    print(f"Listening on udp://{args.host}:{args.port}", flush=True)
    overall_deadline = (
        time.monotonic() + args.timeout
        if args.wait_for_requirements and args.timeout is not None
        else None
    )

    out = None
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        out = args.output.open("a", encoding="utf-8")

    try:
        received = 0
        sample_count = 0
        saw_gaze3d = False
        saw_left_controller_pose = False
        saw_right_controller_pose = False
        left_sources: dict[str, int] = {}
        right_sources: dict[str, int] = {}
        left_missing_reasons: dict[str, int] = {}
        right_missing_reasons: dict[str, int] = {}
        while True:
            try:
                data, addr = sock.recvfrom(65535)
            except SocketTimeout:
                print(f"Timed out waiting for UDP telemetry after {args.timeout} seconds.", file=sys.stderr, flush=True)
                print_summary(
                    received,
                    sample_count,
                    saw_gaze3d,
                    saw_left_controller_pose,
                    saw_right_controller_pose,
                    left_sources,
                    right_sources,
                    left_missing_reasons,
                    right_missing_reasons,
                    stream=sys.stderr,
                )
                missing = missing_requirements(
                    args,
                    saw_gaze3d,
                    saw_left_controller_pose,
                    saw_right_controller_pose,
                )
                if received > 0 and missing:
                    print(
                        "Telemetry requirement not satisfied: " + ", ".join(missing),
                        file=sys.stderr,
                        flush=True,
                    )
                    return 3

                return 2

            text = data.decode("utf-8", errors="replace").strip()
            if not text:
                continue

            try:
                message = json.loads(text)
            except json.JSONDecodeError:
                print(f"[bad-json] from {addr}: {text[:200]}", file=sys.stderr, flush=True)
                continue

            received += 1
            if out is not None:
                wrapped = {
                    "receivedUtc": datetime.now(timezone.utc).isoformat(),
                    "remote": f"{addr[0]}:{addr[1]}",
                    "message": message,
                }
                out.write(json.dumps(wrapped, ensure_ascii=False, separators=(",", ":")) + "\n")
                out.flush()

            msg_type = message.get("type", "?")
            seq = message.get("sequence", "?")
            record = message.get("recordId") or "-"
            if msg_type == "sample":
                sample_count += 1
                gaze = message.get("gazePoint3DWorld")
                left = message.get("leftController", {})
                right = message.get("rightController", {})
                saw_gaze3d = saw_gaze3d or is_vec3(gaze)
                saw_left_controller_pose = saw_left_controller_pose or has_controller_pose(left)
                saw_right_controller_pose = saw_right_controller_pose or has_controller_pose(right)
                count_source(left_sources, left)
                count_source(right_sources, right)
                count_missing_reason(left_missing_reasons, left)
                count_missing_reason(right_missing_reasons, right)

            if args.print_samples:
                print(json.dumps(message, ensure_ascii=False), flush=True)
                if should_stop_for_max_messages(
                    args,
                    received,
                    saw_gaze3d,
                    saw_left_controller_pose,
                    saw_right_controller_pose,
                ):
                    return validate_and_exit(
                        args,
                        received,
                        sample_count,
                        saw_gaze3d,
                        saw_left_controller_pose,
                        saw_right_controller_pose,
                        left_sources,
                        right_sources,
                        left_missing_reasons,
                        right_missing_reasons,
                    )
                continue

            if msg_type == "sample":
                print(
                    f"[{seq}] {record} sample gaze={gaze} "
                    f"L={left.get('position') if left.get('hasPose') else None} "
                    f"R={right.get('position') if right.get('hasPose') else None}",
                    flush=True,
                )
            else:
                print(f"[{seq}] {record} {msg_type}", flush=True)

            if should_stop_for_max_messages(
                args,
                received,
                saw_gaze3d,
                saw_left_controller_pose,
                saw_right_controller_pose,
            ):
                return validate_and_exit(
                    args,
                    received,
                    sample_count,
                    saw_gaze3d,
                    saw_left_controller_pose,
                    saw_right_controller_pose,
                    left_sources,
                    right_sources,
                    left_missing_reasons,
                    right_missing_reasons,
                )

            if overall_deadline is not None and time.monotonic() >= overall_deadline:
                print(
                    f"Timed out waiting for telemetry requirements after {args.timeout} seconds.",
                    file=sys.stderr,
                    flush=True,
                )
                return validate_and_exit(
                    args,
                    received,
                    sample_count,
                    saw_gaze3d,
                    saw_left_controller_pose,
                    saw_right_controller_pose,
                    left_sources,
                    right_sources,
                    left_missing_reasons,
                    right_missing_reasons,
                )
    except KeyboardInterrupt:
        print("Stopped.", flush=True)
        return 0
    finally:
        if out is not None:
            out.close()
        sock.close()


def is_vec3(value: object) -> bool:
    return (
        isinstance(value, list)
        and len(value) == 3
        and all(isinstance(item, (int, float)) for item in value)
    )


def has_controller_pose(value: object) -> bool:
    if not isinstance(value, dict) or not value.get("hasPose"):
        return False

    return is_vec3(value.get("position"))


def count_source(counter: dict[str, int], value: object) -> None:
    source = "missing"
    if isinstance(value, dict):
        raw_source = value.get("source")
        if isinstance(raw_source, str) and raw_source:
            source = raw_source

    counter[source] = counter.get(source, 0) + 1


def count_missing_reason(counter: dict[str, int], value: object) -> None:
    if not isinstance(value, dict) or value.get("hasPose"):
        return

    reason = value.get("missingReason")
    if not isinstance(reason, str) or not reason:
        reason = "unspecified"

    counter[reason] = counter.get(reason, 0) + 1


def should_stop_for_max_messages(
    args: argparse.Namespace,
    received: int,
    saw_gaze3d: bool,
    saw_left_controller_pose: bool,
    saw_right_controller_pose: bool,
) -> bool:
    if args.max_messages is None or received < args.max_messages:
        return False

    if not args.wait_for_requirements:
        return True

    return not missing_requirements(
        args,
        saw_gaze3d,
        saw_left_controller_pose,
        saw_right_controller_pose,
    )


def missing_requirements(
    args: argparse.Namespace,
    saw_gaze3d: bool,
    saw_left_controller_pose: bool,
    saw_right_controller_pose: bool,
) -> list[str]:
    missing = []
    if args.require_gaze3d and not saw_gaze3d:
        missing.append("gazePoint3DWorld")
    if args.require_left_controller_pose and not saw_left_controller_pose:
        missing.append("leftController.hasPose")
    if args.require_right_controller_pose and not saw_right_controller_pose:
        missing.append("rightController.hasPose")
    return missing


def print_summary(
    received: int,
    sample_count: int,
    saw_gaze3d: bool,
    saw_left_controller_pose: bool,
    saw_right_controller_pose: bool,
    left_sources: dict[str, int],
    right_sources: dict[str, int],
    left_missing_reasons: dict[str, int],
    right_missing_reasons: dict[str, int],
    *,
    stream,
) -> None:
    print(
        "Telemetry summary: "
        f"messages={received} samples={sample_count} "
        f"gaze3d={saw_gaze3d} "
        f"leftPose={saw_left_controller_pose} rightPose={saw_right_controller_pose} "
        f"leftSources={left_sources} rightSources={right_sources} "
        f"leftMissingReasons={left_missing_reasons} rightMissingReasons={right_missing_reasons}",
        file=stream,
        flush=True,
    )


def validate_and_exit(
    args: argparse.Namespace,
    received: int,
    sample_count: int,
    saw_gaze3d: bool,
    saw_left_controller_pose: bool,
    saw_right_controller_pose: bool,
    left_sources: dict[str, int],
    right_sources: dict[str, int],
    left_missing_reasons: dict[str, int],
    right_missing_reasons: dict[str, int],
) -> int:
    print_summary(
        received,
        sample_count,
        saw_gaze3d,
        saw_left_controller_pose,
        saw_right_controller_pose,
        left_sources,
        right_sources,
        left_missing_reasons,
        right_missing_reasons,
        stream=sys.stderr,
    )
    missing = missing_requirements(
        args,
        saw_gaze3d,
        saw_left_controller_pose,
        saw_right_controller_pose,
    )
    if missing:
        print(
            "Telemetry requirement not satisfied: " + ", ".join(missing),
            file=sys.stderr,
            flush=True,
        )
        return 3

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
