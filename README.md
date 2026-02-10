<div align="center">
<img width="1224" height="500" alt="HTS logo" src="https://github.com/user-attachments/assets/7408e771-432e-435f-8f6e-d68656205a37" />
<h3 align="center">
    Meta Quest VR App for tracking and streaming hand and wrist landmark telemetry.
  </h3>
</div>
<p align="center">

  <a href="https://www.meta.com/experiences/hand-tracking-streamer/26303946202523164/">
   <img src="https://img.shields.io/badge/download-Meta_Quest_Store-FF5757?labelColor=grey" alt="Horizon Store Release">
  </a>

  <a href="https://github.com/wengmister/hand-tracking-streamer/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/license-Apache%20License%202.0-yellow.svg" alt="Apache 2.0">
  </a>

  <a href="https://zenodo.org/records/18601332">
    <img src="https://img.shields.io/badge/DOI-10.5281%2Fzenodo.18601332-blue.svg" alt="Zenodo DOI">
  </a>

  <a href="https://github.com/wengmister/hand-tracking-sdk">
    <img src="https://img.shields.io/badge/SDK-Python-green?labelColor=grey" alt="Python SDK">
  </a>
</p>

**Hand Tracking Streamer** is a lightweight hand telemetry utility that turns a Meta Quest headset into a precision controller for robotics teleoperation and motion capture. Built on the Meta Interaction SDK, it streams 21-landmark hand data and 6-DoF wrist pose to a PC in real time over Wi-Fi using UDP for ultra-low latency or TCP for reliable data logging. The app supports left, right, or dual-hand modes with in-headset configuration, and includes a live log console and phantom hand visualization for debugging. Data is sent as structured packets of joint positions and orientations in Unity world space, making it suitable for robot control, imitation learning, and gesture-based prototyping.


### What's New!

<details>
<summary>Click to expand changelog</summary>

v1.0.4:
- Added wireless TCP connection
  - This performs more consistently than wireless UDP and is not affected by batching
  - Now gracefully handles TCP connection interruption
- Enabled menu button to pause and evoke main menu during streaming
- Added network status indication on main menu
- Now loads last used connection config automatically on start up

v1.0.3:
- Added landmark visualization for tracked hands

![visualize_landmark](https://github.com/user-attachments/assets/35656d86-02eb-45ec-bb9b-7b4f4cdd3c83)

- Adjusted panel menu to include visualization toggle
- Fixed the issue where index of little finger were shifted by one

v1.0.2:
- Improved panel menu text readability
- Hand tracking frequency is now set to MAX

v1.0.1:
- Fixed the issue where sometimes panel menu would spawn on the floor
- Fixed missing splash screen

v1.0.0:
- Added in-app menu to allow streaming configurations:
  - Network protocol
  - IP Address
  - Port number
  - Hand side

- Added full bimanual tracking.
- Added synthetic hand visualization on tracked hands.
- Now available on SideQuest.

</details>


## Deployment

### AppStore Available

You can download the app from [Meta Quest Store](https://www.meta.com/experiences/hand-tracking-streamer/26303946202523164), or sideload the app with [SideQuest](https://sidequestvr.com/app/46236/hand-tracking-streamer) for free.

### Local Builds

Alternatively, you can build from source by loading `hand_tracking_streamer` project in [Unity](https://unity.com/download) or directly upload `hand_tracking_streamer.apk` via ADB to your device.

>[!NOTE]
>Before direct upload, make sure your device is set to [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/), and allow USB connection. This app is built and tested on Unity 6000.0.65f1

## Data Streaming

See [CONNECTIONS](CONNECTIONS.md) page for detailed documentation on connections and data format. 

### Quick Start
Not ready to integrate into your system yet? Check out the simple socket and the visualizer script provided under [/scripts](/scripts) for quickly testing data streamed from your device.

<details>
<summary>Click to see visualizer in action</summary>

Install dependencies, connect HTS, and simply run:

```python
python ./scripts/visualizer.py --protocol [YOUR PROTOCOL] --host [YOUR HOST IP] --port [YOUR PORT] --show-fingers
```
    
![visualizer](https://github.com/user-attachments/assets/431c994a-9287-4641-acb3-22e96c83b925)

</details>

## Python SDK

For integrating HTS data into your own pipelines, use the official [Python SDK](https://github.com/wengmister/hand-tracking-sdk). It provides typed data structures, parsers for the HTS packet format, and utilities for real-time visualization and logging, so you can go from streamed packets to usable hand pose data quickly. The package is published on [PyPI](https://pypi.org/project/hand-tracking-sdk/); see the [documentation](https://hand-tracking-sdk.readthedocs.io/en/latest/) for API details and examples.

```bash
# if using uv
uv add hand-tracking-sdk

# or install via pip
pip install hand-tracking-sdk
```

## Demo

![wrist tracker](https://github.com/user-attachments/assets/b3a4ae36-00b1-4843-97d1-21ca7184bcd2)


![mjc_teleop](https://github.com/user-attachments/assets/033fc24b-be45-4ad3-8db2-b329871a4c70)


![dex_retarget](https://github.com/user-attachments/assets/bcbbbfcc-f3f8-4f73-883c-ea7acac19d03)



More teleop demo projects in [this repo](https://github.com/wengmister/quest_teleop_mujoco).


## Contact

For support or privacy inquiries related to Hand Tracking Streamer, please email: **wengmister@gmail.com**


## Citation
If you use Hand Tracking Streamer in your research or project, please cite it as:

      @software{weng2026hts,
            author={Weng, Zhengyang K.},
            title={Hand Tracking Streamer: Meta Quest VR App for Motion Capture and Teleoperation},
            url={https://github.com/wengmister/hand-tracking-streamer},
            year={2026}
      }


## License
Apache-2.0
