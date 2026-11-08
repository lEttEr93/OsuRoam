# 谱面预览候选更新（基于 1.5.5）

当前状态（2026-11-08）：**实验版本，透明预览的轻微抽动尚未解决，不能作为已修复版本发布。** 用户对照确认：不透明 HWND flip 路径明显更顺；透明保留内容、单次匀速动画、透明 HWND blt、小不透明辅助窗口以及独立 note 视觉均仍有类似抽动。

`Tools/check-preview.ps1 -Notes` 可运行独立 note 原型：不创建视口大小的透明 note 缓冲，每个矩形使用 1px 不透明表面缩放，固定裁剪层提供顶部淡入。该原型编译和 90 秒图形运行检查通过，用户报告未改善，当前不作为默认路径。

PresentMon 2.6.0 已从 GameTechDev/PresentMon 官方发布页下载至 `D:\AI\tools\presentmon`，Intel Corporation 数字签名有效。普通权限启动 ETW 会返回 access denied。管理员 PowerShell 执行下列脚本，可自动采集三种路径并解析 CSV；脚本不自行提权、不修改用户组：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
& D:\AI\repos\osu-roam\Tools\measure-preview.ps1
```

输出默认在 `D:\AI\runs\osu-roam-preview\presentmon`，包含原始 CSV、测试报告和 summary.json。脚本语法已检查，完整管理员采样尚未运行。保留模式没有逐帧 Present；DWM 输出节拍也不能单独证明每一帧预览内容均上屏，须按进程、交换链及显示器区分分析。

本次范围仅为谱面预览，不包含其他功能更新，也不修改正式版本号。

默认预览改用 DirectComposition 保留内容：三个预绘制内容块循环补充，共享由合成器执行的位移动画。应用线程约每 50ms 同步音频时间、补块并处理窗口变化，不再逐帧绘制和等待透明交换链的帧延迟对象。

每次校正都从上一段动画在当前 QPC 时刻的位置和速度接续，在 100ms 内渐变到修正速度。维护线程唤醒间隔不均匀、采样时间抖动不会直接重置画面位置；只有切歌和明确回跳重新定位。测试覆盖不规则提交和交替 ±12ms 采样误差，检查位置与速度的连续性。

顶部渐隐使用固定在视口的 2px 裁剪带和子树透明度，背景保持透明；判定线独立固定。小幅时间误差通过速度修正，大幅跳转重新定位；200ms 无有效推进的音频采样后停止预测。合成器动画也有同样的有限截止点。

`IDCompositionAnimation.AddCubic` 的参数是开始时间和多项式系数，线性运动使用 `AddCubic(0, start, velocity, 0, 0)`。此前原型将系数误作结束值等参数，已纠正。

检查入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/check-preview.ps1
```

显式图形检查覆盖 4K/7K/10K、补块、尺寸、流速、透明度、回跳、断流和关闭重开，并输出隔离测试背景上的截图及报告。常规测试包含时钟在不同维护频率下的一致性、重复采样暂停和延迟唤醒截止点。

`--preview-legacy` 可用于旧交换链路径对照；初始化或运行失败会记录日志并自动回退。诊断参考帧是栅格化参考，不代表 DWM 实际上屏截图。

应用维护频率不等于显示帧率。此实现消除了应用逐帧提交的节拍依赖，但仍受 DWM、显示器和系统调度约束；未通过 PresentMon/ETW 验证实际稳定 240Hz，也没有作此保证。新路径没有逐帧 Present，分析旧路径时才可直接对照 Present 的显示/丢弃数据。
