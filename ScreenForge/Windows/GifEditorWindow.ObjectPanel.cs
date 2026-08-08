using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenForge.Editor;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;

namespace ScreenForge.Windows;

public sealed partial class GifEditorWindow
{
    /// <summary>Çizim araçlarında kullanılan temel renkler.</summary>
    private static readonly string[] SwatchColors =
    {
        "#FFE5484D", // kırmızı
        "#FFEA6F12", // turuncu
        "#FFF2D600", // sarı
        "#FF2FBF71", // yeşil
        "#FF2F6FED", // mavi
        "#FFB05CE8", // mor
        "#FFFFFFFF", // beyaz
        "#FF14161C", // siyah
    };

    /// <summary>
    /// Seçili nesnenin özellik panelini kurar.
    /// </summary>
    /// <remarks>
    /// Panel seçime göre yeniden üretilir: her nesne türü yalnızca kendisine
    /// uyan denetimleri gösterir. Ekran alıntısı düzenleyicisiyle aynı stil
    /// hafızası kullanıldığı için seçimler iki taraf arasında paylaşılır.
    /// </remarks>
    private void RebuildObjectPanel()
    {
        ObjectPropertyPanel.Children.Clear();

        var selection = _annotationCanvas?.Selection;
        if (selection is not { Count: > 0 })
        {
            ObjectProperties.Visibility = Visibility.Collapsed;
            return;
        }

        ObjectProperties.Visibility = Visibility.Visible;
        var items = selection.ToList();
        var first = items[0];

        AddStrokeControls(items);

        if (items.Any(i => i is RectItem or EllipseItem))
            AddFillControls(items);

        if (first is TextItem)
            AddTextControls(items);

        if (first is StepItem)
            AddStepControls(items);

        if (first is BlurItem)
            AddBlurControls(items);

        // Blur için opaklık anlamsız — gösterme.
        if (first is not BlurItem)
        {
            AddSeparator();
            AddOpacityControl(items);
        }
        AddOrderControls();
    }

    // ─── Bölümler ─────────────────────────────────────────────────────────────

    private void AddStrokeControls(List<SceneItem> items)
    {
        // Metinde bu alan yazı rengidir; etiketi ona göre değişir.
        bool isText = items[0] is TextItem;
        AddLabel(isText ? "Yazı" : "Renk");

        AddColorSwatches(color =>
        {
            foreach (var item in items)
                item.StrokeColor = color;

            _toolStyle.StrokeColor = InteractiveCanvas.HexFromColor(color);
            CommitObjectChange();
        });

        // Bulanıklık ve görselde çizgi kalınlığı anlamsız.
        if (items[0] is BlurItem or ImageItem or TextItem)
            return;

        AddSeparator();
        AddLabel("Kalınlık");
        AddSlider(1, 24, items[0].StrokeWidth, value =>
        {
            foreach (var item in items)
                item.StrokeWidth = (float)value;

            _toolStyle.StrokeWidth = value;
            CommitObjectChange();
        });
    }

    private void AddFillControls(List<SceneItem> items)
    {
        AddSeparator();
        AddLabel("Dolgu");

        // Şeffaf seçeneği başta: dolguyu kaldırmanın yolu.
        var clear = MakeSwatch(SKColors.Transparent, transparent: true);
        clear.Click += (_, _) =>
        {
            foreach (var item in items)
                item.FillColor = SKColors.Transparent;

            CommitObjectChange();
        };
        ObjectPropertyPanel.Children.Add(clear);

        AddColorSwatches(color =>
        {
            // Dolgu yarı saydam olur ki altındaki içerik seçilebilsin.
            var fill = color.WithAlpha(120);

            foreach (var item in items)
                item.FillColor = fill;

            CommitObjectChange();
        });
    }

    private void AddTextControls(List<SceneItem> items)
    {
        AddSeparator();
        AddLabel("Boyut");

        float current = items.OfType<TextItem>().FirstOrDefault()?.FontSize ?? 28;
        AddSlider(10, 96, current, value =>
        {
            foreach (var text in items.OfType<TextItem>())
            {
                text.FontSize = (float)value;
                text.Measure();
            }

            _toolStyle.FontSize = value;
            CommitObjectChange();
        });

        AddToggle("K", "Kalın", items.OfType<TextItem>().FirstOrDefault()?.Bold ?? true, on =>
        {
            foreach (var text in items.OfType<TextItem>())
            {
                text.Bold = on;
                text.Measure();
            }

            _toolStyle.FontBold = on;
            CommitObjectChange();
        });

        AddToggle("Şerit", "Arka plan şeridi", items.OfType<TextItem>().FirstOrDefault()?.Ribbon ?? true, on =>
        {
            foreach (var text in items.OfType<TextItem>())
                text.Ribbon = on;

            _toolStyle.TextRibbon = on;
            CommitObjectChange();
        });
    }

    private void AddStepControls(List<SceneItem> items)
    {
        AddSeparator();
        AddLabel("Boyut");

        float current = items.OfType<StepItem>().FirstOrDefault()?.Diameter ?? 32;
        AddSlider(16, 96, current, value =>
        {
            foreach (var step in items.OfType<StepItem>())
                step.Diameter = (float)value;

            _toolStyle.StepSize = value;
            CommitObjectChange();
        });
    }

    private void AddBlurControls(List<SceneItem> items)
    {
        AddSeparator();
        AddLabel("Güç");

        float current = items.OfType<BlurItem>().FirstOrDefault()?.Strength ?? 8;
        AddSlider(2, 40, current, value =>
        {
            foreach (var blur in items.OfType<BlurItem>())
                blur.Strength = (float)value;

            _toolStyle.BlurStrength = value;
            CommitObjectChange();
        });

        AddToggle("Piksel", "Pikselleştir", items.OfType<BlurItem>().FirstOrDefault()?.Pixelate ?? false, on =>
        {
            foreach (var blur in items.OfType<BlurItem>())
                blur.Pixelate = on;

            _toolStyle.BlurPixelate = on;
            CommitObjectChange();
        });
    }

