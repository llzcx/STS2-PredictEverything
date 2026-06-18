<a id="readme-top"></a>

<!-- LANGUAGE SWITCH -->
<div align="center">

English | [简体中文](README_CN.md)

</div>

---

<!-- PROJECT LOGO -->
<br />
<div align="center">

<h3 align="center">&#128302; Predict Everything</h3>

  <p align="center">
    See every possible outcome before you click — plan your optimal path through the Crystal Sphere event.
    <br />
    <a href="https://github.com/llzcx/STS2-PredictEverything"><strong>Explore the docs &#xBB;</strong></a>
    <br />
  </p>

  <!-- PROJECT SHIELDS -->
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![License][license-shield]][license-url]

  <p align="center">
    <a href="https://github.com/llzcx/STS2-PredictEverything/issues/new?labels=bug&template=bug-report---.md">Report Bug</a>
    &middot;
    <a href="https://github.com/llzcx/STS2-PredictEverything/issues/new?labels=enhancement&template=feature-request---.md">Request Feature</a>
  </p>
</div>



<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#key-features">Key Features</a></li>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li>
      <a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#installation">Installation</a></li>
      </ul>
    </li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#how-it-works">How It Works</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## &#x1F4D6; About The Project

In the Crystal Sphere event, you spend gold to reveal hidden rewards — but the order you click determines what you get. Guess wrong and you waste gold on a path that doesn't lead to the card or relic you want.

Predict Everything **pre-simulates every possible click sequence**, showing you exactly what rewards appear at each step. Pick your target and the mod computes the **optimal gold-spending path** to reach it.

No more blind clicking. Every outcome, predicted.

### Key Features

- &#x1F9E0; **Full RNG Pre-Simulation** — Computes all 27 possible offset outcomes for Rare/Uncommon/Common cards, Relic, and Potions before you spend a single gold
- &#x1F3AF; **Smart Filter + Auto-Pathfinding** — Pick any card or relic from the dropdown, and the engine finds the optimal gold sequence (fewest golds → earliest finish). Unselected columns act as stepping stones — Relic is a last-resort fallback
- &#x1F4A1; **Hover Preview** — Mouse over any grid cell to see exactly what clicking it reveals, accounting for tool size (Big 3×3 / Small 1×1) and reveal order
- &#x1F4CB; **Locked Reward Dashboard** — Right-side panel tracks what you've already locked in, remaining potions, and gold left — with color-coded urgency
- &#x1F4A5; **Right-Click Detail Popup** — Right-click any card or relic name for a full preview window with description, rarity, and upgrade status
- &#x1F3A8; **Color-Coded Row States** — Locked rows (green), planned targets (gold border), reserved RNG slots (column-themed), and passed rows (dark red) — never lose track of your plan
- &#x1F4B0; **Gold Cost Transparency** — Every RNG slot cost visualized: Card +6, Relic +1, Gold +1, Potion +0. Plan summary always visible above the scroll area
- &#x1F504; **Reflective Potion Reading** — Potions are pre-determined before the event starts — the mod reads them via reflection, no RNG simulation needed
- &#x1F30D; **i18n Support** — Full Chinese / English switching, easy to extend
- &#x2699;&#xFE0F; **Config Hot-Reload** — Edit JSON config while the game runs; changes apply on next event entry

<p align="right">(<a href="#readme-top">back to top</a>)</p>



### Built With

