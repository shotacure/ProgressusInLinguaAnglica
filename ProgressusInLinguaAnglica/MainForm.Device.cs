using System;
using System.Drawing;
using System.Windows.Forms;
using ProgressusInLinguaAnglica.Audio;
using ProgressusInLinguaAnglica.Controls;
using ProgressusInLinguaAnglica.Model;

namespace ProgressusInLinguaAnglica
{
    /// <summary>
    /// 実機互換の操作パネル UI と状態機械。
    /// 表示窓（7セグ）・各ボタンの挙動・連続再生/リピート/プリロールを管理する。
    /// </summary>
    public partial class MainForm
    {
        // === デバイス全体の状態 ===
        private enum DeviceMode { NoDisc, Loading, Idle, PreRoll, Playing, Paused, Tail }

        private SevenSegmentDisplay _display = null!;
        private PlaybackEngine _engine = null!;
        private MicRecorder _recorder = null!;

        private DeviceMode _mode = DeviceMode.NoDisc;
        private bool _powered = true;
        private bool _discLoaded;
        private bool _loading;

        // 現在対象の再生ユニット（_segmentItems のインデックス）
        private int _curUnit = -1;

        // クイズ選択肢の選択番号（12桁目）。' ' で非表示
        private char _choiceChar = ' ';

        // === SELECT（チャプター/ステップ/インデックス指定）モード ===
        private enum SelField { Chapter, Step, Index }
        private bool _selecting;
        private SelField _selField;
        private string _selChapter = "";
        private string _selStep = "";
        private string _selIndex = "";
        // 各フィールドで「まだ数字を打っていない」状態か。最初の1桁は置き換え、以降は追記。
        private bool _selFresh;

        // === リピート ===
        private bool _repeating;

        // === プリロール（0.5秒ポーズ→再生）と多重押し検知 ===
        private enum PreRollKind { None, ChapterPrev, IndexBack }
        private PreRollKind _preRoll = PreRollKind.None;
        private bool _pendingRepeat;

        // === タイマー ===
        private System.Windows.Forms.Timer _tmrTrans = null!; // 0.5秒ポーズ・ループ間
        private System.Windows.Forms.Timer _tmrBlink = null!; // 0.3秒点滅
        private bool _blinkOn;

        // === ボタン参照 ===
        private Button _btnOpen = null!, _btnSelect = null!, _btnClear = null!;
        private Button _btnRec = null!, _btnPb = null!;
        private Button _btnStepPrev = null!, _btnStepNext = null!, _btnPause = null!;
        private Button _btnBack = null!, _btnStop = null!, _btnGo = null!, _btnRepeat = null!;
        private readonly Button[] _numButtons = new Button[10];
        private TrackBar _trkVolume = null!;
        private Button _btnToggleList = null!;

        // チャプターリストの折りたたみ
        private Panel _listPanel = null!;
        private bool _listExpanded;
        private const int CollapsedHeight = 526; // デバイスパネル + ステータスバー
        private const int ExpandedHeight = 806;  // + チャプターリスト

        private const int BlinkMs = 300;
        private const int PreRollMs = 500;

        //=====================================================================
        //  初期化・パネル構築
        //=====================================================================

        /// <summary>
        /// コンストラクタから呼ぶ。再生エンジン・タイマー・実機パネルを構築し、
        /// 既存のリスト/パス UI を下部へ再配置する。
        /// </summary>
        private void InitDevicePanel()
        {
            _engine = new PlaybackEngine(PlaybackSampleRate);
            _engine.PlaybackEnded += (_, __) => OnEngineEnded();
            _recorder = new MicRecorder();

            // OPEN ボタンがあるためメニューバーは不要。
            if (menuStrip1 is not null)
            {
                Controls.Remove(menuStrip1);
                MainMenuStrip = null;
            }

            _tmrTrans = new System.Windows.Forms.Timer { Interval = PreRollMs };
            _tmrTrans.Tick += OnTransitionTick;

            _tmrBlink = new System.Windows.Forms.Timer { Interval = BlinkMs };
            _tmrBlink.Tick += (_, __) => { _blinkOn = !_blinkOn; RefreshDisplay(); };
            _tmrBlink.Start();

            BuildDeviceLayout();
            RefreshDisplay();
        }