    private void AddOpacityControl(List<SceneItem> items)
    {
        AddLabel("Opaklık %");
        int display = (int)Math.Round(Math.Clamp(items[0].Opacity * 100f, 0f, 100f));
        var box = new TextBox
        {
            Text = display.ToString(),
            Width = 48,
            Height = 26,
            MaxLength = 3,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Style = TryFindResource("EditorTextBox") as Style,
        };

        void Apply()
        {
            string raw = box.Text.Trim().TrimEnd('%');
            if (!int.TryParse(raw, out int v))
            {
                box.Text = display.ToString();
                return;
            }
            v = Math.Clamp(v, 0, 100);
            display = v;
            box.Text = v.ToString();
            float op = v / 100f;
            foreach (var item in items)
                item.Opacity = op;
            // Opacity kaydedilmez; yalnızca seçili öğeye uygulanır.
            CommitObjectChange();
        }

        box.PreviewTextInput += (_, e) =>
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        };
        box.LostKeyboardFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Apply();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                box.Text = display.ToString();
                e.Handled = true;
            }
        };

        ObjectPropertyPanel.Children.Add(box);
    }

    private void AddOrderControls()
    {
        AddSeparator();

        var front = MakeChipButton("Öne", "Seçimi en öne getir");
        front.Click += (_, _) => ReorderSelection(toFront: true);
        ObjectPropertyPanel.Children.Add(front);

        var back = MakeChipButton("Arkaya", "Seçimi en arkaya gönder");
        back.Click += (_, _) => ReorderSelection(toFront: false);
        ObjectPropertyPanel.Children.Add(back);
    }

    private void ReorderSelection(bool toFront)
    {
        if (_annotationCanvas is not { } canvas || canvas.Selection.Count == 0)
            return;

        var ordered = SceneClipboard.OrderBySceneZ(_track.Scene, canvas.Selection);

        foreach (var item in ordered)
            _track.Scene.Items.Remove(item);

        if (toFront)
            _track.Scene.Items.AddRange(ordered);
        else
            _track.Scene.Items.InsertRange(0, ordered);

        CommitObjectChange();
        ShowToast(toFront ? "Öne getirildi" : "Arkaya gönderildi");
    }

    /// <summary>Nesne değişikliğini uygular ve önizlemeyi tazeler.</summary>
    private void CommitObjectChange()
    {
        _track.Scene.RaiseChanged();
        _annotationCanvas?.InvalidateVisual();
        UpdatePreview();
    }

    // ─── Denetim fabrikaları ──────────────────────────────────────────────────

    private void AddLabel(string text) => ObjectPropertyPanel.Children.Add(new TextBlock
    {
        Text = text,
        Style = (Style)FindResource("EditorFieldLabel"),
        Margin = new Thickness(0, 0, 6, 0),
    });

    private void AddSeparator() => ObjectPropertyPanel.Children.Add(new Border
    {
        Width = 1,
        Background = (Brush)FindResource("BorderBrush"),
        Margin = new Thickness(8, 3, 8, 3),
    });

    private void AddColorSwatches(Action<SKColor> apply)
    {
        foreach (string hex in SwatchColors)
        {
            var color = InteractiveCanvas.ColorFromHex(hex);
            var swatch = MakeSwatch(color, transparent: false);
            swatch.Click += (_, _) => apply(color);
            ObjectPropertyPanel.Children.Add(swatch);
        }
    }

    private Button MakeSwatch(SKColor color, bool transparent)
    {
        var fill = transparent
            ? (Brush)new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255))
            : new SolidColorBrush(WpfColor.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));

        return new Button
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = transparent ? "Dolgusuz" : null,
            Background = fill,
            BorderThickness = new Thickness(0),
            Template = (ControlTemplate)FindResource("SwatchTemplate"),
            Content = transparent ? "✕" : null,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 9,
        };
    }

    private void AddSlider(double min, double max, double value, Action<double> apply)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 86,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("ToolSlider"),
            Margin = new Thickness(0, 0, 4, 0),
        };

        var readout = new TextBlock
        {
            Text = ((int)slider.Value).ToString(),
            Style = (Style)FindResource("EditorFieldLabel"),
            Foreground = (Brush)FindResource("AccentBrush"),
            MinWidth = 22,
        };

        slider.ValueChanged += (_, e) =>
        {
            readout.Text = ((int)e.NewValue).ToString();
            apply(e.NewValue);
        };

        ObjectPropertyPanel.Children.Add(slider);
        ObjectPropertyPanel.Children.Add(readout);
    }

    private void AddToggle(string text, string tooltip, bool isChecked, Action<bool> apply)
    {
        var toggle = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = text,
            IsChecked = isChecked,
            ToolTip = tooltip,
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            MinWidth = 30,
            Margin = new Thickness(3, 0, 0, 0),
            Style = (Style)FindResource("EditorToolToggle"),
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextBrush"),
        };

        toggle.Checked += (_, _) => apply(true);
        toggle.Unchecked += (_, _) => apply(false);
        ObjectPropertyPanel.Children.Add(toggle);
    }

    private Button MakeChipButton(string text, string tooltip) => new()
    {
        Content = text,
        Style = (Style)FindResource("EditorToolButton"),
        ToolTip = tooltip,
        FontSize = 10.5,
        Margin = new Thickness(2, 0, 2, 0),
    };
}
