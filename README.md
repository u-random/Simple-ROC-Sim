# Simple-ROC-Sim
This project serves as a simulator provider for a Remote Operation Center (ROC) demonstration, offering a hands-on experience of how operators remotely oversee and manage autonomous vessels navigating maritime environments. It deliberately avoids the technical sophistication and redundancy found in real-world, commercial systems. The purpose is rather to deliver an approachable and illustrative overview. This is aimed at laymen, students, or industry outsiders unfamiliar with the domain of autonomous maritime systems.

This is a further refined version on the previous work edvartGB and I did on the archived upstream. In addition to the new functionality, I've tried to improve the repository by cleaning extraneous files and restructuring the Assets/ directory.

## New Features
- Built upon the previously implemented Unity simulator platform 
- Migrated to Unity 6
- New WebSocket server system providing:
  - Camera streaming in the form of binary or base64 JPEG
  - Ship telemetry streaming with various parameters
  - Coming: Reception and execution of control signals from Remote Operation Center web-app
  - Coming: Multiple ships and objects in telemetry messages
- New Ship Control script for dual 360 degree engine setup

## Original Upstream Features
- ROS 2 integration with the [Unity ROS-TCP-Connector](https://github.com/Unity-Technologies/ROS-TCP-Connector)
- Simulation of common USV sensors, including 2D/3D LiDAR, RGB+D Camera, IMU and Odometry
- All sensors publish data to the ROS network
- Thrusters can subscribe to ROS topics for force and direction commands
- Ocean simulation utilizing Unity's built-in [HDRP Water System](https://blog.unity.com/engine-platform/new-hdrp-water-system-in-2022-lts-and-2023-1)
- Physics support for floating rigidbodies and vehicles (hydrostatics and hydrodynamics)
- Two hot-swappable buoyancy implementations (triangle- and voxel based
- Simple modular propulsion system
- The joy and usability of Unity!

## Setup instructions
More information to come here.

## Further information
For further details as to the underlying implementation, see the README on the archived upstream repository: https://github.com/edvartGB/unity_asv_sim/blob/main/readme.md
