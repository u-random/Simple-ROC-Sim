# Simple-ROC-Sim
This project serves as a simulator provider for a Remote Operation Center (ROC) demonstration, offering a hands-on experience of how operators remotely oversee and manage autonomous vessels navigating maritime environments. It deliberately avoids the technical sophistication and redundancy found in real-world, commercial systems. The purpose is rather to deliver an approachable and illustrative overview. This is aimed at laymen, students, or industry outsiders unfamiliar with the domain of autonomous maritime systems.

## Features
- Built upon a previously implemented Unity simulator platform 
- Migrated to Unity 6
- New WebSocket server system providing:
  - Camera streaming in the form of base64 JPEG
  - Ship telemetry streaming with various parameters
  - Coming: Reception and execution of control signals from Remote Operation Center web-app
- Coming: New Ship Control script for dual 360 degree engine setup

## Setup instructions
More information to come here.

## Further information
For further details as to the underlying implementation, see the README on the archived upstream repository: https://github.com/edvartGB/unity_asv_sim/blob/main/readme.md
