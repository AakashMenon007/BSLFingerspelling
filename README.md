# 👐 Immersive British Sign Language (BSL) Learning in AR/VR

An immersive Mixed Reality (MR) prototype designed to teach the **British Sign Language (BSL) alphabet** and basic **fingerspelling exercises** using Unity’s **XR Interaction Toolkit** and **XR Hands** package.

This project explores how **gesture recognition, real-time feedback, and user immersion** can enhance accessibility and motivation in sign language learning. It forms part of an MSc dissertation in Virtual Reality at Swansea University.

---

## 🎯 Project Overview

The BSL MR prototype bridges the gap between traditional learning and immersive digital education.  
It enables users to:
- Learn and practice BSL alphabets interactively.
- Receive **real-time gesture recognition feedback**.
- Engage with **fingerspelling tasks** that build vocabulary.
- Experience inclusively designed AR/VR interfaces accessible to beginner learners.

---

## 🧠 Research Background

Traditional BSL learning methods—books, videos, and classroom sessions—often lack interactivity and instant feedback.  
This project leverages **Augmented and Virtual Reality** to create an engaging and culturally authentic learning experience.  
It also aims to fill a market gap in **BSL-specific immersive learning tools**, which are less developed than their ASL counterparts.

---

## 🏗️ System Architecture

Built in **Unity (2022 LTS)** using:
- **XR Interaction Toolkit (v1.5)**
- **XR Hands Package**
- **Meta XR SDK (Quest/Quest 3)**
- **C# scripts for hand gesture capture and recognition**
- **Ghost-hand visual feedback system**

The prototype includes:
- **SavedHandPose Prefabs**: Store reference joint data for each BSL letter.  
- **Real-time Gesture Recognition**: Matches live XR Hands input using cosine similarity.
- **Fingerspelling Mode**: Guides learners through word construction (e.g., C–A–R).  
- **Visual Feedback**: Green tint and letter display upon correct sign formation.

---

## 🧩 Features

✅ **Real-time gesture tracking and recognition**  
✅ **Ghost-hand guidance system**  
✅ **Fingerspelling practice exercises**  
✅ **Immediate visual feedback**  
✅ **Data logging for performance and analysis**  
✅ **Inclusive and accessible design (hand dominance, pace, contrast)**  

---

## 🎮 Usage Instructions

1. **Clone this repository**  
   ```bash
   git clone https://github.com/<yourusername>/BSL-Fingerspelling-ARVR.git
