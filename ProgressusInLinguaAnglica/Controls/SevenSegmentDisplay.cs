using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ProgressusInLinguaAnglica.Controls
{
    /// <summary>
    /// 7セグ表示窓に表示する内容のスナップショット。
    /// 実機の表示窓を模し、CHAPTER / STEP / INDEX / ANSWER の各グループと
    /// 上部ラベル・左下の再生/一時停止マークを持つ。
    /// </summary>
    public sealed class SevenSegState
    {
        /// <summary>全消灯（電源オフ等）。</summary>
        public bool Blank { get; set; }

        /// <summary>セル列全体に左から流し込む特殊表示（例: " OPEN"）。設定時はグループ表示より優先。</summary>
        public string? Overlay { get; set; }

        /// <summary>チャプター番号（最大3桁、右詰め・空白埋め）。</summary>
        public string Chapter { get; set; } = "";

        /// <summary>ステップ番号（最大2桁、右詰め）。実質常に "1"。</summary>
        public string Step { get; set; } = "";

        /// <summary>インデックス番号（2桁、ゼロ埋め）。</summary>
        public string Index { get; set; } = "";

        /// <summary>STEP と INDEX の間のハイフン表示。</summary>
        public bool ShowHyphen { get; set; }

        /// <summary>ANSWER 桁（'A' or ' '）。</summary>
        public char Answer { get; set; } = ' ';

        /// <summary>選択された選択肢番号（クイズ選択肢再生中）。' ' で非表示。</summary>
        public char Choice { get; set; } = ' ';

        public bool ChapterLabel { get; set; }
        public bool StepLabel { get; set; }
        public bool AnswerLabel { get; set; }

        public bool PlayIcon { get; set; }
        public bool PauseIcon { get; set; }
    }

    /// <summary>
    /// コード描画の7セグメント表示窓コントロール。State を差し替えて再描画する。
    /// </summary>
    public sealed class SevenSegmentDisplay : Control
    {
        // 8 セグメントのビット（a=1,b=2,c=4,d=8,e=16,f=32,g=64）
        private const int A = 1, B = 2, C = 4, D = 8, E = 16, F = 32, G = 64;

        private static readonly Color BgColor = Color.FromArgb(0x1A, 0x2A, 0x1A);
        private static readonly Color OnColor = Color.FromArgb(0x44, 0xEE, 0x77);
        private static readonly Color OffColor = Color.FromArgb(0x24, 0x36, 0x24);
        private static readonly Color LabelColor = Color.FromArgb(0x8C, 0xC8, 0x9C);

        private SevenSegState _state = new();

        public SevenSegmentDisplay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            BackColor = BgColor;
        }

        /// <summary>表示内容。設定すると再描画される。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public SevenSegState State
        {
            get => _state;
            set
            {
                _state = value ?? new SevenSegState();
                Invalidate();
            }
        }

        /// <summary>
        /// 文字から点灯セグメントのビットマスクを得る。未対応文字は全消灯。
        /// </summary>
        private static int MaskOf(char c) => c switch
        {
            '0' => A | B | C | D | E | F,
            '1' => B | C,
            '2' => A | B | G | E | D,
            '3' => A | B | G | C | D,
            '4' => F | G | B | C,
            '5' => A | F | G | C | D,
            '6' => A | F | G | E | C | D,
            '7' => A | B | C,
            '8' => A | B | C | D | E | F | G,
            '9' => A | B | C | D | F | G,
            '-' => G,
            'A' => A | B | C | E | F | G,
            'O' => A | B | C | D | E | F,
            'P' => A | B | E | F | G,
            'E' => A | D | E | F | G,
            'N' or 'n' => C | E | G,
            _ => 0,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BgColor);

            if (_state.Blank)
                return;

            // 領域配分: 上部にラベル、左端に ▶/‖ マーク、その右に数字群。
            float w = Width;
            float h = Height;
            float labelH = h * 0.20f;
            float digTop = labelH + h * 0.02f;
            float digBottom = h - h * 0.06f;
            float digH = Math.Max(10f, digBottom - digTop);

            float sideMargin = w * 0.03f;
            float markW = digH * 0.55f;          // 左の ▶/‖ マーク領域
            float areaX = sideMargin + markW;    // 数字領域の開始 X
            float areaW = w - areaX - sideMargin;

            // セルの幅単位の割り当て（セル=1, グループ内=0.18, グループ間=1.1, ハイフン枠=1.0）
            const float cell = 1f, inGap = 0.18f, grpGap = 1.1f, hyphGap = 1.0f;
            float totalUnits = 9 * cell + 6 * inGap + grpGap + hyphGap + grpGap;
            float unit = areaW / totalUnits;
            float cellW = unit * cell;

            // 各セルの X 開始位置を組み立てる
            var cellX = new float[9];
            float hyphenX;
            {
                float x = areaX;
                // chapter: 0,1,2
                for (int i = 0; i < 3; i++) { cellX[i] = x; x += cellW + (i < 2 ? inGap * unit : 0); }
                x += grpGap * unit;
                // step: 3,4
                cellX[3] = x; x += cellW + inGap * unit;
                cellX[4] = x; x += cellW;
                // ハイフン枠
                hyphenX = x; x += hyphGap * unit;
                // index: 5,6
                cellX[5] = x; x += cellW + inGap * unit;
                cellX[6] = x; x += cellW;
                x += grpGap * unit;
                // answer: 7,8
                cellX[7] = x; x += cellW + inGap * unit;
                cellX[8] = x;
            }

            // 表示文字をセル列に割り当てる
            char[] cells = BuildCells();

            for (int i = 0; i < 9; i++)
                DrawDigit(g, cellX[i], digTop, cellW, digH, MaskOf(cells[i]));

            // ハイフン（STEP と INDEX の間）
            if (_state.Overlay is null && _state.ShowHyphen)
                DrawHyphen(g, hyphenX, digTop, hyphGap * unit, digH);

            DrawLabels(g, cellX, cellW, labelH);
            DrawIcons(g, sideMargin, digTop, markW, digH);
        }

        /// <summary>State から 9 セル分の表示文字を組み立てる。</summary>
        private char[] BuildCells()
        {
            var cells = new char[9];
            for (int i = 0; i < 9; i++) cells[i] = ' ';

            if (!string.IsNullOrEmpty(_state.Overlay))
            {
                string s = _state.Overlay!;
                for (int i = 0; i < 9 && i < s.Length; i++)
                    cells[i] = s[i];
                return cells;
            }

            PlaceRight(cells, 0, 3, _state.Chapter); // chapter → セル0-2 右詰め
            PlaceRight(cells, 3, 2, _state.Step);     // step    → セル3-4 右詰め
            PlaceRight(cells, 5, 2, _state.Index);    // index   → セル5-6
            cells[7] = _state.Answer;
            cells[8] = _state.Choice;
            return cells;
        }

        private static void PlaceRight(char[] cells, int start, int len, string value)
        {
            value ??= "";
            if (value.Length > len) value = value.Substring(value.Length - len);
            int pad = len - value.Length;
            for (int i = 0; i < value.Length; i++)
                cells[start + pad + i] = value[i];
        }

        private void DrawDigit(Graphics g, float x, float y, float w, float h, int mask)
        {
            float t = w * 0.20f;             // セグメント太さ
            float pad = t * 0.6f;            // 端の余白
            float xL = x + pad, xR = x + w - pad;
            float yT = y + pad, yB = y + h - pad, yM = y + h / 2f;

            using var onBrush = new SolidBrush(OnColor);
            using var offBrush = new SolidBrush(OffColor);

            DrawSeg(g, HBar(xL, xR, yT, t), mask, A, onBrush, offBrush);
            DrawSeg(g, VBar(xR, yT, yM, t), mask, B, onBrush, offBrush);
            DrawSeg(g, VBar(xR, yM, yB, t), mask, C, onBrush, offBrush);
            DrawSeg(g, HBar(xL, xR, yB, t), mask, D, onBrush, offBrush);
            DrawSeg(g, VBar(xL, yM, yB, t), mask, E, onBrush, offBrush);
            DrawSeg(g, VBar(xL, yT, yM, t), mask, F, onBrush, offBrush);
            DrawSeg(g, HBar(xL, xR, yM, t), mask, G, onBrush, offBrush);
        }

        private static void DrawSeg(Graphics g, PointF[] poly, int mask, int bit, Brush on, Brush off)
        {
            g.FillPolygon((mask & bit) != 0 ? on : off, poly);
        }

        private static PointF[] HBar(float xL, float xR, float cy, float t)
        {
            float h = t / 2f;
            return new[]
            {
                new PointF(xL, cy),
                new PointF(xL + h, cy - h),
                new PointF(xR - h, cy - h),
                new PointF(xR, cy),
                new PointF(xR - h, cy + h),
                new PointF(xL + h, cy + h),
            };
        }

        private static PointF[] VBar(float cx, float yT, float yB, float t)
        {
            float h = t / 2f;
            return new[]
            {
                new PointF(cx, yT),
                new PointF(cx + h, yT + h),
                new PointF(cx + h, yB - h),
                new PointF(cx, yB),
                new PointF(cx - h, yB - h),
                new PointF(cx - h, yT + h),
            };
        }

        private void DrawHyphen(Graphics g, float x, float y, float w, float h)
        {
            // 長め・細めの中央ダッシュ。
            float cy = y + h / 2f;
            float xL = x + w * 0.08f, xR = x + w * 0.92f;
            float thickness = h * 0.10f;
            using var brush = new SolidBrush(OnColor);
            g.FillPolygon(brush, HBar(xL, xR, cy, thickness));
        }

        private void DrawLabels(Graphics g, float[] cellX, float cellW, float labelH)
        {
            using var font = new Font("Segoe UI", Math.Max(7f, labelH * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(LabelColor);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            if (_state.ChapterLabel)
            {
                var r = new RectangleF(cellX[0], 0, (cellX[2] + cellW) - cellX[0], labelH);
                g.DrawString("CHAPTER", font, brush, r, fmt);
            }
            if (_state.StepLabel)
            {
                var r = new RectangleF(cellX[3], 0, (cellX[4] + cellW) - cellX[3], labelH);
                g.DrawString("STEP", font, brush, r, fmt);
            }
            if (_state.AnswerLabel)
            {
                var r = new RectangleF(cellX[7], 0, (cellX[8] + cellW) - cellX[7], labelH);
                g.DrawString("ANSWER", font, brush, r, fmt);
            }
        }

        private void DrawIcons(Graphics g, float x, float y, float zoneW, float zoneH)
        {
            using var brush = new SolidBrush(OnColor);
            float s = Math.Min(zoneW, zoneH * 0.34f);

            // 再生マーク（▶）— 上段
            if (_state.PlayIcon)
            {
                float py = y + zoneH * 0.10f;
                var tri = new[]
                {
                    new PointF(x, py),
                    new PointF(x + s, py + s / 2f),
                    new PointF(x, py + s),
                };
                g.FillPolygon(brush, tri);
            }

            // 一時停止マーク（‖）— 下段（再生マークの下）
            if (_state.PauseIcon)
            {
                float py = y + zoneH * 0.56f;
                float bw = s * 0.32f;
                g.FillRectangle(brush, x, py, bw, s);
                g.FillRectangle(brush, x + bw * 1.9f, py, bw, s);
            }
        }
    }
}
