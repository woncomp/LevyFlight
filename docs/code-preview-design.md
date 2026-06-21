# 代码预览窗口设计方案

## 目标

在 `LevyFlightWindow` 右侧新增一个占据一半视图的代码预览窗口，居中显示当前 Focused `JumpItem` 所描述的代码文件及其位置，并具备代码高亮能力。

## 现状

- `LevyFlight` 是 Visual Studio 2022 的 VSIX 扩展，使用 WPF 构建 UI。
- `LevyFlightWindow` 当前布局为：
  - 第 0 行：Filter 输入框 + 分类按钮
  - 第 1 行：主内容区，分为三列
    - 左侧（45*）：`ListBox` 显示 `JumpItem` 列表
    - 中间：`GridSplitter`
    - 右侧（55*）：`TextBlock` 显示 `DebugString`（评分详情）
  - 第 2 行：状态栏显示 `SelectedItemFullPath`
- `JumpItem` 已包含以下关键属性：
  - `FullPath`：文件完整路径
  - `LineNumber`：1-based 行号（-1 表示不适用）
  - `CaretColumn`：0-based 列号
  - `Category`：类型（如 `Bookmark`、`TreeSitter`、`SolutionFile` 等）
- 当前没有任何语法高亮或代码预览组件，也没有相关 NuGet 包。

## 方案选型

### 推荐方案：AvalonEdit

使用 `ICSharpCode.AvalonEdit` 作为代码预览控件，原因：

- 专为 WPF 设计，与当前技术栈一致。
- 支持只读模式、行号显示、滚动到指定行。
- 内置多种语言语法高亮（C#、C++、XML、JSON、Python 等）。
- 社区成熟，文档丰富，维护活跃。
- 一个 NuGet 包即可满足全部需求。

### 备选方案

| 方案 | 优点 | 缺点 |
|------|------|------|
| 手动实现 `RichTextBox` + 自定义着色 | 无外部依赖 | 需要自行实现语法高亮，维护成本高 |
| VS SDK 的 `ITextViewHost` | 原生集成 VS 编辑器 | 实现复杂，需处理大量 VS 编辑器宿主细节 |

## 详细设计

### 1. 添加依赖

在 `LevyFlight.csproj` 中添加 NuGet 包：

```xml
<PackageReference Include="ICSharpCode.AvalonEdit" Version="6.3.0.90" />
```

### 2. XAML 布局调整

在 `LevyFlightWindow.xaml` 中：

- 添加 AvalonEdit 命名空间：

  ```xml
  xmlns:ae="http://icsharpcode.net/sharpdevelop/avalonedit"
  ```

- 将右侧 `TextBlock`（Column 2）替换为 `ae:TextEditor`：

  ```xml
  <ae:TextEditor Grid.Column="2"
                 Name="codePreview"
                 IsReadOnly="True"
                 ShowLineNumbers="True"
                 FontFamily="Consolas"
                 Background="{DynamicResource {x:Static shell:VsBrushes.ToolWindowBackgroundKey}}"
                 Foreground="{DynamicResource {x:Static shell:VsBrushes.WindowTextKey}}" />
  ```

- 可选：保留 `DebugString` 显示，可置于预览窗口底部或 Tab 切换。

### 3. 语法高亮

根据 `JumpItem.FullPath` 的扩展名选择高亮定义：

```csharp
var extension = System.IO.Path.GetExtension(jumpItem.FullPath);
string highlightingName = extension switch
{
    ".cs" => "C#",
    ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "C++",
    ".xml" or ".xaml" or ".config" => "XML",
    ".json" => "JSON",
    ".py" => "Python",
    _ => null
};

codePreview.SyntaxHighlighting = highlightingName != null
    ? ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition(highlightingName)
    : null;
```

### 4. 文件加载与滚动

在 `lstFiles_SelectionChanged` 中增加预览逻辑：

```csharp
private async void lstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    var jumpItem = lstFiles.SelectedItem as JumpItem;
    if (jumpItem == null)
    {
        SelectedItemFullPath = null;
        DebugString = null;
        codePreview.Text = string.Empty;
        return;
    }

    SelectedItemFullPath = jumpItem.FullPath;
    DebugString = jumpItem.DebugString;

    await LoadCodePreviewAsync(jumpItem);
}
```

`LoadCodePreviewAsync` 负责异步读取文件并滚动到目标行：

```csharp
private async Task LoadCodePreviewAsync(JumpItem jumpItem)
{
    var path = jumpItem.FullPath;
    if (!File.Exists(path))
    {
        codePreview.Text = $"// File not found: {path}";
        return;
    }

    string text;
    try
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        text = await reader.ReadToEndAsync();
    }
    catch (Exception ex)
    {
        codePreview.Text = $"// Failed to load file: {ex.Message}";
        return;
    }

    codePreview.Text = text;

    if (jumpItem.LineNumber > 0)
    {
        codePreview.ScrollToLine(jumpItem.LineNumber);
        HighlightTargetLine(jumpItem.LineNumber);
    }
}
```

### 5. 目标行高亮

实现一个 `IBackgroundRenderer`，为 `JumpItem.LineNumber` 对应的行绘制半透明背景：

```csharp
public class TargetLineRenderer : IBackgroundRenderer
{
    public int TargetLine { get; set; } = -1;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (TargetLine < 1) return;

        var visualLine = textView.GetVisualLine(TargetLine);
        if (visualLine == null) return;

        var rect = BackgroundGeometryBuilder.GetRectsForSegment(textView, visualLine.FirstDocumentLine).First();
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(60, 255, 255, 0)),
            null,
            new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
    }
}
```

在加载时注册/更新：

```csharp
private void HighlightTargetLine(int lineNumber)
{
    targetLineRenderer.TargetLine = lineNumber;
    codePreview.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
}
```

### 6. 性能与边界处理

- **异步加载**：文件读取使用 `FileStream` + `StreamReader` 异步读取，避免阻塞 UI。
- **大文件保护**：对超过一定大小的文件，可限制只加载前 N 行（例如 10,000 行）。
- **缓存**：对最近预览过的文件路径缓存内容，避免频繁读取磁盘。
- **二进制文件**：检测到二进制内容时显示 `"// Binary file not previewable"`。
- **无路径项**：如果 `JumpItem.FullPath` 为空或无效，清空预览并显示占位文本。
- **并发安全**：如果用户快速切换选择，取消旧的加载任务，只应用最新结果。

### 7. 视觉设计

- 预览窗口与左侧列表宽度比保持 `45:55`（当前布局）。
- 使用 VS 主题刷色：
  - `Background`：`VsBrushes.ToolWindowBackgroundKey`
  - `Foreground`：`VsBrushes.WindowTextKey`
- 目标行背景色使用醒目的半透明黄色或绿色，与当前选中列表项颜色协调。

## 实现顺序建议

1. 添加 `ICSharpCode.AvalonEdit` NuGet 包。
2. 修改 `LevyFlightWindow.xaml`，替换右侧 `TextBlock` 为 `TextEditor`。
3. 在 `LevyFlightWindow.xaml.cs` 中实现 `LoadCodePreviewAsync`。
4. 实现 `TargetLineRenderer` 并集成。
5. 处理语法高亮映射。
6. 添加边界情况处理（文件不存在、大文件、二进制等）。
7. 手动测试不同 `Category`（Bookmark、TreeSitter、SolutionFile）的预览效果。
