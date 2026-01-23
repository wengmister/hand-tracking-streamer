# Hand Tracking Streamer
 
Meta Quest 3/3s VR App for tracking and streaming hand and wrist landmark telemetry.

## Deployment

You can download the app with SideQuest for free, or directly upload `hand_tracking_streamer.apk` via ADB to your device.

>[!NOTE]
> Before direct upload, make sure your device is set to [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/), and allow USB connection.

Alternatively, you can build from source by loading `hand_tracking_streamer` project in [Unity](https://unity.com/download).

>[!NOTE]
> This app is built and tested on Unity 6000.0.65f1

## Demo

![wrist tracker](https://github.com/user-attachments/assets/b3a4ae36-00b1-4843-97d1-21ca7184bcd2)


![mjc_teleop](https://github.com/user-attachments/assets/033fc24b-be45-4ad3-8db2-b329871a4c70)


![dex_retarget](https://github.com/user-attachments/assets/bcbbbfcc-f3f8-4f73-883c-ea7acac19d03)



More teleop demo projects in [this repo](https://github.com/wengmister/quest_teleop_mujoco).


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
