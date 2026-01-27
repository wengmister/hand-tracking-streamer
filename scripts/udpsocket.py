"""Simple UDP listener for Hand Tracking Streamer (HTS).

The script can either:

* Log each UTF-8 message received over UDP, split by newline, or
* Tally how many UDP packets arrive per second (``--tally`` mode).

By default it listens on ``0.0.0.0:9000``. Run as::

	python udpsocket.py

Press Ctrl-C to stop.
"""

import argparse
import logging
import select
import signal
import socket
import time


def run_udp_listener(host: str, port: int, tally: bool = False) -> None:
	"""Listen for UDP packets and log or tally them.

	Parameters
	----------
	host:
		IP address or hostname to bind the UDP socket to.
	port:
		UDP port number to bind to.
	tally:
		If ``True``, only log the number of packets received per second.
		If ``False``, log the decoded message contents.
	"""

	sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
	sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
	sock.bind((host, port))
	sock.setblocking(False)

	logging.info("Listening for UDP on %s:%d", host, port)

	running = True

	def _handle_sigint(signum, frame):
		"""Signal handler that requests a clean shutdown."""

		del signum, frame  # unused in handler
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
				# keep drift small
				next_report += 1.0
	finally:
		try:
			sock.close()
		except OSError:
			# Best-effort cleanup; ignore close errors.
			pass


def main() -> None:
	"""Parse command-line arguments and start the UDP listener."""

	parser = argparse.ArgumentParser(
		prog="udpsocket",
		description=(
			"UDP message listener for Hand Tracking Streamer (HTS); "
			"either prints messages or tallies packets per second."
		),
	)
	parser.add_argument(
		"-p",
		"--port",
		type=int,
		default=9000,
		help="UDP port to listen on (default: 9000)",
	)
	parser.add_argument(
		"--host",
		default="0.0.0.0",
		help="Host/IP to bind to (default: 0.0.0.0)",
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

	run_udp_listener(args.host, args.port, args.tally)


if __name__ == "__main__":
	main()

