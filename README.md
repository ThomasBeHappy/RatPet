# 🐀 RatPet - Your Chaotic Digital Companion

> **Meet your new digital pet rat that's equal parts adorable and absolutely chaotic!** 

RatPet is a Windows desktop pet that brings joy, mischief, and pure chaos to your daily computing experience. This isn't just a cute pet - it's a full-featured digital companion with personality, intelligence, and a penchant for causing delightful mayhem! >:3

![RatPet Demo](https://github.com/ThomasBeHappy/RatPet/blob/main/ratbanner.png)

## ✨ Features That Will Blow Your Mind

### 🎭 **Core Personality**
- **Multiple States**: Idle, walking, sleeping, playing, stealing your cursor
- **Smart Movement**: Chases your mouse, explores windows, gets distracted by toys
- **Footprint System**: Leaves tiny paw prints wherever it goes (with proper rotation!)
- **Dynamic Sprites**: Smooth animations with idle, move, sleep, and play states

### 🎪 **Chaos Mode** 
*Enable this at your own risk!*
- **2x Speed**: Rat moves twice as fast
- **2x Mischief**: More likely to chase cursor and minimize windows
- **Zoomies**: Rat goes absolutely berserk, running everywhere at light speed

### 📸 **Photojournalist Mode**
Your rat is a digital paparazzi!
- **Auto Selfies**: Rat takes photos of itself in different locations
- **User Surveillance**: Captures what you're doing (with permission!)
- **Bad Website Detection**: Snaps "evidence" when you visit questionable sites
- **Smart Timing**: Random 200-600 second cooldowns between photos
- **Gallery Access**: Easy tray menu to view all photos
- **Toggle Control**: Enable/disable photojournalist mode from tray menu

### 📝 **Digital Diary**
Your rat is a nosy journalist!
- **Activity Detection**: Recognizes 50+ applications and activities
- **Smart Commentary**: Different diary styles based on what you're doing
- **Personality-Driven Entries**: Coding gets different commentary than gaming
- **Automatic Tracking**: Writes entries every 2 minutes when activities change

### 📊 **Statistics & Analytics**
Your rat is a data scientist!
- **Time Tracking**: Monitors how long you spend on different activities
- **Beautiful Reports**: Generates detailed reports with charts and commentary
- **Rat Analysis**: Provides hilarious commentary on your habits
- **Fun Facts**: Discovers interesting patterns in your behavior

### 🎮 **Interactive Features**
- **Toy System**: Drag toys from inventory to play with your rat
- **Meme Gallery**: Rat occasionally shows memes
- **Bubble Messages**: Rat communicates through speech bubbles

### 🔧 **Advanced Technical Features**
- **Sneak Mode**: Rat can hide behind windows
- **Browser URL Detection**: Uses UI Automation to read browser URLs
- **Window Management**: Smart window manipulation and detection
- **Performance Optimized**: Uses DrawingVisuals for smooth footprint rendering
- **Multi-Monitor Support**: Works across all your screens

## 🚀 Installation & Setup

### Prerequisites
- Windows 10/11 (64-bit)
- A sense of humor and tolerance for chaos
- **No additional software required!** (Self-contained deployment)

### Quick Start
1. **Download** the latest release from [Releases](https://github.com/ThomasBeHappy/RatPet/releases)
2. **Run** `RatPet.exe` (no installation required!)
3. **Right-click** the tray icon to access all features
4. **Enjoy** your new chaotic digital companion!

### Building from Source
If you want to build RatPet yourself:

```bash
# Clone the repository
git clone https://github.com/ThomasBeHappy/RatPet.git
cd RatPet

# Build self-contained release (Windows)
.\build-release.bat
# OR
.\build-release.ps1

# The executable will be in the publish/ folder
```

### First Time Setup
1. **Enable features** you want from the tray menu:
   - ⚙️ Settings
   - ✅ Sneak behind windows
   - ✅ Try to read browser URL (UIA)
   - ✅ Bad website detection
   - ✅ Photojournalist mode
   - ✅ CHAOS MODE >:3

2. **Customize** your experience:
   - Add memes to a folder and point RatPet to it
   - Adjust scale and behavior settings
   - Enable/disable specific features

## 🎯 Usage Guide

### Basic Controls
- **Tray Icon**: Right-click for full menu access
- **Drag & Drop**: Drag toys from inventory to play
- **Automatic**: Most features work automatically!

### Folder Structure
```
RatPet/
├── Photos/           # Rat's photography collection
├── RatDiary/         # Rat's journal entries
├── RatStats/         # Statistical reports
└── memes/            # Your meme collection
```

## 🎨 Customization

### Memes
- Create a `memes` folder under the app folder
- Add your favorite images
- Rat will randomly display them with bubbles

### Scale & Appearance
- Adjust rat size via tray menu
- Rat adapts to different screen resolutions
- Smooth scaling for all monitor sizes

### Behavior Tuning
- Enable/disable specific features
- Adjust chaos levels
- Customize photo frequency

## 🔍 Technical Details

### Architecture
- **Framework**: WPF (.NET 9.0)
- **Rendering**: DrawingVisual for high-performance graphics
- **Window Management**: Native Windows API integration
- **UI Automation**: Advanced browser interaction
- **Performance**: Optimized for smooth 60fps operation

### File Formats
- **Images**: PNG support for sprites and photos
- **Data**: Plain text for diary and statistics
- **Configuration**: In-memory settings with tray persistence

### Performance
- **Memory Efficient**: Object pooling for footprints
- **CPU Optimized**: Minimal impact on system resources
- **GPU Accelerated**: Hardware-accelerated rendering
- **Battery Friendly**: Smart update intervals

## 🎭 Rat Personality Examples

### When You're Coding
> *"Dear Diary, My human is writing code again! They keep mumbling about 'bugs'—but I haven't seen any! Should I go look for them? Maybe chew on some cables just in case! >:3"*

### When You're Gaming
> *"My human is playing Minecraft! They look so excited! I wonder if I can join them... Maybe I should run around really fast to match their energy! >:3"*

### When You're Browsing
> *"I saw them watching a video of a cat! Betrayal! I will retaliate with crumbs on the keyboard. Revenge will be swift. >:3"*

## 📊 Sample Statistics Report

```
🐀 RAT STATISTICS REPORT 🐀
Generated: 2025-10-17 14:30:22
Total observation time: 02:45:33

📊 ACTIVITY BREAKDOWN:
==================================================

🎯 writing code
   Time: 01:23:45
   Percentage: 50.2%
   Bar: ████████████████████████░

🎯 browsing the internet
   Time: 00:45:12
   Percentage: 27.3%
   Bar: █████████████░░░░░░░░░░░░

🎯 playing games
   Time: 00:36:36
   Percentage: 22.5%
   Bar: ███████████░░░░░░░░░░░░░░

🐀 RAT ANALYSIS:
==================================================

My human spends most of their time writing code! That's 1.4 hours! 
I'm so proud! >:3

🎲 FUN FACTS:
==================================================

• I observed 3 different activities today!
• My human switched activities 2 times!
• The longest single session was 45.2 minutes!
• I'm getting really good at this statistics thing!
• Maybe I should become a data analyst rat! >:3
```

## 🤝 Contributing

We'd love your help making RatPet even more chaotic! Here's how you can contribute:

### Bug Reports
- Found a bug? Open an issue with details
- Include steps to reproduce
- Attach any relevant logs or screenshots

### Feature Requests
- Have an idea? We'd love to hear it!
- Check existing issues first
- Describe the feature and why it would be awesome

### Code Contributions
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

### Ideas We're Looking For
- 🎨 New rat personalities and behaviors
- 🎮 More interactive features
- 🖼️ Additional customization options
- 🎵 Sound effects and audio
- 🌐 Multi-language support
- 📱 Mobile companion app

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **Inspiration**: Desktop pets from the 90s and early 2000s
- **Community**: All the amazing people who suggested features
- **Memes**: The internet for providing endless rat memes
- **Chaos**: The beautiful chaos that makes life interesting

## 🐀 Why You Need RatPet

Still not convinced? Here's why RatPet is the digital companion you never knew you needed:

- Its a rat, what else do you wanna hear!

## 🚨 Warning

**RatPet may cause:**
- 😂 Uncontrollable laughter
- 🎭 Decreased productivity through chaos
- 📸 Obsessive photo-taking habits
- 📝 Compulsive diary reading
- 🐀 Irresistible urge to get a real rat
- 💻 Complete transformation of your desktop experience

**Use responsibly!** >:3

---

## 🎉 Get Started Now!

Ready to meet your new digital best friend? Download RatPet today and prepare for the most chaotic, adorable, and entertaining desktop experience of your life!

[![Download RatPet](https://github.com/ThomasBeHappy/RatPet/blob/main/download.png)](https://github.com/ThomasBeHappy/RatPet/releases)

**Remember**: With great chaos comes great responsibility! >:3

---

*Made with ❤️ and 🐀 by developers who believe the world needs more digital chaos*
