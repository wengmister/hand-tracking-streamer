"""Simple TCP listener for Hand Tracking Streamer (HTS).

The script can either:

* Log each UTF-8 message received over TCP, split by newline, or
* Tally how many TCP packets arrive per second (``--tally`` mode).

By default it listens on ``localhost:8000``. Run as::

    python tcpsocket.py

Press Ctrl-C to stop.
"""

import argparse
import logging
import socket
import time


def run_tcp_server(host: str, port: int, tally: bool = False) -> None:
    """Listen for TCP connections from HTS and log or tally messages.

    Parameters
    ----------
    host:
        Hostname or IP address to bind the TCP server socket to.
    port:
        TCP port number to bind to.
    tally:
        If ``True``, only log the number of packets received per second.
        If ``False``, log the decoded message contents.
    """

    server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_sock.bind((host, port))
    server_sock.listen(1)

    logging.info("TCP server listening on %s:%d", host, port)

    try:
        while True:
            logging.info("Waiting for a connection from HTS (Quest)...")
            conn, addr = server_sock.accept()
            with conn:
                logging.info("Accepted connection from %s", addr)

                msgs_this_second = 0
                next_report = time.monotonic() + 1.0

                while True:
                    data = conn.recv(4096)
                    if not data:
                        if tally and msgs_this_second:
                            logging.info(
                                "messages/sec (final interval): %d",
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
                                    logging.info(
                                        "Message from %s: %s", addr, line
                                    )
                        except UnicodeDecodeError:
                            logging.info("Message from %s: %s", addr, data)

                    if tally:
                        now = time.monotonic()
                        if now >= next_report:
                            logging.info(
                                "messages/sec: %d",
                                msgs_this_second,
                            )
                            msgs_this_second = 0
                            # keep drift small
                            next_report += 1.0
    finally:
        server_sock.close()


def main() -> None:
    """Parse command-line arguments and start the TCP listener."""

    parser = argparse.ArgumentParser(
        prog="tcpsocket",
        description=(
            "TCP message listener for Hand Tracking Streamer (HTS); "
            "either prints UTF-8 messages or tallies packets per second."
        ),
    )
    parser.add_argument(
        "-p",
        "--port",
        type=int,
        default=8000,
        help="TCP port to listen on (default: 8000)",
    )
    parser.add_argument(
        "--host",
        default="localhost",
        help="Host/IP to bind to (default: localhost)",
    )
    parser.add_argument(
        "--tally",
        action="store_true",
        help=(
            "Only count messages per second instead of printing "
            "each decoded message"
        ),
    )
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
    )

    run_tcp_server(args.host, args.port, args.tally)


if __name__ == "__main__":
    main()