- [![C#][CSharp]][CSharp-url] .NET 9.0
- [![Godot][Godot]][Godot-url] 4.5.1 Mono
- Harmony — Runtime patching

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- GETTING STARTED -->
## &#x1F680; Getting Started

### Prerequisites

- Slay the Spire 2 (Godot 4.5.1 Mono build)
- .NET 9.0 Runtime

### Installation

1. Download the latest `predict_everything.dll` and `manifest.json` from [Releases](https://github.com/llzcx/STS2-PredictEverything/releases)
2. Create a `mods/PredictEverything/` directory in your game folder
3. Ensure the structure looks like:
   ```
   mods/PredictEverything/
   ├── manifest.json
   ├── predict_everything.dll
   └── locale/
       ├── en.json
       └── zh.json
   ```
4. Launch the game — the prediction panel appears automatically when you enter a Crystal Sphere event

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- USAGE -->
## &#x1F4BB; Usage

### Quick Start

Enter a Crystal Sphere event. Two panels auto-appear:

| Panel | Position | Purpose |
|-------|----------|---------|
| **Prediction Grid** | Left | 4-column table (Rare / Uncommon / Common / Relic) × 27 rows — every possible outcome |
| **Locked Dashboard** | Right | Tracks locked rewards, potions, and remaining gold |

### Finding What You Want

**The easy way (recommended):**
1. Use the **dropdown filters** at the top of the prediction panel
2. Pick the card or relic you want
3. The engine auto-plans the optimal path — follow the green summary at the bottom

**The manual way:**
1. Scan the grid for the card/relic you want — note which row it's in
2. Click that row in the target column to set your plan
3. The plan summary shows exactly how many gold clicks before each pick

### Pro Tips

- **Hover** any card/relic name to see a quick tooltip with effects
- **Right-click** any card/relic name to open a full detail preview window
- **Hover** any hidden grid cell to preview what clicking it reveals (accounts for tool size!)
- Click **?** to re-read the tutorial anytime
- Drag panels by the title bar; click **▼** to collapse them
- The transparent fog mask (configurable) makes hidden items faintly visible

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- HOW IT WORKS -->
## &#x1F9EE; How It Works

```
CrystalSphere event starts
         │
         ▼
Read event RNG seed + counter via Harmony patch
         │
         ▼
Simulate RNG for all 27 offsets (0–26 gold clicks)
  ├── Predict cards:  simulates Rng.NextItem + upgrade roll
  ├── Predict relic:  reads RelicGrabBag._deques via reflection (no mutation)
  └── Read potions:   reads pre-determined PotionModel via reflection
         │
         ▼
Display full 27-row prediction grid
         │
         ▼
Player selects target card/relic via filter dropdown
         │
         ▼
Optimal path algorithm runs:
  • Permutes column reveal order
  • DFS search, max 7 gold budget
  • Minimizes gold spent, then earliest finish
  • Unselected columns = free stepping stones
  • Relic = last resort after gold is spent
         │
         ▼
Player follows the green plan, gold is spent optimally
```

### RNG Cost Model

| Action | RNG slots consumed |
|--------|-------------------|
| Gold pick (Small +10 / Big +30) | 1 |
| Card column reveal (3 cards) | 6 |
| Relic reveal | 1 |
| Potion reveal | 0 |
| Curse reveal | 0 |

**Same click reveal order:** Gold always reveals before cards/relics. Potions are pre-determined and don't consume RNG.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- ROADMAP -->
## &#x1F5FA;&#xFE0F; Roadmap

- [ ] Visual path highlight on planned rows (not just border)
- [ ] Cross-panel linking — click locked reward in Dashboard → scroll to that row
- [ ] Compact mode — collapse to a slim bar showing only the plan summary
- [ ] Interactive step-by-step tutorial
- [ ] Export/share prediction results as screenshots
- [ ] HoverPopup fade-in/out animation

See the [open issues](https://github.com/llzcx/STS2-PredictEverything/issues) for a full list of proposed features.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- CONTRIBUTING -->
## &#x1F91D; Contributing

Contributions are what make the open-source community amazing. Any contributions are **greatly appreciated**.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'feat: Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- LICENSE -->
## &#x1F4AD; License

Copyright &#xA9; 2025 [Shiang Chen](https://github.com/llzcx).

Released under the [MIT][license-url] license.

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- CONTACT -->
## &#x1F4E7; Contact

Shiang Chen — [@llzcx](https://github.com/llzcx)

Project Link: [https://github.com/llzcx/STS2-PredictEverything](https://github.com/llzcx/STS2-PredictEverything)

<p align="right">(<a href="#readme-top">back to top</a>)</p>



<!-- STAR HISTORY -->
## &#x2B50; Star History

<div align="center">
  <a href="https://star-history.com/#llzcx/STS2-PredictEverything&Date">
    <img src="https://api.star-history.com/svg?repos=llzcx/STS2-PredictEverything&type=Date" alt="Star History Chart" width="800">
  </a>
</div>



<!-- REFERENCE LINKS -->
[contributors-shield]: https://img.shields.io/github/contributors/llzcx/STS2-PredictEverything.svg?style=flat-round
[contributors-url]: https://github.com/llzcx/STS2-PredictEverything/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/llzcx/STS2-PredictEverything.svg?style=flat-round
[forks-url]: https://github.com/llzcx/STS2-PredictEverything/network/members
[stars-shield]: https://img.shields.io/github/stars/llzcx/STS2-PredictEverything.svg?style=flat-round
[stars-url]: https://github.com/llzcx/STS2-PredictEverything/stargazers
[issues-shield]: https://img.shields.io/github/issues/llzcx/STS2-PredictEverything.svg?style=flat-round
[issues-url]: https://github.com/llzcx/STS2-PredictEverything/issues
[license-shield]: https://img.shields.io/github/license/llzcx/STS2-PredictEverything.svg?style=flat-round
[license-url]: https://github.com/llzcx/STS2-PredictEverything/blob/master/LICENSE
[CSharp]: https://img.shields.io/badge/C%23-512BD4?style=flat-round&logo=csharp&logoColor=white
[CSharp-url]: https://dotnet.microsoft.com/en-us/languages/csharp
[Godot]: https://img.shields.io/badge/Godot-478CBF?style=flat-round&logo=godotengine&logoColor=white
[Godot-url]: https://godotengine.org/
