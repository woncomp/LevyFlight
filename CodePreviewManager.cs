using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml;

namespace LevyFlight
{
    internal static class CodePreviewManager
    {
        private const string DarkSuffix = "-Dark";
        private static readonly Dictionary<string, string> ExtensionToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "C#",
            [".cpp"] = "C++",
            [".cxx"] = "C++",
            [".cc"] = "C++",
            [".c"] = "C++",
            [".h"] = "C++",
            [".hpp"] = "C++",
            [".hxx"] = "C++",
            [".hh"] = "C++",
            [".inl"] = "C++",
            [".ipp"] = "C++",
            [".tpp"] = "C++",
            [".xml"] = "XML",
            [".xaml"] = "XML",
            [".config"] = "XML",
            [".json"] = "JSON",
            [".py"] = "Python",
            [".js"] = "JavaScript",
            [".md"] = "MarkDown",
        };

        static CodePreviewManager()
        {
            BuildDarkHighlightingDefinitions();
            VSColorTheme.ThemeChanged += OnVsColorThemeChanged;
        }

        public static bool IsDarkTheme
        {
            get
            {
                try
                {
                    var color = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                    return (color.R + color.G + color.B) / 3.0 < 128;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static event EventHandler ThemeChanged;

        public static void ApplyThemeToEditor(TextEditor editor)
        {
            if (editor == null)
                return;

            bool dark = IsDarkTheme;
            object backgroundKey = dark ? VsBrushes.ToolWindowBackgroundKey : VsBrushes.WindowKey;
            editor.Background = VsBrush(backgroundKey);
            editor.Foreground = VsBrush(VsBrushes.WindowTextKey);
            editor.LineNumbersForeground = VsBrush(VsBrushes.GrayTextKey);
        }

        public static IHighlightingDefinition GetHighlightingDefinition(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string ext = Path.GetExtension(filePath);
            if (!ExtensionToName.TryGetValue(ext, out string name))
                return null;

            if (IsDarkTheme)
            {
                string darkName = name + DarkSuffix;
                var darkDef = HighlightingManager.Instance.GetDefinition(darkName);
                if (darkDef != null)
                    return darkDef;
            }

            return HighlightingManager.Instance.GetDefinition(name);
        }

        private static void OnVsColorThemeChanged(ThemeChangedEventArgs e)
        {
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static Brush VsBrush(object key)
        {
            var brush = Application.Current?.TryFindResource(key) as Brush;
            return brush ?? Brushes.Transparent;
        }

        private static void BuildDarkHighlightingDefinitions()
        {
            var assembly = typeof(HighlightingManager).Assembly;
            var resourceNameMap = new Dictionary<string, string>
            {
                ["C#"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.CSharp-Mode.xshd",
                ["C++"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.CPP-Mode.xshd",
                ["XML"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.XML-Mode.xshd",
                ["JSON"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.Json.xshd",
                ["Python"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.Python-Mode.xshd",
                ["JavaScript"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.JavaScript-Mode.xshd",
                ["MarkDown"] = "ICSharpCode.AvalonEdit.Highlighting.Resources.MarkDown-Mode.xshd",
            };

            foreach (var pair in resourceNameMap)
            {
                try
                {
                    using (var stream = assembly.GetManifestResourceStream(pair.Value))
                    {
                        if (stream == null)
                            continue;

                        using (var reader = XmlReader.Create(stream))
                        {
                            var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                            TransformDefinitionToDark(definition);
                            string darkName = pair.Key + DarkSuffix;
                            HighlightingManager.Instance.RegisterHighlighting(darkName, Array.Empty<string>(), definition);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ExtensionErrorHandler.Log("Build dark highlighting for " + pair.Key, ex);
                }
            }
        }

        private static void TransformDefinitionToDark(IHighlightingDefinition definition)
        {
            var visited = new HashSet<HighlightingRuleSet>();
            foreach (var color in definition.NamedHighlightingColors)
            {
                TransformColor(color);
            }

            if (definition.MainRuleSet != null)
            {
                TransformRuleSet(definition.MainRuleSet, visited);
            }
        }

        private static void TransformRuleSet(HighlightingRuleSet ruleSet, HashSet<HighlightingRuleSet> visited)
        {
            if (ruleSet == null || !visited.Add(ruleSet))
                return;

            foreach (var span in ruleSet.Spans)
            {
                TransformColor(span.StartColor);
                TransformColor(span.SpanColor);
                TransformColor(span.EndColor);
                TransformRuleSet(span.RuleSet, visited);
            }

            foreach (var rule in ruleSet.Rules)
            {
                TransformColor(rule.Color);
            }
        }

        private static void TransformColor(HighlightingColor color)
        {
            if (color == null)
                return;

            if (color.Foreground is SimpleHighlightingBrush simpleForeground)
            {
                var fg = simpleForeground.GetColor(null);
                if (fg.HasValue)
                    color.Foreground = new SimpleHighlightingBrush(EnsureDarkVisibility(fg.Value));
            }

            if (color.Background is SimpleHighlightingBrush simpleBackground)
            {
                var bg = simpleBackground.GetColor(null);
                if (bg.HasValue)
                    color.Background = new SimpleHighlightingBrush(EnsureDarkVisibility(bg.Value));
            }
        }

        private static Color EnsureDarkVisibility(Color color)
        {
            var hsl = RgbToHsl(color);
            if (hsl.L < 0.55)
            {
                hsl.L = 0.55 + (1.0 - hsl.L) * 0.15;
                hsl.S = Math.Min(1.0, hsl.S * 1.15);
            }
            return HslToRgb(hsl);
        }

        private static (double H, double S, double L) RgbToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, l = (max + min) / 2.0;

            if (Math.Abs(max - min) > 0.00001)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (Math.Abs(max - r) < 0.00001)
                    h = (g - b) / d + (g < b ? 6.0 : 0.0);
                else if (Math.Abs(max - g) < 0.00001)
                    h = (b - r) / d + 2.0;
                else
                    h = (r - g) / d + 4.0;

                h /= 6.0;
            }

            return (h, s, l);
        }

        private static Color HslToRgb((double H, double S, double L) hsl)
        {
            double r, g, b;

            if (Math.Abs(hsl.S) < 0.00001)
            {
                r = g = b = hsl.L;
            }
            else
            {
                double q = hsl.L < 0.5 ? hsl.L * (1.0 + hsl.S) : hsl.L + hsl.S - hsl.L * hsl.S;
                double p = 2.0 * hsl.L - q;
                r = HueToRgb(p, q, hsl.H + 1.0 / 3.0);
                g = HueToRgb(p, q, hsl.H);
                b = HueToRgb(p, q, hsl.H - 1.0 / 3.0);
            }

            return Color.FromArgb(
                255,
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }

    internal sealed class TargetLineRenderer : IBackgroundRenderer
    {
        private static Brush darkBrush;
        private static Brush lightBrush;

        public int TargetLine { get; set; } = -1;

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (TargetLine < 1)
                return;

            var visualLine = textView.GetVisualLine(TargetLine);
            if (visualLine == null)
                return;

            var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, visualLine.FirstDocumentLine);
            if (!rects.Any())
                return;

            var rect = rects.First();
            drawingContext.DrawRectangle(GetTargetLineBrush(), null, new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
        }

        private static Brush GetTargetLineBrush()
        {
            try
            {
                var color = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
                bool dark = (color.R + color.G + color.B) / 3.0 < 128;
                if (dark)
                {
                    if (darkBrush == null)
                        darkBrush = new SolidColorBrush(Color.FromArgb(80, 100, 200, 255));
                    return darkBrush;
                }
                else
                {
                    if (lightBrush == null)
                        lightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 200, 50));
                    return lightBrush;
                }
            }
            catch
            {
                if (lightBrush == null)
                    lightBrush = new SolidColorBrush(Color.FromArgb(60, 255, 200, 50));
                return lightBrush;
            }
        }
    }
}
