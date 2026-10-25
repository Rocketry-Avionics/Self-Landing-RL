# Autonomous Rocket Landing with Reinforcement Learning

A reinforcement learning project to achieve autonomous rocket propulsive landing in a physics-based simulation.

## Overview

This project simulates and trains a rocket to perform powered self-landing using Reinforcement Learning (RL) inside Unity.

## Video Demo

<video src="resources/Video%20Demo.mp4" controls="controls" width="100%">
  <a href="resources/Video%20Demo.mp4">Watch Demo Video</a>
</video>

Credit: Sushan Gopu

## Pipeline

1. **Simulation Environment**: Built in Unity with custom Earth physics, including accurate gravity and atmospheric dynamics.
2. **Rocket Model**: Structural airframe and inertial specifications provided by the rocketry structural team.
3. **Actuation and Control**: Thrust Vector Control (TVC) gimbaling and throttle management for attitude and descent control.
4. **Agent Training**: Train an RL agent to stabilize, guide, and land the booster autonomously on a designated pad.

## Old Structure (Initial Models)

| Rocket Design | Rocket with Thrust |
| :---: | :---: |
| ![Rocket Design](resources/rocket%20design.jpg) | ![Rocket with Thrust](resources/rocket%20with%20thrust.png) |

Credit: Adhiraj Dogra
