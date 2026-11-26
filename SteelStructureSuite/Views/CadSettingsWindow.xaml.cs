using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 图层、颜色、文字与标注样式设置窗口。
    /// </summary>
    public partial class CadSettingsWindow : Window
    {
        private static readonly string[] CommonAciItems =
        {
            "1 - 红",
            "2 - 黄",
            "3 - 绿",
            "4 - 青",
            "5 - 蓝",
            "6 - 洋红",
            "7 - 白/黑",
            "8 - 灰",
            "9 - 浅灰",
            "30 - 橙",
            "140 - 钢蓝"
        };

        private static readonly string[] DimAciItems =
        {
            "256 - 随层 (ByLayer)",
            "1 - 红",
            "2 - 黄",
            "3 - 绿",
            "4 - 青",
            "5 - 蓝",
            "6 - 洋红",
            "7 - 白/黑",
            "8 - 灰"
        };

        private static readonly string[] CommonFonts =
        {
            "仿宋",
            "SimSun",
            "宋体",
            "微软雅黑",
            "Arial",
            "Tahoma"
        };

        public CadSettingsWindow()
        {
            InitializeComponent();
            UserSettings.AlignWindowToAutoCadScreen(this, "CadSettingsWindow");
            Closing += (s, e) => UserSettings.SaveWindowState(this, "CadSettingsWindow");

            InitCombos();
            LoadFromSettings(UserSettings.GetCadDisplaySettings());
        }

        private void InitCombos()
        {
            ComboBox[] layerColorCombos =
            {
                CmbColorModel3D, CmbColorModelWire, CmbColorVisible,
                CmbColorHidden, CmbColorAxis, CmbColorDim, CmbColorFrame
            };
            Border[] swatches =
            {
                SwatchModel3D, SwatchModelWire, SwatchVisible,
                SwatchHidden, SwatchAxis, SwatchDim, SwatchFrame
            };

            for (int i = 0; i < layerColorCombos.Length; i++)
            {
                ComboBox cmb = layerColorCombos[i];
                Border swatch = swatches[i];
                foreach (string item in CommonAciItems)
                    cmb.Items.Add(item);
                cmb.SelectionChanged += (s, e) => UpdateSwatch(cmb, swatch);
                cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, e) => UpdateSwatch(cmb, swatch)));
            }

            foreach (string item in DimAciItems)
            {
                CmbDimLineColor.Items.Add(item);
                CmbDimTextColor.Items.Add(item);
            }

            foreach (string f in CommonFonts)
                CmbTextFont.Items.Add(f);
        }

        private void LoadFromSettings(CadDisplaySettings cfg)
        {
            if (cfg == null) cfg = CadDisplaySettings.CreateDefault();

            TxtLayerModel3D.Text = cfg.LayerModel3D;
            SetAciCombo(CmbColorModel3D, cfg.ColorModel3D);
            TxtLayerModelWire.Text = cfg.LayerModelWire;
            SetAciCombo(CmbColorModelWire, cfg.ColorModelWire);

            TxtLayerVisible.Text = cfg.LayerVisible;
            SetAciCombo(CmbColorVisible, cfg.ColorVisible);
            TxtLayerHidden.Text = cfg.LayerHidden;
            SetAciCombo(CmbColorHidden, cfg.ColorHidden);
            TxtLayerAxis.Text = cfg.LayerAxis;
            SetAciCombo(CmbColorAxis, cfg.ColorAxis);
            TxtLayerDim.Text = cfg.LayerDim;
            SetAciCombo(CmbColorDim, cfg.ColorDim);
            TxtLayerFrame.Text = cfg.LayerFrame;
            SetAciCombo(CmbColorFrame, cfg.ColorFrame);

            TxtTextStyleName.Text = cfg.TextStyleName;
            CmbTextFont.Text = cfg.TextFontName;
            TxtBaseTextHeight.Text = cfg.BaseTextHeightMm.ToString("0.##", CultureInfo.InvariantCulture);
            TxtTextWidthFactor.Text = cfg.TextWidthFactor.ToString("0.##", CultureInfo.InvariantCulture);

            TxtDimStyleName.Text = cfg.DimStyleName;
            TxtDimArrowSize.Text = cfg.DimArrowSizeMm.ToString("0.##", CultureInfo.InvariantCulture);
            TxtDimExtExceed.Text = cfg.DimExtExceedMm.ToString("0.##", CultureInfo.InvariantCulture);
            SetAciCombo(CmbDimLineColor, cfg.DimLineColor);
            SetAciCombo(CmbDimTextColor, cfg.DimTextColor);

            UpdateSwatch(CmbColorModel3D, SwatchModel3D);
            UpdateSwatch(CmbColorModelWire, SwatchModelWire);
            UpdateSwatch(CmbColorVisible, SwatchVisible);
            UpdateSwatch(CmbColorHidden, SwatchHidden);
            UpdateSwatch(CmbColorAxis, SwatchAxis);
            UpdateSwatch(CmbColorDim, SwatchDim);
            UpdateSwatch(CmbColorFrame, SwatchFrame);
        }

        private static void SetAciCombo(ComboBox cmb, short aci)
        {
            foreach (object item in cmb.Items)
            {
                if (item is string s && ParseAci(s, -1) == aci)
                {
                    cmb.SelectedItem = item;
                    return;
                }
            }
            cmb.Text = aci.ToString(CultureInfo.InvariantCulture);
        }

        private static short ParseAci(string text, short fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            int dash = text.IndexOf('-');
            string num = dash >= 0 ? text.Substring(0, dash).Trim() : text.Trim();
            return short.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out short v) ? v : fallback;
        }

        private static double ParseDouble(string text, double fallback)
            => double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static void UpdateSwatch(ComboBox cmb, Border swatch)
        {
            if (swatch == null || cmb == null) return;
            short aci = ParseAci(cmb.Text, 7);
            swatch.Background = new SolidColorBrush(AciToWpfColor(aci));
        }

        private static Color AciToWpfColor(short aci)
        {
            switch (aci)
            {
                case 1: return Color.FromRgb(235, 50, 50);
                case 2: return Color.FromRgb(240, 220, 40);
                case 3: return Color.FromRgb(40, 205, 70);
                case 4: return Color.FromRgb(40, 210, 215);
                case 5: return Color.FromRgb(55, 115, 240);
                case 6: return Color.FromRgb(220, 65, 220);
                case 7: return Color.FromRgb(245, 245, 245);
                case 8: return Color.FromRgb(128, 128, 128);
                case 9: return Color.FromRgb(185, 185, 185);
                case 30: return Color.FromRgb(240, 140, 35);
                case 140: return Color.FromRgb(65, 145, 215);
                default:
                    try
                    {
                        var acCol = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci);
                        var sysCol = acCol.ColorValue;
                        return Color.FromRgb(sysCol.R, sysCol.G, sysCol.B);
                    }
                    catch
                    {
                        return Color.FromRgb(180, 180, 180);
                    }
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            LoadFromSettings(CadDisplaySettings.CreateDefault());
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var cfg = new CadDisplaySettings
            {
                LayerModel3D = TxtLayerModel3D.Text,
                ColorModel3D = ParseAci(CmbColorModel3D.Text, 140),
                LayerModelWire = TxtLayerModelWire.Text,
                ColorModelWire = ParseAci(CmbColorModelWire.Text, 8),

                LayerVisible = TxtLayerVisible.Text,
                ColorVisible = ParseAci(CmbColorVisible.Text, 7),
                LayerHidden = TxtLayerHidden.Text,
                ColorHidden = ParseAci(CmbColorHidden.Text, 8),
                LayerAxis = TxtLayerAxis.Text,
                ColorAxis = ParseAci(CmbColorAxis.Text, 1),
                LayerDim = TxtLayerDim.Text,
                ColorDim = ParseAci(CmbColorDim.Text, 3),
                LayerFrame = TxtLayerFrame.Text,
                ColorFrame = ParseAci(CmbColorFrame.Text, 7),

                TextStyleName = TxtTextStyleName.Text,
                TextFontName = CmbTextFont.Text,
                BaseTextHeightMm = ParseDouble(TxtBaseTextHeight.Text, 3.5),
                TextWidthFactor = ParseDouble(TxtTextWidthFactor.Text, 0.80),

                DimStyleName = TxtDimStyleName.Text,
                DimArrowSizeMm = ParseDouble(TxtDimArrowSize.Text, 2.5),
                DimExtExceedMm = ParseDouble(TxtDimExtExceed.Text, 1.5),
                DimLineColor = ParseAci(CmbDimLineColor.Text, 256),
                DimTextColor = ParseAci(CmbDimTextColor.Text, 256)
            };

            UserSettings.SaveCadDisplaySettings(cfg);
            DialogResult = true;
            Close();
        }
    }
}
