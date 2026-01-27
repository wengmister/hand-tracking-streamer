# Connecting and Streaming Data from HTS

## Data format

The current HTS setup utilizes OpenXR Hand Skeleton rig. Full details can be found on [this Meta documentation](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-interactions/).

While the full index list of OXR Hand list have 29 entries, many of them are rather static and not helpful for landmark tracking (start/palm/finger metacarpals) and therefore not streamed to save space.

Full tracked index can be found below:

    [OXR Index // streamed index: joint name]
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


Tracked finger data are streamed back in utf-8 CSV, with a leading label of type and side:

    Right wrist:, 0.2502, 1.0635, 0.2540, 0.194, -0.116, 0.094, -0.970 // x, y, z, qx, qy, qz, qw
    Right landmarks:, 0.0000, 0.0000, 0.0000, -0.0275, -0.0197, 0.0362, -0.0438, -0.0335, 0.0608, -0.0418, -0.0480, 0.0913, -0.0329, -0.0595, 0.1111, -0.0236, -0.0073, 0.0960, -0.0179, -0.0226, 0.1302, -0.0150, -0.0428, 0.1435, -0.0116, -0.0633, 0.1518, -0.0017, -0.0025, 0.0956, 0.0054, -0.0226, 0.1329, 0.0081, -0.0456, 0.1478, 0.0100, -0.0680, 0.1589, 0.0175, -0.0065, 0.0887, 0.0252, -0.0222, 0.1236, 0.0279, -0.0407, 0.1424, 0.0282, -0.0595, 0.1580, 0.0230, -0.0094, 0.0341, 0.0351, -0.0137, 0.0779, 0.0433, -0.0227, 0.1061, 0.0477, -0.0342, 0.1223 // [x, y, z] * 21 sequentially




## UDP connection

Allows streaming data via **wireless** UDP communication with Quest headset. You'll need to make sure the target device and vr headset are on the same WLAN that allow device discovery.

Defaults to broadcasting to 255.255.255.255 at port 9000. Can be configured in in-game menu.


## TCP connection

Allows streaming data via **wired** TCP communication with Quest headset via ADB. You'll need to connect your device with a data capable USB-C cable. 

Defaults to localhost at port 8000 for ADP reverse loopback.

You will need to setup TCP connection prior to start streaming from HTS App.

``bash
    adb reverse tcp:8000 tcp:8000
``

and you should be able to verify with

``bash
    adb reverse --list
``

## Troubleshooting

1. have you enabled `allow USB connection` from your Meta Quest?
- Verify with command `adb devices`. You should see your headset coming up

2. setup firewall inbound rules for the port number you're trying to use.

