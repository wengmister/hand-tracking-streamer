"""Matplotlib visualizer for HTS hand landmarks with stable dynamic scaling.

Usage:
    python visualizer.py --protocol udp --host 0.0.0.0 --port 9000
    python visualizer.py --protocol tcp --host localhost --port 8000
"""

from __future__ import annotations

import argparse
import logging
import socket
import threading
import time
from dataclasses import dataclass, field
from typing import Dict, Iterable, Optional, Tuple

try:
    import matplotlib.pyplot as plt
    import numpy as np
except ImportError as exc:  # pragma: no cover
    raise SystemExit(
        "Missing dependency. Please install numpy and matplotlib."
    ) from exc


def _quat_normalize(quat: np.ndarray) -> np.ndarray:
    """Return a normalized quaternion."""
    norm = np.linalg.norm(quat)
    if norm <= 0.0:
        return np.array([0.0, 0.0, 0.0, 1.0], dtype=float)
    return quat / norm


def _quat_rotate(points: np.ndarray, quat: np.ndarray) -> np.ndarray:
    """Rotate Nx3 points by quaternion (x, y, z, w)."""
    quat = _quat_normalize(quat)
    q_xyz = quat[:3]
    q_w = quat[3]
    t = 2.0 * np.cross(q_xyz, points)
    return points + q_w * t + np.cross(q_xyz, t)


def _quat_to_matrix(quat: np.ndarray) -> np.ndarray:
    """Convert a quaternion (x, y, z, w) to a 3x3 rotation matrix."""
    quat = _quat_normalize(quat)
    x, y, z, w = quat
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    return np.array(
        [
            [1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy)],
            [2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx)],
            [2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy)],
        ],
        dtype=float,
    )


def _matrix_to_quat(mat: np.ndarray) -> np.ndarray:
    """Convert a 3x3 rotation matrix to a quaternion (x, y, z, w)."""
    trace = mat[0, 0] + mat[1, 1] + mat[2, 2]
    if trace > 0.0:
        s = 0.5 / np.sqrt(trace + 1.0)
        w = 0.25 / s
        x = (mat[2, 1] - mat[1, 2]) * s
        y = (mat[0, 2] - mat[2, 0]) * s
        z = (mat[1, 0] - mat[0, 1]) * s
    elif mat[0, 0] > mat[1, 1] and mat[0, 0] > mat[2, 2]:
        s = 2.0 * np.sqrt(1.0 + mat[0, 0] - mat[1, 1] - mat[2, 2])
        w = (mat[2, 1] - mat[1, 2]) / s
        x = 0.25 * s
        y = (mat[0, 1] + mat[1, 0]) / s
        z = (mat[0, 2] + mat[2, 0]) / s
    elif mat[1, 1] > mat[2, 2]:
        s = 2.0 * np.sqrt(1.0 + mat[1, 1] - mat[0, 0] - mat[2, 2])
        w = (mat[0, 2] - mat[2, 0]) / s
        x = (mat[0, 1] + mat[1, 0]) / s
        y = 0.25 * s
        z = (mat[1, 2] + mat[2, 1]) / s
    else:
        s = 2.0 * np.sqrt(1.0 + mat[2, 2] - mat[0, 0] - mat[1, 1])
        w = (mat[1, 0] - mat[0, 1]) / s
        x = (mat[0, 2] + mat[2, 0]) / s
        y = (mat[1, 2] + mat[2, 1]) / s
        z = 0.25 * s
    return _quat_normalize(np.array([x, y, z, w], dtype=float))