        private void BuildDeviceLayout()
        {
            // クライアント領域をデバイス（上）とリスト（下）の2段に分ける
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 500));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var device = BuildDevicePanel();
            _listPanel = BuildListPanel();

            root.Controls.Add(device, 0, 0);
            root.Controls.Add(_listPanel, 0, 1);

            Controls.Add(root);
            root.SendToBack();          // 最背面へ（残り領域にフィット）
            statusStrip1.BringToFront(); // ステータスバーが先に下端領域を確保するよう最前面へ

            // 既定はリストをたたんでおく（トグルボタンで下方向に展開）。
            _listExpanded = false;
            _listPanel.Visible = false;
            ClientSize = new Size(Math.Max(ClientSize.Width, 1000), CollapsedHeight);
        }

        private Panel BuildDevicePanel()
        {
            // 実機互換の操作パネル配置を絶対座標で再現する。
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0xCB, 0xCB, 0xCB),
            };

            // --- 表示窓（上部・横長）。左に ▶/‖、上辺に CHAPTER/STEP/ANSWER ラベル ---
            _display = new SevenSegmentDisplay
            {
                Location = new Point(16, 14),
                Size = new Size(700, 150),
            };

            // --- OPEN は右側 ---
            _btnOpen = MakeAt("OPEN", 868, 124, 96, 40, (_, __) => OnOpen());

            // --- 機能ボタン: SELECT / CLEAR（POWER は実機ではインジケータなので置かない） ---
            _btnSelect = MakeAt("SELECT", 16, 180, 92, 40, (_, __) => OnSelect());
            _btnClear = MakeAt("CLEAR", 112, 180, 92, 40, (_, __) => OnClear());

            // --- 数字ボタン 2行×5列（1〜5 / 6〜0） ---
            int[] order = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 };
            for (int i = 0; i < order.Length; i++)
            {
                int digit = order[i];
                int col = i % 5, rowNo = i / 5;
                var b = MakeAt(digit.ToString(), 16 + col * 70, 234 + rowNo * 52, 60, 44, null);
                b.Click += (_, __) => OnNumber(digit);
                _numButtons[digit] = b;
                panel.Controls.Add(b);
            }

            // --- MIC（インジケータ：ボタンではない）/ REC（押している間録音）/ PB（録音再生） ---
            var micInd = new Label
            {
                Text = "● MIC",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x80, 0x80, 0x80),
                Location = new Point(16, 352),
                Font = new Font("Segoe UI", 8f),
            };
            _btnRec = MakeAt("REC", 16, 372, 56, 42, null);
            _btnPb = MakeAt("PB", 78, 372, 56, 42, (_, __) => _recorder.PlayRecording());
            // REC は「押している間」録音
            _btnRec.MouseDown += (_, __) => _recorder.StartRecording();
            _btnRec.MouseUp += (_, __) => _recorder.StopRecording();

            // --- トランスポート（図の配置）---
            //   STEP(上): ◀◀ ▶▶ ／ その下: ▶‖（再生）■（停止）／ 右: ◀（バック） GO（大）／ さらに右: REPEAT
            var stepLbl = new Label
            {
                Text = "STEP",
                AutoSize = true,
                Location = new Point(238, 332),
                Font = new Font("Segoe UI", 8f),
            };
            _btnStepPrev = MakeAt("◀◀", 210, 350, 60, 42, (_, __) => OnStepPrev());
            _btnStepNext = MakeAt("▶▶", 274, 350, 60, 42, (_, __) => OnStepNext());
            _btnPause = MakeAt("▶‖", 210, 396, 60, 42, (_, __) => OnPause()); // ◀◀ の下
            _btnStop = MakeAt("■", 274, 396, 60, 42, (_, __) => OnStop());    // ▶▶ の下
            _btnBack = MakeAt("◀", 346, 372, 56, 50, (_, __) => OnBack());     // 右・中段
            _btnGo = MakeAt("GO ▶▶", 408, 350, 122, 88, (_, __) => OnGo());    // 最も大きい
            _btnGo.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            _btnRepeat = MakeAt("REPEAT", 542, 372, 100, 50, (_, __) => OnRepeat());

            // --- ボリューム ---
            var volLbl = new Label { Text = "VOLUME", AutoSize = true, Location = new Point(212, 452) };
            _trkVolume = new TrackBar
            {
                Location = new Point(292, 446),
                Size = new Size(300, 45),
                Minimum = 0,
                Maximum = 100,
                Value = 80,
                TickFrequency = 10,
            };
            _trkVolume.ValueChanged += (_, __) => _engine.Volume = _trkVolume.Value / 100f;

            // --- チャプターリストの開閉トグル ---
            _btnToggleList = MakeAt("チャプター一覧 ▼", 662, 450, 180, 34, (_, __) => OnToggleList());

            panel.Controls.Add(_display);
            panel.Controls.Add(_btnToggleList);
            panel.Controls.Add(_btnOpen);
            panel.Controls.Add(_btnSelect);
            panel.Controls.Add(_btnClear);
            panel.Controls.Add(micInd);
            panel.Controls.Add(_btnRec);
            panel.Controls.Add(_btnPb);
            panel.Controls.Add(stepLbl);
            panel.Controls.Add(_btnStepPrev);
            panel.Controls.Add(_btnStepNext);
            panel.Controls.Add(_btnPause);
            panel.Controls.Add(_btnStop);
            panel.Controls.Add(_btnBack);
            panel.Controls.Add(_btnGo);
            panel.Controls.Add(_btnRepeat);
            panel.Controls.Add(volLbl);
            panel.Controls.Add(_trkVolume);
            return panel;
        }

        private static Button MakeAt(string text, int x, int y, int w, int h, EventHandler? onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                UseVisualStyleBackColor = true,
                FlatStyle = FlatStyle.System,
            };
            if (onClick is not null) b.Click += onClick;
            return b;
        }

        /// <summary>チャプターリストを下段パネルへ再配置する。パス表示・参照ボタンは OPEN があるため廃止。</summary>
        private Panel BuildListPanel()
        {
            // 上に見出し用の余白(30)、下にステータスバーと干渉しないための余白(14)。
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 30, 6, 14) };

            // パス表示・参照・再生ボタンはフォームから取り除く（OPEN／ダブルクリックで代替）。
            RemoveFromForm(lblRoot);
            RemoveFromForm(txtRootPath);
            RemoveFromForm(btnBrowseFolder);
            RemoveFromForm(btnPlaySelected);

            // リストは Dock=Fill（アンカーの伸縮ずれを避ける）。Padding の内側に収まる。
            lstChapters.Dock = DockStyle.Fill;

            lblChapters.Location = new Point(6, 6);
            lblChapters.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            panel.Controls.Add(lstChapters);  // Fill を先に追加
            panel.Controls.Add(lblChapters);  // 見出しは上の余白に重ねる
            return panel;
        }

        private void RemoveFromForm(Control? c)
        {
            if (c?.Parent is not null) c.Parent.Controls.Remove(c);
        }

        /// <summary>チャプターリストの開閉。閉時はウィンドウを縮め、開時は下方向へ広げる。</summary>
        private void OnToggleList()
        {
            _listExpanded = !_listExpanded;
            _listPanel.Visible = _listExpanded;
            _btnToggleList.Text = _listExpanded ? "チャプター一覧 ▲" : "チャプター一覧 ▼";
            ClientSize = new Size(ClientSize.Width, _listExpanded ? ExpandedHeight : CollapsedHeight);
            if (_listExpanded) SyncListSelection();
        }

        /// <summary>再生中（対象）のユニットにリスト選択を追尾させ、可視範囲に入れる。</summary>
        private void SyncListSelection()
        {
            if (_curUnit < 0 || _curUnit >= lstChapters.Items.Count) return;

            if (lstChapters.SelectedIndex != _curUnit)
                lstChapters.SelectedIndex = _curUnit;

            // 可視範囲外なら先頭位置を調整して見えるようにする。
            int itemH = Math.Max(1, lstChapters.ItemHeight);
            int visible = Math.Max(1, lstChapters.ClientSize.Height / itemH);
            if (_curUnit < lstChapters.TopIndex || _curUnit >= lstChapters.TopIndex + visible)
                lstChapters.TopIndex = _curUnit;
        }

        //=====================================================================
        //  読み込みフック（MainForm.cs の LoadRoot 群から呼ばれる）
        //=====================================================================

        /// <summary>SOUND.RTF を開いて TBL 読み込みを開始した時点。</summary>
        private void DeviceOnLoadingStarted()
        {
            _engine.Stop();
            CancelTransitions();
            _selecting = false;
            _repeating = false;
            _curUnit = -1;
            _discLoaded = false;
            _loading = true;
            _mode = DeviceMode.Loading;
            RefreshDisplay();
        }

        /// <summary>TBL 読み込みが完了した時点。</summary>
        private void DeviceOnLoadingFinished()
        {
            _loading = false;
            if (_segmentItems.Count > 0)
            {
                _discLoaded = true;
                _curUnit = 0;
                _mode = DeviceMode.Idle;
            }
            else
            {
                _discLoaded = false;
                _mode = DeviceMode.NoDisc;
            }
            RefreshDisplay();
        }

        //=====================================================================
        //  ボタン挙動
        //=====================================================================

        private void OnOpen() => BrowseAndLoadFolder();

        private void OnSelect()
        {
            if (!_powered || !_discLoaded) return;

            if (!_selecting)
            {
                _selecting = true;
                _selField = SelField.Chapter;
                // 現在位置をシード（表示用）。打ち始めの1桁目で置き換わるようにする。
                if (_curUnit >= 0)
                {
                    var it = _segmentItems[_curUnit];
                    _selChapter = it.ChapterNo.ToString();
                    _selIndex = (it.Segment.SourceIndex?.IndexNumber ?? 0).ToString("00");
                }
                _selStep = "1";
            }
            else
            {
                _selField = _selField switch
                {
                    SelField.Chapter => SelField.Step,
                    SelField.Step => SelField.Index,
                    _ => SelField.Chapter,
                };
            }
            _selFresh = true; // フィールドに入った直後は最初の数字で置き換える
            RefreshDisplay();
        }

        private void OnNumber(int digit)
        {
            if (!_powered) return;

            if (_selecting)
            {
                // フィールドに入って最初の1桁は置き換え、以降は追記（既存番号からの続き入力を防ぐ）。
                switch (_selField)
                {
                    case SelField.Chapter:
                        _selChapter = NextEntry(_selChapter, digit, 3);
                        break;
                    case SelField.Step:
                        _selStep = NextEntry(_selStep, digit, 2);
                        break;
                    case SelField.Index:
                        _selIndex = NextEntry(_selIndex, digit, 2);
                        break;
                }
                _selFresh = false;
                RefreshDisplay();
                return;
            }

            // アンサー（クイズ）モード：選択肢にある番号だけ受け付ける。
            if (IsAnswerMode())
            {
                if (IsValidChoiceDigit(digit, CurrentChoiceCount()))
                {
                    // 先に選択肢を再生（ActuallyPlay が _choiceChar をクリアする）→ そのあと番号をセット
                    PlayChoice(digit);
                    _choiceChar = (char)('0' + (digit % 10));
                    RefreshDisplay();
                }
                return;
            }

            // SELECT でもアンサーでもない時は数字ボタンは無効（UI 上も無効化済み）。
        }

        /// <summary>選択桁への数字入力。新規入力なら置き換え、継続なら末尾追記（最大 maxLen 桁）。</summary>
        private string NextEntry(string cur, int digit, int maxLen)
        {
            char c = (char)('0' + (digit % 10));
            if (_selFresh) return c.ToString();
            string s = (cur ?? "") + c;
            if (s.Length > maxLen) s = s.Substring(s.Length - maxLen);
            return s;
        }

        private void OnClear()
        {
            if (!_powered || !_selecting) return;
            switch (_selField)
            {
                case SelField.Chapter: _selChapter = "0"; break;
                case SelField.Step: _selStep = "0"; break;
                case SelField.Index: _selIndex = "0"; break;
            }
            _selFresh = true; // クリア後の最初の数字で置き換える
            RefreshDisplay();
        }

        private void OnStepPrev()
        {
            if (!_powered || !_discLoaded || _curUnit < 0) return;
            _repeating = false;
            int target = (_preRoll == PreRollKind.ChapterPrev)
                ? PrevChapterHead(_curUnit)
                : CurrentChapterHead(_curUnit);
            if (target < 0) target = CurrentChapterHead(_curUnit);
            PlayWithPreRoll(target, PreRollKind.ChapterPrev);
        }

        private void OnStepNext()
        {
            if (!_powered || !_discLoaded || _curUnit < 0) return;
            _repeating = false;
            int target = NextChapterHead(_curUnit);
            if (target < 0) return; // 次が無ければ何もしない
            PlayWithPreRoll(target, PreRollKind.None);
        }

        private void OnBack()
        {
            if (!_powered || !_discLoaded || _curUnit < 0) return;
            _repeating = false;
            int target = (_preRoll == PreRollKind.IndexBack)
                ? PrevIndexHead(_curUnit)
                : CurrentIndexHead(_curUnit);
            if (target < 0) target = CurrentIndexHead(_curUnit);
            PlayWithPreRoll(target, PreRollKind.IndexBack);
        }

        private void OnPause()
        {
            if (!_powered) return;

            if (_selecting)
            {
                CommitSelectionAndPlay();
                return;
            }

            switch (_mode)
            {
                case DeviceMode.Playing:
                    _engine.Pause();
                    _mode = DeviceMode.Paused;
                    RefreshDisplay();
                    break;
                case DeviceMode.Paused:
                    _engine.Resume();
                    _mode = DeviceMode.Playing;
                    RefreshDisplay();
                    break;
                case DeviceMode.Idle:
                    if (_curUnit >= 0) ActuallyPlay(_curUnit, false);
                    break;
                case DeviceMode.Tail:
                    // 末尾まで再生して止まっている場合は何もしない
                    break;
            }
        }

        private void OnGo()
        {
            if (!_powered) return;

            if (_selecting)
            {
                CommitSelectionAndPlay();
                return;
            }

            switch (_mode)
            {
                case DeviceMode.Playing:
                case DeviceMode.Paused:
                case DeviceMode.Tail:
                    _repeating = false;
                    int next = NextIndexHead(_curUnit);
                    if (next >= 0) PlayWithPreRoll(next, PreRollKind.None);
                    else EnterTail();
                    break;
                case DeviceMode.Idle:
                    if (_curUnit >= 0) ActuallyPlay(_curUnit, false);
                    break;
            }
        }

        private void OnStop()
        {
            if (!_powered) return;
            _engine.Stop();
            CancelTransitions();
            _repeating = false;
            _selecting = false;
            // インデックスの選択状態（_curUnit）は維持。表示は読み込み直後相当へ。
            _mode = _discLoaded ? DeviceMode.Idle : DeviceMode.NoDisc;
            _choiceChar = ' ';
            RefreshDisplay();
        }

        private void OnRepeat()
        {
            if (!_powered || _curUnit < 0) return;

            if (!_repeating)
            {
                ActuallyPlay(_curUnit, true); // 先頭からループ再生開始
                return;
            }

            // ループ中
            if (_mode == DeviceMode.Playing)
            {
                _engine.Pause();
                _mode = DeviceMode.Paused;
                RefreshDisplay();
            }
            else
            {
                ActuallyPlay(_curUnit, true); // 先頭へ戻ってループ再開
            }
        }

        //=====================================================================
        //  再生制御
        //=====================================================================

        /// <summary>0.5秒ポーズを挟んでから指定ユニットを再生する。</summary>
        private void PlayWithPreRoll(int unit, PreRollKind kind)
        {
            if (unit < 0) return;
            _engine.Stop();
            _curUnit = unit;
            _preRoll = kind;
            _pendingRepeat = false;
            _mode = DeviceMode.PreRoll;
            SyncListSelection();
            RefreshDisplay();

            _tmrTrans.Stop();
            _tmrTrans.Interval = PreRollMs;
            _tmrTrans.Start();
        }

        private void OnTransitionTick(object? sender, EventArgs e)
        {
            _tmrTrans.Stop();
            _preRoll = PreRollKind.None;
            ActuallyPlay(_curUnit, _pendingRepeat);
        }

        /// <summary>指定ユニットを先頭から実際に再生する。</summary>
        private void ActuallyPlay(int unit, bool repeat)
        {
            if (unit < 0 || unit >= _segmentItems.Count) return;
            _curUnit = unit;
            _repeating = repeat;
            _choiceChar = ' ';

            short[] pcm;
            try
            {
                pcm = DecodeSegmentPcm(_segmentItems[unit]);
            }
            catch
            {
                pcm = Array.Empty<short>();
            }

            if (pcm.Length == 0)
            {
                EnterTail();
                return;
            }

            _engine.Volume = _trkVolume.Value / 100f;
            _engine.Play(pcm);
            _mode = DeviceMode.Playing;
            SyncListSelection();
            RefreshDisplay();
        }

        /// <summary>再生が自然終了したとき（末尾到達）。連続再生フラグ・リピートを処理する。</summary>
        private void OnEngineEnded()
        {
            if (!_powered) return;

            if (_repeating)
            {
                // ループ: 0.5秒の間（ポーズ表示）を置いて先頭から再生
                _mode = DeviceMode.PreRoll;
                _pendingRepeat = true;
                RefreshDisplay();
                _tmrTrans.Stop();
                _tmrTrans.Interval = PreRollMs;
                _tmrTrans.Start();
                return;
            }

            if (_curUnit < 0) { EnterTail(); return; }

            var (playback, _) = GetSegmentFlags(_segmentItems[_curUnit]);
            if (playback == PlaybackContinuation.Stop)
            {
                EnterTail();
                return;
            }

            int next = playback == PlaybackContinuation.NextSubIndex
                ? FindNextSubIndex(_curUnit)
                : FindNextIndex(_curUnit);

            if (next >= 0) ActuallyPlay(next, false);
            else EnterTail();
        }

        private void EnterTail()
        {
            _repeating = false;
            _mode = DeviceMode.Tail;
            RefreshDisplay();
        }

        private void CommitSelectionAndPlay()
        {
            _selecting = false;

            int chap = ParseOr(_selChapter, -1);
            int idx = ParseOr(_selIndex, -1);
            // ステップは何が選ばれても強制的に 1
            _selStep = "1";

            int unit = (chap >= 0 && idx >= 0) ? FirstUnitOfIndex(chap, idx) : -1;
            if (unit < 0 && chap >= 0) unit = FirstUnitOfChapter(chap); // インデックス無ければチャプター先頭

            if (unit < 0)
            {
                // チャプター/インデックスが存在しない → 初期状態へ
                _curUnit = _segmentItems.Count > 0 ? 0 : -1;
                _mode = _discLoaded ? DeviceMode.Idle : DeviceMode.NoDisc;
                RefreshDisplay();
                return;
            }

            _repeating = false;
            ActuallyPlay(unit, false);
        }

        private static int ParseOr(string s, int fallback)
            => int.TryParse(s, out int v) ? v : fallback;

        private void CancelTransitions()
        {
            _tmrTrans?.Stop();
            _preRoll = PreRollKind.None;
            _pendingRepeat = false;
        }

        private bool IsQuizUnit(int unit)
        {
            if (unit < 0 || unit >= _segmentItems.Count) return false;
            var (_, kind) = GetSegmentFlags(_segmentItems[unit]);
            return kind != SegmentKind.Regular;
        }

        //=====================================================================
        //  アンサー（クイズ）モードと数字ボタンの有効制御
        //=====================================================================

        /// <summary>
        /// アンサーモード（クイズの選択肢入力を受け付ける状態）か。
        /// 現在のユニットがクイズ（種別が Regular 以外）で、SELECT 中でないとき。
        /// </summary>
        private bool IsAnswerMode()
        {
            if (_selecting || !_discLoaded || _curUnit < 0) return false;
            return IsQuizUnit(_curUnit);
        }

        /// <summary>
        /// 現在のクイズの選択肢数。サブインデックス0は「問題」なので、
        /// 選択肢はサブ1以降（= サブ数 - 1）。
        /// </summary>
        private int CurrentChoiceCount()
        {
            if (_curUnit < 0 || _curUnit >= _segmentItems.Count) return 0;
            var idx = _segmentItems[_curUnit].Segment.SourceIndex;
            int subs = idx?.SubIndices.Count ?? 0;
            return Math.Max(0, subs - 1);
        }

        /// <summary>数字 digit が、選択肢数 count に対して有効な選択肢か（1..count）。</summary>
        private static bool IsValidChoiceDigit(int digit, int count)
            => digit >= 1 && digit <= count;

        /// <summary>
        /// 選択された選択肢（1始まり）に対応するサブインデックスを再生する。
        /// サブ0は問題なので、ボタン K はサブインデックス番号 K に対応する。
        /// </summary>
        private void PlayChoice(int choice)
        {
            if (_curUnit < 0) return;
            var idx = _segmentItems[_curUnit].Segment.SourceIndex;
            if (idx is null) return;

            for (int i = 0; i < _segmentItems.Count; i++)
            {
                var it = _segmentItems[i];
                if (ReferenceEquals(it.Segment.SourceIndex, idx)
                    && it.Segment.SourceSubIndex?.SubNumber == choice)
                {
                    ActuallyPlay(i, false);
                    return;
                }
            }
        }

        /// <summary>SELECT / アンサーの状態に応じて数字ボタンの有効・無効を更新する。</summary>
        private void UpdateNumberButtons()
        {
            bool selecting = _selecting;
            bool answer = IsAnswerMode();
            int choices = answer ? CurrentChoiceCount() : 0;

            for (int d = 0; d <= 9; d++)
            {
                var btn = _numButtons[d];
                if (btn is null) continue;

                bool enabled;
                if (selecting) enabled = true;                          // SELECT 中は全桁入力可
                else if (answer) enabled = IsValidChoiceDigit(d, choices); // 選択肢のみ
                else enabled = false;                                   // それ以外は無効

                btn.Enabled = enabled;
            }
        }

        //=====================================================================
        //  ナビゲーション（_segmentItems はチャプター昇順→インデックス昇順→サブ順）
        //=====================================================================

        private static (int chap, int idx) UnitKey(SegmentListItem it)
            => (it.ChapterNo, it.Segment.SourceIndex?.IndexNumber ?? 0);

        private int FirstUnitOfChapter(int chap)
        {
            for (int i = 0; i < _segmentItems.Count; i++)
                if (_segmentItems[i].ChapterNo == chap) return i;
            return -1;
        }

        private int FirstUnitOfIndex(int chap, int idx)
        {
            for (int i = 0; i < _segmentItems.Count; i++)
            {
                var k = UnitKey(_segmentItems[i]);
                if (k.chap == chap && k.idx == idx) return i;
            }
            return -1;
        }

        private int CurrentChapterHead(int unit)
        {
            if (unit < 0) return -1;
            return FirstUnitOfChapter(_segmentItems[unit].ChapterNo);
        }

        private int PrevChapterHead(int unit)
        {
            int head = CurrentChapterHead(unit);
            if (head <= 0) return -1;
            return FirstUnitOfChapter(_segmentItems[head - 1].ChapterNo);
        }

        private int NextChapterHead(int unit)
        {
            if (unit < 0) return -1;
            int chap = _segmentItems[unit].ChapterNo;
            for (int i = 0; i < _segmentItems.Count; i++)
                if (_segmentItems[i].ChapterNo > chap) return i;
            return -1;
        }

        private int CurrentIndexHead(int unit)
        {
            if (unit < 0) return -1;
            var k = UnitKey(_segmentItems[unit]);
            return FirstUnitOfIndex(k.chap, k.idx);
        }

        private int PrevIndexHead(int unit)
        {
            int head = CurrentIndexHead(unit);
            if (head <= 0) return -1;
            var pk = UnitKey(_segmentItems[head - 1]);
            return FirstUnitOfIndex(pk.chap, pk.idx);
        }

        private int NextIndexHead(int unit)
        {
            if (unit < 0) return -1;
            var k = UnitKey(_segmentItems[unit]);
            for (int i = 0; i < _segmentItems.Count; i++)
            {
                var ki = UnitKey(_segmentItems[i]);
                if (ki.chap > k.chap || (ki.chap == k.chap && ki.idx > k.idx)) return i;
            }
            return -1;
        }

        //=====================================================================
        //  表示更新
        //=====================================================================

        /// <summary>現在のデバイス状態から 7セグ表示窓の内容を組み立てて反映する。</summary>
        private void RefreshDisplay()
        {
            if (_display is null) return;

            UpdateNumberButtons();

            var s = new SevenSegState();

            if (!_powered)
            {
                s.Blank = true;
                _display.State = s;
                return;
            }

            if (_loading || _mode == DeviceMode.Loading)
            {
                // 読み込み中: 表示は空、再生マークを点滅
                s.PlayIcon = _blinkOn;
                _display.State = s;
                return;
            }

            if (!_discLoaded)
            {
                s.Overlay = " OPEN";
                _display.State = s;
                return;
            }

            // ここから有効ファイル読み込み済み
            s.ChapterLabel = true;
            s.StepLabel = true;
            s.ShowHyphen = true;

            if (_selecting)
            {
                s.Chapter = _selChapter;
                s.Step = _selStep;
                s.Index = _selIndex;
                _display.State = s;
                return;
            }

            s.Step = "1";
            SegmentListItem? u = _curUnit >= 0 && _curUnit < _segmentItems.Count ? _segmentItems[_curUnit] : null;
            if (u is not null)
            {
                s.Chapter = u.ChapterNo.ToString();
                int idxNo = u.Segment.SourceIndex?.IndexNumber ?? 0;
                s.Index = idxNo.ToString("00");
            }

            // クイズ表示（ANSWER）
            SegmentKind kind = u is not null ? GetSegmentFlags(u).kind : SegmentKind.Regular;
            bool quiz = kind != SegmentKind.Regular;
            s.AnswerLabel = quiz;

            if (_mode == DeviceMode.Playing && (kind == SegmentKind.Question || kind == SegmentKind.CorrectAnswer))
            {
                s.Answer = 'A';
            }
            else if (quiz && (_mode == DeviceMode.Paused || _mode == DeviceMode.Tail || kind == SegmentKind.WrongAnswer))
            {
                // クイズ問題が停止/一時停止、または誤答再生中は点滅
                s.Answer = _blinkOn ? 'A' : ' ';
            }
            else
            {
                s.Answer = ' ';
            }

            s.Choice = _choiceChar;

            // マーク
            s.PlayIcon = _mode == DeviceMode.Playing;
            s.PauseIcon = _mode == DeviceMode.Paused
                       || _mode == DeviceMode.Tail
                       || _mode == DeviceMode.PreRoll;

            _display.State = s;
        }

        /// <summary>リスト行の直接再生（ダブルクリック等）。エンジンへ載せ替える。</summary>
        private void PlayUnitFromList(int listIndex)
        {
            if (!_powered || listIndex < 0 || listIndex >= _segmentItems.Count) return;
            _selecting = false;
            _repeating = false;
            CancelTransitions();
            ActuallyPlay(listIndex, false);
        }

        /// <summary>デバイス関連リソースの後始末（OnFormClosed から）。</summary>
        private void DisposeDevice()
        {
            _tmrTrans?.Stop();
            _tmrBlink?.Stop();
            _engine?.Dispose();
            _recorder?.Dispose();
        }
    }
}
