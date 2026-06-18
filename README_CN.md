<a id="readme-top"></a>
<!-- LANGUAGE SWITCH -->
<div align="center">

[English](README.md) | 简体中文

</div>

---

<br />
<div align="center">

<h3 align="center">&#128302; 预测一切 (Predict Everything)</h3>

  <p align="center">
    在水晶球占卜事件中提前预知一切——不再盲点，用数据找到最优路径。
    <br />
    <a href="https://github.com/llzcx/STS2-PredictEverything"><strong>查看文档 &#xBB;</strong></a>
    <br />
  </p>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![License][license-shield]][license-url]

  <p align="center">
    <a href="https://github.com/llzcx/STS2-PredictEverything/issues/new?labels=bug&template=bug-report---.md">报告 Bug</a>
    &middot;
    <a href="https://github.com/llzcx/STS2-PredictEverything/issues/new?labels=enhancement&template=feature-request---.md">功能建议</a>
  </p>
</div>



<details>
  <summary>目录</summary>
  <ol>
    <li>
      <a href="#项目简介">项目简介</a>
      <ul>
        <li><a href="#核心功能">核心功能</a></li>
        <li><a href="#技术栈">技术栈</a></li>
      </ul>
    </li>
    <li>
      <a href="#快速开始">快速开始</a>
      <ul>
        <li><a href="#运行环境">运行环境</a></li>
        <li><a href="#安装步骤">安装步骤</a></li>
      </ul>
    </li>
    <li><a href="#使用指南">使用指南</a></li>
    <li><a href="#工作原理">工作原理</a></li>
    <li><a href="#路线图">路线图</a></li>
    <li><a href="#参与贡献">参与贡献</a></li>
    <li><a href="#许可证">许可证</a></li>
    <li><a href="#联系方式">联系方式</a></li>
  </ol>
</details>



## &#x1F4D6; 项目简介

水晶球占卜事件中，你需要花金币揭露隐藏的奖励——但点击顺序决定了你能拿到什么。点错了，金币就浪费在没有你想要的卡牌或遗物的路线上。

预测一切会**预先模拟所有可能的点击顺序**，告诉你每一步会得到什么。选定目标后，MOD 自动计算出**最优的金币消耗路径**。

不再盲点。一切结局，提前预知。

### 核心功能

- &#x1F9E0; **全量 RNG 预模拟** — 在花费金币之前，计算全部 27 个偏移位置的金卡/蓝卡/白卡/遗物/药水结果
- &#x1F3AF; **智能筛选 + 自动寻路** — 从下拉框选择任意目标卡牌或遗物，引擎自动规划最优路径（金币消耗最少 → 结束偏移最早）。未选中的卡牌列充当跳板，遗物仅在金币耗尽后兜底
- &#x1F4A1; **悬停预览** — 鼠标悬停任意格子，实时预览点击后会揭示什么，自动适配工具大小（大锤 3×3 / 小锤 1×1）和揭示顺序
- &#x1F4CB; **锁定奖励面板** — 右侧面板追踪已锁定的奖励、剩余药水和金币，金币不足时自动变红警告
- &#x1F4A5; **右键详情弹窗** — 右键任意卡牌或遗物名称，弹出完整预览窗口，查看效果描述、稀有度和升级状态
- &#x1F3A8; **行状态色彩编码** — 已锁定（绿底）、计划目标（金边）、RNG 预留（列专属色）、已错过（暗红）——始终清楚当前位置
- &#x1F4B0; **金币消耗透明** — 每一步 RNG 消耗可视化：卡牌 +6、遗物 +1、金币 +1、药水 +0。计划摘要固定在滚动区上方，无需翻页
- &#x1F504; **药水反射读取** — 药水在事件开始前已确定，通过反射直接读取，无需 RNG 模拟
- &#x1F30D; **双语支持** — 完整的中文/英文切换，易于扩展
- &#x2699;&#xFE0F; **配置热加载** — 游戏运行时编辑 JSON 配置，下次进入事件自动生效

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



### 技术栈

