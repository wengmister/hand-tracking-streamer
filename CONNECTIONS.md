# Connecting and Streaming Data from HTS

This document describes how Hand Tracking Streamer (HTS) streams hand and wrist data, and how to connect to it over UDP and TCP.

## Data format

HTS uses the OpenXR hand skeleton rig. Full details can be found in the [Meta documentation](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-interactions/).


<img width="1999" height="951" alt="OXR Hand" src="https://github.com/user-attachments/assets/b964ca21-3cd6-44c7-b225-be503ed17a65" />


The full OpenXR hand skeleton has 29 joints, but many of them are static (palm, metacarpals, etc.) and are not streamed to save bandwidth. HTS sends the wrist plus 21 tracked joints per hand.

Tracked joint indices:

```text
[OpenXR index // streamed index: joint name]
1,  // 0: Wrist
2,  // 1: ThumbMetacarpal
3,  // 2: ThumbProximal
4,  // 3: ThumbDistal
5,  // 4: ThumbTip
7,  // 5: IndexProximal
8,  // 6: IndexIntermediate
9,  // 7: IndexDistal
10, // 8: IndexTip
12, // 9: MiddleProximal
13, // 10: MiddleIntermediate
14, // 11: MiddleDistal
15, // 12: MiddleTip
17, // 13: RingProximal
18, // 14: RingIntermediate
19, // 15: RingDistal
20, // 16: RingTip
21, // 17: LittleProximal
22, // 18: LittleIntermediate
23, // 19: LittleDistal
24  // 20: LittleTip
```

Tracked data are streamed as UTF‑8 CSV lines, with a leading label indicating side and type:

```text
Right wrist:, 0.2502, 1.0635, 0.2540, 0.194, -0.116, 0.094, -0.970
Right landmarks:, 0.0000, 0.0000, 0.0000, -0.0275, -0.0197, 0.0362, -0.0438, -0.0335, 0.0608, -0.0418, -0.0480, 0.0913, -0.0329, -0.0595, 0.1111, -0.0236, -0.0073, 0.0960, -0.0179, -0.0226, 0.1302, -0.0150, -0.0428, 0.1435, -0.0116, -0.0633, 0.1518, -0.0017, -0.0025, 0.0956, 0.0054, -0.0226, 0.1329, 0.0081, -0.0456, 0.1478, 0.0100, -0.0680, 0.1589, 0.0175, -0.0065, 0.0887, 0.0252, -0.0222, 0.1236, 0.0279, -0.0407, 0.1424, 0.0282, -0.0595, 0.1580, 0.0230, -0.0094, 0.0341, 0.0351, -0.0137, 0.0779, 0.0433, -0.0227, 0.1061, 0.0477, -0.0342, 0.1223
```

- `Right wrist:`: 7 floats → `x, y, z, qx, qy, qz, qw`.
- `Right landmarks:`: 63 floats → `[x, y, z] * 21` in the joint order listed above.


## UDP connection

HTS can stream data via **wireless** UDP to a host on the same network as the Quest headset.

- Default target: `255.255.255.255:9000` (broadcast).
- You can change the target IP and port from the in‑game/network configuration menu.

>[!NOTE]
> UDP streaming performance (latency, packet loss, jitter) depends heavily on your local network conditions and traffic. For best results, use a stable Wi‑Fi network and avoid congested or high‑latency links.


## TCP connection

HTS can also stream data via **wired** TCP using ADB. Connect your Quest to your machine with a data‑capable USB‑C cable.

- Default target: `localhost:8000` via ADB reverse loopback.

>[!NOTE]
> TCP streaming over USB generally provides more consistent and reliable performance than wireless UDP. In practice, HTS over TCP tops out at around 70 Hz Quest hand tracking frequency.

Set up the TCP reverse mapping before starting streaming from the HTS app:

```bash
adb reverse tcp:8000 tcp:8000
```

You can verify that the reverse rule is active with:

```bash
adb reverse --list
```

## Troubleshooting

1. Have you enabled "Allow USB connection" on your Meta Quest?
   - Verify with `adb devices`. You should see your headset listed.
2. Make sure your firewall allows inbound traffic on the UDP/TCP port you are using.


