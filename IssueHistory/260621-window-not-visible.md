# LevyFlightWindow Quick Open 窗口内容不可见

## 问题描述

打开 Quick Open 窗口（`LevyFlightWindow`）时，窗口以 modal dialog 形式存在（阻塞主窗口，点击产生提示音），但内容完全不可见。Windows Peek 预览显示白色矩形。

## 关键发现

### 1. 窗口加载流程正常

通过 `Debug.WriteLine` 诊断确认，整个加载流程完整执行：

- `Content` 不为 null（是 Grid）
- `Visibility=Visible`，`Opacity=1`
- Grid 有 4 个子元素
- 尺寸正确（ActualWidth/Height 正常）
- `Window_Loaded` 完整执行

### 2. 内容不渲染但窗口存在

- 在 XAML 中添加红色诊断 TextBlock → 仍然不可见
- 窗口在任务栏可见（`ShowInTaskbar="True"`）
- Peek 预览显示白色矩形

### 3. 二分法定位：ImageThemingUtilities 绑定导致渲染失败

通过逐步替换 VS 主题资源为硬编码颜色来定位问题：

| 测试 | 结果 |
|------|------|
| 最简窗口（白底红字 HELLO WORLD） | 正常显示 |
| 完整布局 + 硬编码颜色 | 正常显示 |
| + `shell:VsBrushes`（Window/控件级别） | 正常显示 |
| + `platformUI:EnvironmentColors` | 正常显示 |
| + `ImageThemingUtilities.ImageBackgroundColor` + `BrushToColorConverter` | 不显示 |

**结论**：`ImageThemingUtilities.ImageBackgroundColor` 绑定（使用 `BrushToColorConverter`）导致窗口内容不渲染。

问题代码：

```xml
theming:ImageThemingUtilities.ImageBackgroundColor="{Binding Background, RelativeSource={RelativeSource Self}, Converter={StaticResource BrushToColorConverter}}"
```

### 4. WindowState 问题

`LoadWindowSettings` 中读取保存的 `WindowState`，如果之前保存为 `Minimized`（值 1），配合 `ShowInTaskbar="False"` 会导致窗口完全不可见。

修复代码：

```csharp
var savedState = (WindowState)settings.GetInt32(COLL, "WindowState", 0);
this.WindowState = savedState == WindowState.Minimized ? WindowState.Normal : savedState;
```

注意：删除实验实例缓存（`Local` 目录）不会清除设置存储（`Roaming` 目录），所以 `WindowState` 设置会残留。

## 修复内容

### LevyFlightWindow.xaml

- 移除了 `ListBox` 上的 `theming:ImageThemingUtilities.ImageBackgroundColor` 绑定
- 移除了 `BrushToColorConverter` 资源
- 移除了不再使用的 XAML 命名空间声明：`theming`、`utilities`、`catalog`

### LevyFlightPackage.cs

- 添加了启动日志，用于确认测试使用的是正确构建：

```csharp
Debug.WriteLine("[LevyFlight] Extension package initializing. Build marker: WINDOW-FIX-VERIFY-2026-06-21");
```

## 验证结果

- 构建成功（0 错误）
- 实验实例中打开 Quick Open 窗口，内容正常渲染
- 输出窗口中确认出现启动日志标记：`[LevyFlight] Extension package initializing. Build marker: WINDOW-FIX-VERIFY-2026-06-21`

## 相关文件

- `LevyFlightWindow.xaml`
- `LevyFlightWindow.xaml.cs`
- `LevyFlightPackage.cs`