- [![C#][CSharp]][CSharp-url] .NET 9.0
- [![Godot][Godot]][Godot-url] 4.5.1 Mono
- Harmony — 运行时补丁

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F680; 快速开始

### 运行环境

- 杀戮尖塔 2（Godot 4.5.1 Mono 版本）
- .NET 9.0 运行时

### 安装步骤

1. 从 [Releases](https://github.com/llzcx/STS2-PredictEverything/releases) 下载最新版 `predict_everything.dll` 和 `manifest.json`
2. 在游戏目录下创建 `mods/PredictEverything/` 文件夹
3. 目录结构如下：
   ```
   mods/PredictEverything/
   ├── manifest.json
   ├── predict_everything.dll
   └── locale/
       ├── en.json
       └── zh.json
   ```
4. 启动游戏——进入水晶球占卜事件时预测面板自动出现

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F4BB; 使用指南

### 快速上手

进入水晶球事件，两个面板自动出现：

| 面板 | 位置 | 用途 |
|-------|----------|---------|
| **预测网格** | 左侧 | 4 列表格（金卡/蓝卡/白卡/遗物）× 27 行——所有可能的结果 |
| **锁定面板** | 右侧 | 追踪已锁定奖励、药水和剩余金币 |

### 找到你想要的

**推荐方式：**
1. 使用预测面板顶部的**下拉筛选框**
2. 选择你想要的卡牌或遗物
3. 引擎自动规划最优路径——按底部绿色提示操作

**手动方式：**
1. 浏览网格找到目标卡牌/遗物——记住它在哪一行
2. 点击目标列的那一行设置计划
3. 计划摘要告诉你每次点击前需要先花几次金币

### 小技巧

- **悬停**任意卡牌/遗物名查看快速提示
- **右键**任意卡牌/遗物名弹出完整详情窗口
- **悬停**任意隐藏格子预览点击后的结果（自动适配工具大小！）
- 点击 **?** 按钮随时重温教程
- 按住标题栏拖拽面板，点击 **▼** 折叠面板
- 开启迷雾透明（可配置），让隐藏物品隐约可见

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F9EE; 工作原理

```
水晶球事件开始
         │
         ▼
通过 Harmony 补丁读取事件的 RNG 种子和计数器
         │
         ▼
模拟全部 27 个偏移（0–26 次金币点击）的 RNG
  ├── 预测卡牌：模拟 Rng.NextItem + 升级概率
  ├── 预测遗物：反射读取 RelicGrabBag._deques（只读，不修改）
  └── 读取药水：反射读取已预先确定的 PotionModel
         │
         ▼
展示完整的 27 行预测网格
         │
         ▼
玩家通过筛选下拉框选择目标卡牌/遗物
         │
         ▼
最优路径算法执行：
  • 排列列的揭示顺序
  • DFS 搜索，上限 7 金币预算
  • 金币消耗最少优先，其次结束位置最早
  • 未选中列 = 免费跳板
  • 遗物 = 金币耗尽后的最后兜底
         │
         ▼
玩家按绿色计划操作，金币被最优使用
```

### RNG 消耗模型

| 操作 | RNG 槽位消耗 |
|--------|-------------------|
| 金币（小 +10 / 大 +30） | 1 |
| 卡牌列揭示（3 张） | 6 |
| 遗物揭示 | 1 |
| 药水揭示 | 0 |
| 诅咒揭示 | 0 |

**同次点击揭示顺序：** 金币总是在卡牌/遗物之前揭示。药水是预先确定的，不消耗 RNG。

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F5FA;&#xFE0F; 路线图

- [ ] 计划行视觉高亮（不仅边框）
- [ ] 跨面板联动——点击锁定面板的奖励 → 预测网格滚动到对应行
- [ ] 紧凑模式——折叠为只显示计划摘要的窄条
- [ ] 交互式分步教程
- [ ] 导出/分享预测结果截图
- [ ] 悬停弹窗淡入淡出动画

查看 [open issues](https://github.com/llzcx/STS2-PredictEverything/issues) 获取完整的功能建议列表。

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F91D; 参与贡献

开源社区的繁荣离不开您的贡献。任何贡献都**非常欢迎**。

1. Fork 本项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'feat: 添加某个很棒的功能'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 提交 Pull Request

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F4AD; 许可证

Copyright &#xA9; 2025 [Shiang Chen](https://github.com/llzcx).

基于 [MIT][license-url] 许可证发布。

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x1F4E7; 联系方式

Shiang Chen — [@llzcx](https://github.com/llzcx)

项目链接：[https://github.com/llzcx/STS2-PredictEverything](https://github.com/llzcx/STS2-PredictEverything)

<p align="right">(<a href="#readme-top">回到顶部</a>)</p>



## &#x2B50; Star 历史

<div align="center">
  <a href="https://star-history.com/#llzcx/STS2-PredictEverything&Date">
    <img src="https://api.star-history.com/svg?repos=llzcx/STS2-PredictEverything&type=Date" alt="Star History Chart" width="800">
  </a>
</div>



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