# Unity LH (x right, y up, z forward) -> RH (x front, y left, z up)
_UNITY_TO_RH = np.array(
    [
        [0.0, 0.0, 1.0],
        [-1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
    ],
    dtype=float,
)


def _convert_vec(vec: np.ndarray) -> np.ndarray:
    """Convert a vector from Unity LH to RH coordinates."""
    return _UNITY_TO_RH @ vec


def _convert_quat(quat: np.ndarray) -> np.ndarray:
    """Convert a quaternion from Unity LH to RH coordinates."""
    r_unity = _quat_to_matrix(quat)
    r_rh = _UNITY_TO_RH @ r_unity @ _UNITY_TO_RH.T
    return _matrix_to_quat(r_rh)


@dataclass
class HandState:
    """State for a single hand."""

    side: str
    wrist_position: Optional[np.ndarray] = None
    wrist_quat: Optional[np.ndarray] = None
    landmarks_local: Optional[np.ndarray] = None
    last_update: float = field(default_factory=time.monotonic)

    def update_wrist(self, data: Iterable[float]) -> None:
        """Update wrist pose from a 7-float sequence."""
        values = np.array(list(data), dtype=float)
        if values.size < 7:
            return
        self.wrist_position = _convert_vec(values[:3])
        self.wrist_quat = _convert_quat(values[3:7])
        self.last_update = time.monotonic()

    def update_landmarks(self, data: Iterable[float]) -> None:
        """Update local landmarks from a flat xyz array."""
        values = np.array(list(data), dtype=float)
        if values.size < 3:
            return
        if values.size % 3 != 0:
            values = values[: values.size - (values.size % 3)]
        reshaped = values.reshape((-1, 3))
        self.landmarks_local = (_UNITY_TO_RH @ reshaped.T).T
        self.last_update = time.monotonic()

    def world_points(self) -> Optional[np.ndarray]:
        """Return landmarks transformed to world space."""
        if self.landmarks_local is None:
            return None
        if self.wrist_position is None or self.wrist_quat is None:
            return self.landmarks_local
        return _quat_rotate(self.landmarks_local, self.wrist_quat) + self.wrist_position

    def wrist_point(self) -> Optional[np.ndarray]:
        """Return the wrist position if available."""
        if self.wrist_position is None:
            return None
        return self.wrist_position


def _parse_line(line: str) -> Optional[Tuple[str, str, Tuple[float, ...]]]:
    """Parse a CSV line into (side, kind, floats)."""
    parts = [part.strip() for part in line.split(",")]
    if not parts:
        return None
    label = parts[0].lower()
    if "wrist" not in label and "landmarks" not in label:
        return None
    side = "right" if "right" in label else "left" if "left" in label else ""
    if not side:
        return None
    kind = "wrist" if "wrist" in label else "landmarks"
    floats = []
    for part in parts[1:]:
        if not part:
            continue
        try:
            floats.append(float(part))
        except ValueError:
            continue
    return (side, kind, tuple(floats))


class StreamReceiver:
    """Background receiver for UDP/TCP hand data."""

    def __init__(self, protocol: str, host: str, port: int) -> None:
        self.protocol = protocol
        self.host = host
        self.port = port
        self.hands: Dict[str, HandState] = {
            "right": HandState("right"),
            "left": HandState("left"),
        }
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._conn_threads: list[threading.Thread] = []

    def start(self) -> None:
        """Start the receiver thread."""
        self._thread.start()

    def stop(self) -> None:
        """Stop the receiver thread and active connections."""
        self._stop.set()
        if self._thread.is_alive():
            self._thread.join(timeout=1.0)
        for thread in list(self._conn_threads):
            if thread.is_alive():
                thread.join(timeout=0.5)

    def _handle_line(self, line: str) -> None:
        parsed = _parse_line(line)
        if not parsed:
            return
        side, kind, floats = parsed
        hand = self.hands[side]
        if kind == "wrist":
            hand.update_wrist(floats)
        elif kind == "landmarks":
            hand.update_landmarks(floats)

    def _run_udp(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((self.host, self.port))
        sock.settimeout(0.5)
        logging.info("UDP listening on %s:%d", self.host, self.port)
        try:
            while not self._stop.is_set():
                try:
                    data, _addr = sock.recvfrom(65536)
                except socket.timeout:
                    continue
                except OSError:
                    break
                try:
                    message = data.decode("utf-8")
                except UnicodeDecodeError:
                    continue
                for line in message.splitlines():
                    if line:
                        self._handle_line(line)
        finally:
            sock.close()

    def _handle_tcp_conn(self, conn: socket.socket, addr) -> None:
        with conn:
            logging.info("Accepted connection from %s", addr)
            conn.settimeout(0.5)
            buffer = ""
            while not self._stop.is_set():
                try:
                    data = conn.recv(4096)
                except socket.timeout:
                    continue
                if not data:
                    break
                try:
                    buffer += data.decode("utf-8")
                except UnicodeDecodeError:
                    continue
                while "\n" in buffer:
                    line, buffer = buffer.split("\n", 1)
                    if line:
                        self._handle_line(line)

    def _run_tcp(self) -> None:
        server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server_sock.bind((self.host, self.port))
        server_sock.listen(1)
        server_sock.settimeout(0.5)
        logging.info("TCP server listening on %s:%d", self.host, self.port)
        try:
            while not self._stop.is_set():
                try:
                    conn, addr = server_sock.accept()
                except socket.timeout:
                    continue
                thread = threading.Thread(
                    target=self._handle_tcp_conn, args=(conn, addr), daemon=True
                )
                self._conn_threads.append(thread)
                thread.start()
        finally:
            server_sock.close()

    def _run(self) -> None:
        if self.protocol == "udp":
            self._run_udp()
        else:
            self._run_tcp()


def _set_axes_from_bounds(ax: plt.Axes, center: np.ndarray, limit: float) -> None:
    """Set axes limits and aspect from center and half-extent."""
    ax.set_xlim(center[0] - limit, center[0] + limit)
    ax.set_ylim(center[1] - limit, center[1] + limit)
    ax.set_zlim(center[2] - limit, center[2] + limit)
    try:
        ax.set_box_aspect([1.0, 1.0, 1.0])
    except Exception:
        pass


def _finger_segments(
    wrist: np.ndarray, landmarks: np.ndarray
) -> Tuple[Tuple[np.ndarray, np.ndarray], ...]:
    """Return line segments for finger trees based on streamed indices."""
    if landmarks.shape[0] >= 21:
        # Landmarks include wrist at index 0 (as seen in sample data).
        idx = landmarks
        thumb = (1, 2, 3, 4)
        index = (5, 6, 7, 8)
        middle = (9, 10, 11, 12)
        ring = (13, 14, 15, 16)
        little = (17, 18, 19, 20)
    else:
        # Landmarks exclude wrist; indices start at ThumbMetacarpal.
        idx = landmarks
        thumb = (0, 1, 2, 3)
        index = (4, 5, 6, 7)
        middle = (8, 9, 10, 11)
        ring = (12, 13, 14, 15)
        little = (16, 17, 18, 19)
    segments = []
    # Thumb: wrist -> 1 -> 2 -> 3 -> 4
    segments.append((wrist, idx[thumb[0]]))
    segments.append((idx[thumb[0]], idx[thumb[1]]))
    segments.append((idx[thumb[1]], idx[thumb[2]]))
    segments.append((idx[thumb[2]], idx[thumb[3]]))
    # Index: wrist -> 5 -> 6 -> 7 -> 8
    segments.append((wrist, idx[index[0]]))
    segments.append((idx[index[0]], idx[index[1]]))
    segments.append((idx[index[1]], idx[index[2]]))
    segments.append((idx[index[2]], idx[index[3]]))
    # Middle: wrist -> 9 -> 10 -> 11 -> 12
    segments.append((wrist, idx[middle[0]]))
    segments.append((idx[middle[0]], idx[middle[1]]))
    segments.append((idx[middle[1]], idx[middle[2]]))
    segments.append((idx[middle[2]], idx[middle[3]]))
    # Ring: wrist -> 13 -> 14 -> 15 -> 16
    segments.append((wrist, idx[ring[0]]))
    segments.append((idx[ring[0]], idx[ring[1]]))
    segments.append((idx[ring[1]], idx[ring[2]]))
    segments.append((idx[ring[2]], idx[ring[3]]))
    # Little: wrist -> 17 -> 18 -> 19 -> 20
    segments.append((wrist, idx[little[0]]))
    segments.append((idx[little[0]], idx[little[1]]))
    segments.append((idx[little[1]], idx[little[2]]))
    segments.append((idx[little[2]], idx[little[3]]))
    return tuple(segments)


def _init_finger_lines(ax: plt.Axes, color: str) -> list:
    lines = []
    for _ in range(20):
        (line,) = ax.plot([], [], [], color=color, linewidth=2)
        lines.append(line)
    return lines


def _update_finger_lines(lines: list, segments) -> None:
    for line, (start, end) in zip(lines, segments):
        line.set_data([start[0], end[0]], [start[1], end[1]])
        line.set_3d_properties([start[2], end[2]])


def run_visualizer(
    protocol: str,
    host: str,
    port: int,
    show_left: bool,
    show_right: bool,
    axis_limit: float,
    alpha: float,
    show_fingers: bool,
) -> None:
    """Run the matplotlib visualizer."""
    receiver = StreamReceiver(protocol=protocol, host=host, port=port)
    receiver.start()

    plt.ion()
    fig = plt.figure()
    ax = fig.add_subplot(111, projection="3d")
    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.set_zlabel("Z")
    try:
        ax.view_init(elev=10, azim=-170, roll=0)
    except TypeError:
        ax.view_init(elev=10, azim=-170)

    right_scatter = ax.scatter([], [], [], c="#E45756", s=20, label="Right")
    left_scatter = ax.scatter([], [], [], c="#4C78A8", s=20, label="Left")
    right_wrist = ax.scatter([], [], [], c="#B2333C", s=60, marker="x")
    left_wrist = ax.scatter([], [], [], c="#2D5E8D", s=60, marker="x")
    ax.scatter([0.0], [0.0], [0.0], c="#222222", s=40, marker="o")
    ax.legend(loc="upper right")

    right_lines = _init_finger_lines(ax, color="#FFE692") if show_fingers else []
    left_lines = _init_finger_lines(ax, color="#94FFDF") if show_fingers else []

    plt.show(block=False)

    cached_right_points = None
    cached_left_points = None
    ema_center = None
    ema_limit = axis_limit

    try:
        while plt.fignum_exists(fig.number):
            if show_right:
                right_points = receiver.hands["right"].world_points()
                if right_points is not None:
                    right_scatter._offsets3d = (
                        right_points[:, 0],
                        right_points[:, 1],
                        right_points[:, 2],
                    )
                    cached_right_points = right_points
                right_wrist_point = receiver.hands["right"].wrist_point()
                if right_wrist_point is not None:
                    right_wrist._offsets3d = (
                        [right_wrist_point[0]],
                        [right_wrist_point[1]],
                        [right_wrist_point[2]],
                    )
                if (
                    show_fingers
                    and right_points is not None
                    and right_wrist_point is not None
                ):
                    segments = _finger_segments(right_wrist_point, right_points)
                    _update_finger_lines(right_lines, segments)

            if show_left:
                left_points = receiver.hands["left"].world_points()
                if left_points is not None:
                    left_scatter._offsets3d = (
                        left_points[:, 0],
                        left_points[:, 1],
                        left_points[:, 2],
                    )
                    cached_left_points = left_points
                left_wrist_point = receiver.hands["left"].wrist_point()
                if left_wrist_point is not None:
                    left_wrist._offsets3d = (
                        [left_wrist_point[0]],
                        [left_wrist_point[1]],
                        [left_wrist_point[2]],
                    )
                if (
                    show_fingers
                    and left_points is not None
                    and left_wrist_point is not None
                ):
                    segments = _finger_segments(left_wrist_point, left_points)
                    _update_finger_lines(left_lines, segments)

            points_for_bounds = []
            if cached_right_points is not None:
                points_for_bounds.append(cached_right_points)
            if cached_left_points is not None:
                points_for_bounds.append(cached_left_points)

            if points_for_bounds:
                all_points = np.vstack(points_for_bounds)
                mins = all_points.min(axis=0)
                maxs = all_points.max(axis=0)
                center = (mins + maxs) * 0.5
                extent = (maxs - mins).max() * 0.5
                padding = max(extent * 0.3, 0.02)
                target_limit = max(extent + padding, 0.05)
                if ema_center is None:
                    ema_center = center
                    ema_limit = target_limit
                else:
                    ema_center = (1.0 - alpha) * ema_center + alpha * center
                    ema_limit = (1.0 - alpha) * ema_limit + alpha * target_limit
                _set_axes_from_bounds(ax, ema_center, float(ema_limit))

            fig.canvas.draw_idle()
            fig.canvas.flush_events()
            plt.pause(0.001)
    finally:
        receiver.stop()


def main() -> None:
    """Parse CLI args and start the visualizer."""
    parser = argparse.ArgumentParser(
        prog="visualizer",
        description="Matplotlib visualizer for HTS streams (stable scaling).",
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
        "--left-only",
        action="store_true",
        help="Only visualize the left hand.",
    )
    parser.add_argument(
        "--right-only",
        action="store_true",
        help="Only visualize the right hand.",
    )
    parser.add_argument(
        "--axis-limit",
        type=float,
        default=0.4,
        help="Initial axis limit for X/Y/Z (default: 0.4).",
    )
    parser.add_argument(
        "--alpha",
        type=float,
        default=0.1,
        help="EMA smoothing factor for dynamic scaling (default: 0.1).",
    )
    parser.add_argument(
        "--show-fingers",
        action="store_true",
        help="Render finger bone lines.",
    )
    args = parser.parse_args()

    if args.left_only and args.right_only:
        raise SystemExit("Choose only one of --left-only or --right-only.")

    show_left = not args.right_only
    show_right = not args.left_only

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
    )

    run_visualizer(
        protocol=args.protocol,
        host=args.host,
        port=args.port,
        show_left=show_left,
        show_right=show_right,
        axis_limit=args.axis_limit,
        alpha=args.alpha,
        show_fingers=args.show_fingers,
    )


if __name__ == "__main__":
    main()
