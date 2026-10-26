<p align="center">
  <img src="apps/manager/public/stellar-breath-logo.png" width="104" alt="habitOrbit logo">
</p>

<h1 align="center">habitOrbit · 星以为常</h1>

<p align="center"><strong>A desktop planet counter: finish something, click once.</strong></p>

<p align="center"><a href="README.md">简体中文</a> · English</p>

<p align="center">
  <a href="#features">Features &amp; Demos</a> ·
  <a href="#start">Download &amp; Usage</a> ·
  <a href="#why">Motivation &amp; Related Research</a> ·
  <a href="#credits">Acknowledgments &amp; License</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square" alt="Windows x64">
  <img src="https://img.shields.io/badge/version-0.1.0-547A68?style=flat-square" alt="Version 0.1.0">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--only-4B6584?style=flat-square" alt="GPL-3.0-only"></a>
</p>

---

I often sit at my computer for hours at a time. My water cup is right beside me, but once I get absorbed in a project, I forget to drink—and forget to get up and move. Sometimes I remember to drink a few cups, then can't recall how much I actually drank by the evening. Opening an app and typing in a number every time feels like an unnecessary interruption for such a small thing.

Another inspiration came from a [2024 study](https://doi.org/10.1111/sms.14628). In an experiment with 18 overweight or obese men, researchers found that breaking up sitting with three minutes of walking or squatting every 45 minutes produced a lower post-meal blood glucose response than one continuous 30-minute walk. It made me pay more attention to short bouts of activity spread throughout the day. Even getting out of the chair for a few minutes deserves to be taken seriously.

So I made habitOrbit, hoping to make these everyday things easier to remember and easier to record. Each activity can become a little planet on the desktop. After finishing a cup of water, click its planet to record the volume you set in advance; after an activity break, click that activity's planet. You can adjust the planets' colors and lighting, too, keeping the small things you want to stick with in view.

<a id="features"></a>

## ✨ Features & Demos

### Create a planet and set its recording rules and appearance

Create a planet for an activity, give it a name, then choose how much one click records and which unit to use. For drinking water, for example, set each click to 200 milliliters. Choose colors and adjust lighting while watching the preview, then save. The unit and amount per click are fixed after creation; the appearance can be changed later.

### Click a desktop planet to record

For squats, set “one click = 10 repetitions.” After a set, left-click the center of the planet to add 10 to today's total. If you click by mistake, right-click to undo today's latest entry. You do not need to switch to the manager window to record. Drag to move the planet; the decorative area around it lets clicks pass through to the window underneath.

You can record counts, quantities, and manually entered durations—for example, 10 squats or five minutes of walking per click. Durations are entered in minutes.

![Clicking desktop planets displays activity records](docs/media/desktop-record.gif)

### Drag cards to organize activities

The manager displays activities as cards. Drag them to reorder or group them, and put frequently used activities in the quick-access bar at the top. Quick-access status and desktop visibility are controlled separately.

### Record matrix and history

The record matrix shows multiple activities side by side by date, so you can see which days have records. Open an individual planet's details to adjust today's total, divide the day's activity into groups, and review an annual heatmap or bar charts covering the past 7, 30, or 90 days.

![The record matrix shows daily records for reading, drinking water, and exercise, with the number of recorded days in the selected period](docs/media/statistics.png)

### Local storage and backups

Data stays on your computer. Closing the manager window leaves the desktop planets and background recording service running. Choose “完全退出” (Exit completely) from the tray to quit the whole application. Records, appearance, and layout are restored the next time you open it.

In “设置与备份” (Settings & Backup), export a `.pulse-backup` file to save records and layout. Restoring a backup replaces the current data; the application saves a copy of the current data first. Archive activities you are not doing for now while keeping their history.

<a id="start"></a>

## 🚀 Download & Usage

For **Windows 10 version 2004 or later, x64**. The installer includes the required runtime dependencies. You do not need Node.js, Rust, or the .NET SDK for everyday use.

1. **Download and install.** Download the [Windows x64 installer](https://github.com/Lucian-D-W/habitOrbit/releases/download/v0.1.0/habitOrbit-Setup-x64.exe), run `habitOrbit-Setup-x64.exe`, and open “星以为常 habitOrbit” from the Start menu.
2. **Create your first planet.** Enter a name, unit, and amount per click, choose an appearance, and save. Double-click the overview card or click its monitor button in the upper-right corner to show the planet on the desktop.
3. **Record an activity.** Left-click the center of the desktop planet to add an entry; right-click to undo. To see details, select a card in the manager and press Enter.
4. **Keep a backup.** Export a `.pulse-backup` file from Settings & Backup and save it in your backup location. Use the tray's Exit completely command when you want to quit.

See the [user guide](docs/user-guide.md) for operations, data locations, and backups. Starting with Windows is off by default and can be enabled in settings. Developers can use the [development and build guide](docs/development.md).

<a id="why"></a>

## 💡 Motivation & Related Research

**References**

**1. Enhanced muscle activity during interrupted sitting improves glycemic control in overweight and obese men**  
Gao, Li, Finni, and Pesola, 2024. *Scandinavian Journal of Medicine & Science in Sports*, 34(4), e14628. [Original article](https://doi.org/10.1111/sms.14628). In a randomized crossover study with 18 overweight or obese men, three-minute walking or squatting breaks every 45 minutes during a sitting session produced a lower post-meal blood glucose response than one continuous 30-minute walk. This short-term finding prompted my interest in recording small bouts of activity throughout the day.

**2. What influences the selection of contextual cues when starting a new routine behaviour? An exploratory study**  
Stawarz, Gardner, Cox, and Blandford, 2020. *BMC Psychology*, 8, 29. [Original article](https://doi.org/10.1186/s40359-020-0394-9). Through a three-week daily vitamin-taking task and interviews with 39 participants, the study explored how people choose everyday cues. Keeping things close at hand or in a visible place was common; relying on a single cue or vague plans could fail, and a constantly visible cue could gradually lose its salience.

**3. Atomic Habits and the Four Laws of Behavior Change**  
James Clear, 2018. *Atomic Habits*. [Excerpt on the author's website](https://jamesclear.com/three-steps-habit-change). The book translates cues, cravings, responses, and rewards into four design questions: how to make a behavior obvious, attractive, easy, and satisfying. This practical framework is one of my references when thinking about habit tools.

**Design references and application**

What matters most to me is whether I still want to record something after doing it. If every set means opening a window, finding an activity, and entering a number, I am likely to find it a hassle. So I set the quantity in advance and made the recording button a planet that stays on the desktop. The appearance is worth spending time on, too: I will see it many times a day, and I want to like it. These readings gave me points of reference; the specific choices came from how I wanted to use this counter myself.

<a id="credits"></a>

## 🙏 Acknowledgments & License

Thanks to [metasidd/Orb](https://github.com/metasidd/Orb) for inspiration for the planets' appearance, colors, and animation parameters, and to [AOSP Launcher3](https://android.googlesource.com/platform/packages/apps/Launcher3/+/refs/tags/android-11.0.0_r2/) for references for card reordering and grouping. Desktop rendering uses Win2D. The manager uses Tauri, React, dnd-kit, ECharts, react-activity-calendar, and Lucide; recording uses .NET and SQLite. See [third-party notices](THIRD_PARTY_NOTICES.md) for attribution and licenses.

Copyright © 2026 **Lucian W. (lucian-d-w)**. The original habitOrbit code is licensed under **[GPL-3.0-only](LICENSE)**; see the [copyright and license scope](COPYRIGHT.md). Third-party code and assets retain their respective licenses and notices.
