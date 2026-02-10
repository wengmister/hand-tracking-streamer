"""TCP/UDP listener for Hand Tracking Streamer (HTS).

The script can either:
    - Log each UTF-8 message received, split by newline, or
    - Tally how many packets arrive per second (``--tally`` mode).

Examples:
    python sockets.py --protocol udp --host 0.0.0.0 --port 9000
    python sockets.py --protocol tcp --host localhost --port 8000
"""

from __future__ import annotations

import argparse
import logging
import select
import signal
import socket
import threading
import time


def run_udp_listener(host: str, port: int, tally: bool) -> None:
    """Listen for UDP packets and log or tally them."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((host, port))
    sock.setblocking(False)

    logging.info("Listening for UDP on %s:%d", host, port)

    running = True

    def _handle_sigint(signum, frame):
        """Signal handler that requests a clean shutdown."""
        del signum, frame
        nonlocal running
        running = False

    signal.signal(signal.SIGINT, _handle_sigint)
    signal.signal(signal.SIGTERM, _handle_sigint)

    msgs_this_second = 0
    next_report = time.monotonic() + 1.0

    try:
        while running:
            timeout = max(0.0, next_report - time.monotonic())
            ready, _, _ = select.select([sock], [], [], timeout)
            if ready:
                try:
                    data, addr = sock.recvfrom(65536)
                except BlockingIOError:
                    continue

                if tally:
                    msgs_this_second += 1
                else:
                    try:
                        message = data.decode("utf-8")
                        for line in message.split("\n"):
                            if line:
                                logging.info("Message from %s: %s", addr, line)
                    except UnicodeDecodeError:
                        logging.info("Message from %s: %s", addr, data)

            now = time.monotonic()
            if now >= next_report and tally:
                logging.info("messages/sec: %d", msgs_this_second)
                msgs_this_second = 0
                next_report += 1.0
    finally:
        try:
            sock.close()
        except OSError:
            pass


def handle_tcp_connection(conn, addr, tally: bool) -> None:
    """Handle a single TCP connection in a separate thread."""
    with conn:
        logging.info("Accepted connection from %s", addr)

        msgs_this_second = 0
        next_report = time.monotonic() + 1.0

        try:
            while True:
                data = conn.recv(4096)
                if not data:
                    if tally and msgs_this_second:
                        logging.info(
                            "messages/sec (final interval) from %s: %d",
                            addr,
                            msgs_this_second,
                        )
                    logging.info("Connection from %s closed", addr)
                    break

                if tally:
                    msgs_this_second += 1
                else:
                    try:
                        message = data.decode("utf-8")
                        for line in message.split("\n"):
                            if line:
                                logging.info("Message from %s: %s", addr, line)
                    except UnicodeDecodeError:
                        logging.info("Message from %s: %s", addr, data)

                if tally:
                    now = time.monotonic()
                    if now >= next_report:
                        logging.info("messages/sec from %s: %d", addr, msgs_this_second)
                        msgs_this_second = 0
                        next_report += 1.0
        except Exception as e:
            logging.error("Error handling connection from %s: %s", addr, e)


def run_tcp_server(host: str, port: int, tally: bool) -> None:
    """Listen for TCP connections from HTS and handle each in a separate thread."""
    server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_sock.bind((host, port))
    server_sock.listen(5)  # Allow multiple pending connections

    logging.info("TCP server listening on %s:%d", host, port)
    logging.info("Waiting for connections from HTS (Quest)...")
    logging.info("Remember to setup TCP reverse first: 'adb reverse tcp:%d tcp:%d'", port, port)

    try:
        while True:
            conn, addr = server_sock.accept()
            # Handle each connection in a separate thread
            thread = threading.Thread(
                target=handle_tcp_connection,
                args=(conn, addr, tally),
                daemon=True
            )
            thread.start()
    finally:
        server_sock.close()


def _default_host(protocol: str) -> str:
    return "0.0.0.0" if protocol == "udp" else "localhost"


def _default_port(protocol: str) -> int:
    return 9000 if protocol == "udp" else 8000


def main() -> None:
    """Parse command-line arguments and start the listener."""
    parser = argparse.ArgumentParser(
        prog="sockets",
        description=(
            "TCP/UDP message listener for Hand Tracking Streamer (HTS); "
            "either prints messages or tallies packets per second."
        ),
    )
    parser.add_argument(
        "--protocol",
        choices=("udp", "tcp"),
        default="udp",
        help="Transport protocol to listen on (default: udp).",
    )
    parser.add_argument(
        "-p",
        "--port",
        type=int,
        default=None,
        help="Port to listen on (default: 9000 for UDP, 8000 for TCP).",
    )
    parser.add_argument(
        "--host",
        default=None,
        help="Host/IP to bind to (default: 0.0.0.0 for UDP, localhost for TCP).",
    )
    parser.add_argument(
        "--tally",
        action="store_true",
        help="Only count messages per second instead of printing each message.",
    )
    args = parser.parse_args()

    host = args.host or _default_host(args.protocol)
    port = args.port or _default_port(args.protocol)

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
    )

    if args.protocol == "udp":
        run_udp_listener(host, port, args.tally)
    else:
        run_tcp_server(host, port, args.tally)


if __name__ == "__main__":
    main()
