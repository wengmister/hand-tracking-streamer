<div align="center">
<img width="1224" height="500" alt="HTS logo" src="https://github.com/user-attachments/assets/7408e771-432e-435f-8f6e-d68656205a37" />
<h3 align="center">
    Meta Quest VR App for tracking and streaming hand and wrist landmark telemetry.
  </h3>
</div>
<p align="center">

  <a href="https://github.com/wengmister/hand-tracking-streamer/releases">
    <img src="https://img.shields.io/github/v/release/wengmister/hand-tracking-streamer.svg?include_prereleases&sort=semver" alt="Releases">
  </a>

  <a href="https://sidequestvr.com/app/46236">
   <img src="https://img.shields.io/badge/sideload-sidequest-FFD700?labelColor=grey" alt="SideQuest Release">
  </a>

  <a href="https://sidequestvr.com/app/46236">
   <img src="https://img.shields.io/badge/download-horizon_store-FF5757?labelColor=grey" alt="SideQuest Release">
  </a>

  <a href="https://github.com/wengmister/hand-tracking-streamer/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/license-Apache%20License%202.0-blue.svg" alt="Apache 2.0">
  </a>
</p>

**Hand Tracking Streamer** is a lightweight, high-fidelity hand telemetry utility that turns a Meta Quest headset into a precision controller for robotics teleoperation and motion capture. Built on the Meta Interaction SDK, it streams 21-landmark hand data and 6-DoF wrist pose to a PC in real time over Wi-Fi using UDP for ultra-low latency or TCP for reliable data logging. The app supports left, right, or dual-hand modes with in-headset configuration, and includes a live log console and phantom hand visualization for debugging. Data is sent as structured packets of joint positions and orientations in Unity world space, making it suitable for robot control, imitation learning, and gesture-based prototyping.


### What's New!

<details>
<summary>Click to expand changelog</summary>

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

You can sideload the app with [SideQuest](https://sidequestvr.com/app/46236/hand-tracking-streamer) for free, or directly upload `hand_tracking_streamer.apk` via ADB to your device.

>[!NOTE]
> Before direct upload, make sure your device is set to [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/), and allow USB connection.

Alternatively, you can build from source by loading `hand_tracking_streamer` project in [Unity](https://unity.com/download).

>[!NOTE]
> This app is built and tested on Unity 6000.0.65f1

## Data Streaming

See [CONNECTIONS](CONNECTIONS.md) page for detailed documentation on connections and data format. 

### Quick Start
Not ready to teleop your robot yet? Check out the simple socket and the visualizer script provided under [/scripts](/scripts) for quickly testing data streamed from your device.

## Demo

![wrist tracker](https://github.com/user-attachments/assets/b3a4ae36-00b1-4843-97d1-21ca7184bcd2)


![mjc_teleop](https://github.com/user-attachments/assets/033fc24b-be45-4ad3-8db2-b329871a4c70)


![dex_retarget](https://github.com/user-attachments/assets/bcbbbfcc-f3f8-4f73-883c-ea7acac19d03)



More teleop demo projects in [this repo](https://github.com/wengmister/quest_teleop_mujoco).


## Contact

For support or privacy inquiries related to Hand Tracking Streamer, please email: **wengmister@gmail.com**


## Citation
If you find this app useful, please consider citing as:

      @misc{weng2025levr,
            title={LeVR: A Modular VR Teleoperation Framework for Imitation Learning in Dexterous Manipulation}, 
            author={Zhengyang Kris Weng and Matthew L. Elwin and Han Liu},
            year={2025},
            eprint={2509.14349},
            archivePrefix={arXiv},
            primaryClass={cs.RO},
            url={https://arxiv.org/abs/2509.14349}, 
      }


## License
Apache-2.0
