"""Measure inter-arrival time of HTS messages over UDP or TCP.

Defaults to line-level timing (each CSV line). For UDP, a single packet
may contain multiple lines; each line is counted separately.

Usage:
	python interarrival.py --protocol udp --host 0.0.0.0 --port 9000
	python interarrival.py --protocol tcp --host localhost --port 8000
"""

from __future__ import annotations

import argparse
import logging
import socket
import time
from typing import Iterable, List, Optional, Tuple


def _percentile(sorted_values: List[float], pct: float) -> float:
	if not sorted_values:
		return 0.0
	if pct <= 0.0:
		return sorted_values[0]
	if pct >= 100.0:
		return sorted_values[-1]
	index = (len(sorted_values) - 1) * (pct / 100.0)
	low = int(index)
	high = min(low + 1, len(sorted_values) - 1)
	weight = index - low
	return sorted_values[low] * (1.0 - weight) + sorted_values[high] * weight


class InterArrivalStats:
	def __init__(self, report_interval: float):
		self.report_interval = report_interval
		self.last_time: Optional[float] = None
		self.intervals: List[float] = []
		self.count = 0
		self.next_report = time.monotonic() + report_interval

	def add(self, timestamp: float) -> None:
		if self.last_time is not None:
			self.intervals.append(timestamp - self.last_time)
		self.last_time = timestamp
		self.count += 1

	def maybe_report(self) -> None:
		now = time.monotonic()
		if now < self.next_report:
			return
		self._report()
		self._reset(now)

	def _report(self) -> None:
		if not self.intervals:
			logging.info("messages=%d (no intervals yet)", self.count)
			return
		values = sorted(self.intervals)
		n = len(values)
		mean = sum(values) / n
		min_v = values[0]
		max_v = values[-1]
		p50 = _percentile(values, 50.0)
		p90 = _percentile(values, 90.0)
		p99 = _percentile(values, 99.0)
		logging.info(
			"messages=%d intervals=%d mean=%.2fms min=%.2fms p50=%.2fms p90=%.2fms p99=%.2fms max=%.2fms",
			self.count,
			n,
			mean * 1000.0,
			min_v * 1000.0,
			p50 * 1000.0,
			p90 * 1000.0,
			p99 * 1000.0,
			max_v * 1000.0,
		)

	def _reset(self, now: float) -> None:
		self.intervals.clear()
		self.count = 0
		self.next_report = now + self.report_interval


def _iter_lines_from_udp(sock: socket.socket) -> Tuple[Iterable[str], Tuple[str, int]]:
	data, addr = sock.recvfrom(65536)
	try:
		message = data.decode("utf-8")
	except UnicodeDecodeError:
		return [], addr
	return [line for line in message.splitlines() if line], addr


def _iter_lines_from_tcp(conn: socket.socket, buffer: str) -> Tuple[Iterable[str], str]:
	data = conn.recv(4096)
	if not data:
		return [], buffer
	try:
		buffer += data.decode("utf-8")
	except UnicodeDecodeError:
		return [], buffer
	lines = []
	while "\n" in buffer:
		line, buffer = buffer.split("\n", 1)
		if line:
			lines.append(line)
	return lines, buffer


def run_udp(host: str, port: int, report_interval: float, handshake: bool) -> None:
	sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
	sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
	sock.bind((host, port))
	sock.settimeout(0.5)
	logging.info("UDP listening on %s:%d", host, port)

	stats = InterArrivalStats(report_interval)
	try:
		while True:
			try:
				lines, addr = _iter_lines_from_udp(sock)
			except socket.timeout:
				stats.maybe_report()
				continue
			if handshake:
				try:
					sock.sendto(b"\x00", addr)
				except OSError:
					pass
			for _line in lines:
				stats.add(time.monotonic())
			stats.maybe_report()
	finally:
		sock.close()


def run_tcp(host: str, port: int, report_interval: float) -> None:
	server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
	server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
	server_sock.bind((host, port))
	server_sock.listen(1)
	server_sock.settimeout(0.5)
	logging.info("TCP server listening on %s:%d", host, port)

	stats = InterArrivalStats(report_interval)
	try:
		while True:
			try:
				conn, addr = server_sock.accept()
			except socket.timeout:
				stats.maybe_report()
				continue
			logging.info("Accepted connection from %s", addr)
			with conn:
				conn.settimeout(0.5)
				buffer = ""
				while True:
					try:
						lines, buffer = _iter_lines_from_tcp(conn, buffer)
					except socket.timeout:
						stats.maybe_report()
						continue
					if not lines and not buffer:
						break
					for _line in lines:
						stats.add(time.monotonic())
					stats.maybe_report()
	finally:
		server_sock.close()


def main() -> None:
	parser = argparse.ArgumentParser(
		prog="interarrival",
		description="Measure inter-arrival time for HTS stream messages.",
	)
	parser.add_argument(
		"--protocol",
		choices=("udp", "tcp"),
		default="udp",
		help="Transport protocol to listen on (default: udp).",
	)
	parser.add_argument(
		"--host",
		default="0.0.0.0",
		help="Host/IP to bind to (default: 0.0.0.0 for UDP).",
	)
	parser.add_argument(
		"--port",
		type=int,
		default=9000,
		help="Port to listen on (default: 9000 for UDP).",
	)
	parser.add_argument(
		"--report-interval",
		type=float,
		default=1.0,
		help="Seconds between stats reports (default: 1.0).",
	)
	parser.add_argument(
		"--handshake",
		action="store_true",
		help="Send a 1-byte UDP packet back to the sender per received packet.",
	)
	args = parser.parse_args()

	logging.basicConfig(
		level=logging.INFO,
		format="%(asctime)s %(levelname)s %(message)s",
	)

	if args.protocol == "udp":
		run_udp(args.host, args.port, args.report_interval, args.handshake)
	else:
		run_tcp(args.host, args.port, args.report_interval)


if __name__ == "__main__":
	main()
