using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ProgressusInLinguaAnglica.Model;
using ProgressusInLinguaAnglica.Xa;

namespace ProgressusInLinguaAnglica
{
    /// <summary>
    /// メインフォーム
    /// </summary>
    public partial class MainForm : Form
    {
        private string? _rootPath;
        private XaRiffReader? _xaRiff;
        private XaSectorLocator? _locator;
        private readonly List<TrackTable> _tracks = new();

        // TBL のバックグラウンド読み込み制御
        private CancellationTokenSource? _loadCts;

        // セグメント表示用
        private readonly List<SegmentListItem> _segmentItems = new();

        // 再生サンプルレート（いったん固定値）
        private const int PlaybackSampleRate = 18900;

        /// <summary>
        /// リストボックス1行とセグメントの対応付け
        /// </summary>
        private sealed class SegmentListItem
        {
            public TrackTable Track { get; init; } = null!;
            public Segment Segment { get; init; } = null!;
            public int ChapterNo { get; init; }
            public string DisplayText { get; init; } = "";

            public override string ToString() => DisplayText;
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            InitDevicePanel();
        }

        /// <summary>
        /// フォームを閉じるときに、開きっぱなしのファイルハンドルや
        /// バックグラウンド処理を確実に後始末する。
        /// </summary>
        /// <param name="e">イベントパラメータ</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearState();
            DisposeDevice();
            base.OnFormClosed(e);
        }

        //=====================================================================
        //  CD 自動認識（メディア挿入/取り出しの検知）
        //=====================================================================

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;        // メディア・デバイスの挿入完了
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004; // メディア・デバイスの取り出し完了
        private const int DBT_DEVTYP_VOLUME = 0x0002;        // ボリューム（ドライブ）

        /// <summary>
        /// 起動時、既にディスクが入っているドライブがあれば認識して読み込む。
        /// </summary>
        /// <param name="e">イベントパラメータ</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TryAutoLoadReadyDisc();
        }

        /// <summary>
        /// デバイス変更メッセージ（CD の挿入/取り出し）を監視する。
        /// </summary>
        /// <param name="m">ウィンドウメッセージ</param>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE
                && m.LParam != IntPtr.Zero
                && Marshal.ReadInt32(m.LParam, 4) == DBT_DEVTYP_VOLUME)
            {
                long evt = m.WParam.ToInt64();
                int unitMask = Marshal.ReadInt32(m.LParam, 12);
                var driveRoots = UnitMaskToDriveRoots(unitMask);

                if (evt == DBT_DEVICEARRIVAL)
                {
                    // 挿入直後はメディアがまだ読めないことがあるため、少し待ちつつ判定する。
                    ScheduleDiscDetection(driveRoots);
                }
                else if (evt == DBT_DEVICEREMOVECOMPLETE)
                {
                    OnVolumeRemoved(driveRoots); // WndProc は UI スレッド
                }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// ユニットマスク（ビット0=A:, ビット1=B:, ...）をドライブのルートパス一覧に変換する。
        /// </summary>
        /// <param name="unitMask">DEV_BROADCAST_VOLUME のユニットマスク</param>
        /// <returns>ルートパス（例: "D:\"）の一覧</returns>
        private static List<string> UnitMaskToDriveRoots(int unitMask)
        {
            var roots = new List<string>();
            for (int i = 0; i < 26; i++)
            {
                if ((unitMask & (1 << i)) != 0)
                    roots.Add($"{(char)('A' + i)}:\\");
            }
            return roots;
        }

        /// <summary>
        /// 挿入されたボリュームを少し待ちつつ繰り返し確認し、
        /// SOUND.RTF を持つドライブが見つかったら読み込む。
        /// </summary>
        /// <param name="driveRoots">挿入されたドライブのルートパス一覧</param>
        private void ScheduleDiscDetection(List<string> driveRoots)
        {
            Task.Run(async () =>
            {
                // メディアがマウントされるまで数回リトライする。
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    foreach (var root in driveRoots)
                    {
                        if (HasSoundRtf(root))
                        {
                            PostToUi(CancellationToken.None, () =>
                            {
                                statusLabel.Text = $"ディスクを認識しました: {root}";
                                LoadRoot(root);
                            });
                            return;
                        }
                    }

                    await Task.Delay(800).ConfigureAwait(false);
                }
            });
        }

        /// <summary>
        /// 取り出されたドライブが現在読み込み中のディスクなら、状態を破棄して
        /// ファイルハンドルを解放する。
        /// </summary>
        /// <param name="driveRoots">取り出されたドライブのルートパス一覧</param>
        private void OnVolumeRemoved(List<string> driveRoots)
        {
            if (_rootPath is null) return;

            string? currentRoot = Path.GetPathRoot(_rootPath);
            if (string.IsNullOrEmpty(currentRoot)) return;

            foreach (var root in driveRoots)
            {
                if (string.Equals(Path.GetPathRoot(root), currentRoot, StringComparison.OrdinalIgnoreCase))
                {
                    ClearState();
                    statusLabel.Text = "ディスクが取り出されました。";
                    return;
                }
            }
        }

        /// <summary>
        /// 起動時用：準備完了済みの CD-ROM ドライブから SOUND.RTF 入りのものを探して読み込む。
        /// </summary>
        private void TryAutoLoadReadyDisc()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.CDRom || !drive.IsReady)
                        continue;

                    string root = drive.RootDirectory.FullName;
                    if (HasSoundRtf(root))
                    {
                        statusLabel.Text = $"ディスクを認識しました: {root}";
                        LoadRoot(root);
                        return;
                    }
                }
            }
            catch
            {
                // 起動時スキャンの失敗は致命的ではないので無視する。
            }
        }

        /// <summary>
        /// 指定ルート直下に SOUND.RTF が存在するか。メディア未準備等は false 扱い。
        /// </summary>
        /// <param name="root">ドライブのルートパス</param>
        /// <returns>SOUND.RTF があれば true</returns>
        private static bool HasSoundRtf(string root)
        {
            try
            {
                return Directory.EnumerateFiles(root, "SOUND.RTF*", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ファイルメニュー - フォルダを開く
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void menuFileOpenFolder_Click(object? sender, EventArgs e)
        {
            BrowseAndLoadFolder();
        }

        /// <summary>
        /// ファイルメニュー - 終了
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void menuFileExit_Click(object? sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 参照ボタン
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void btnBrowseFolder_Click(object? sender, EventArgs e)
        {
            BrowseAndLoadFolder();
        }

        /// <summary>
        /// リストボックスダブルクリック
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void lstChapters_DoubleClick(object? sender, EventArgs e)
        {
            PlaySelectedChapter();
        }

        /// <summary>
        /// 再生ボタンクリック
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void btnPlaySelected_Click(object? sender, EventArgs e)
        {
            PlaySelectedChapter();
        }

        /// <summary>
        /// ファイル参照ダイアログ表示
        /// </summary>
        private void BrowseAndLoadFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "対応ディスクがマウントされているドライブ、またはディスクデータのルートフォルダーを選択してください。",
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                LoadRoot(dlg.SelectedPath);
            }
        }

        /// <summary>
        /// ディスク読み取り
        /// </summary>
        /// <param name="path">ディスクディレクトリのパス</param>
        private void LoadRoot(string path)
        {
            // 前回の読み込み・再生状態を破棄（ファイルハンドルもここで閉じる）
            ClearState();

            statusLabel.Text = "解析中...";

            _rootPath = path;
            txtRootPath.Text = path;

            // SOUND.RTF を探す
            var soundPath = Directory.EnumerateFiles(path, "SOUND.RTF*", SearchOption.TopDirectoryOnly)
                                     .FirstOrDefault();
            if (soundPath is null)
            {
                MessageBox.Show(this, "SOUND.RTF が見つかりませんでした。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ClearState();
                return;
            }

            try
            {
                // RIFF ヘッダと先頭セクタだけ読む。全走査しないので即座に開く。
                _xaRiff = new XaRiffReader(soundPath);
                _locator = new XaSectorLocator(_xaRiff);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"SOUND.RTF の読み込みに失敗しました。\r\n{ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ClearState();
                return;
            }

            // 表示窓を読み込み中表示に（再生マーク点滅）
            DeviceOnLoadingStarted();

            // Cxxx.TBL を列挙（CHAP.TBL は別用途なので除外）
            var tblFiles = Directory.EnumerateFiles(path, "C*.TBL", SearchOption.TopDirectoryOnly)
                                    .Where(f => !string.Equals(Path.GetFileName(f), "CHAP.TBL",
                                                               StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

            // TBL の解析はバックグラウンドで行い、リストへ逐次追加していく。
            _loadCts = new CancellationTokenSource();
            LoadTracksInBackground(tblFiles, _loadCts.Token);
        }

        /// <summary>
        /// TBL ファイル群をバックグラウンドで順次解析し、解析できたトラックから
        /// リスト（チャプター行）へ逐次反映する。UI はブロックしない。
        /// </summary>
        /// <param name="tblFiles">TBL ファイルパスの一覧（ソート済み）</param>
        /// <param name="token">キャンセルトークン</param>
        private void LoadTracksInBackground(List<string> tblFiles, CancellationToken token)
        {
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();

                for (int i = 0; i < tblFiles.Count; i++)
                {
                    if (token.IsCancellationRequested) return;

                    TrackTable? track = null;
                    try
                    {
                        track = TblParser.Parse(tblFiles[i]);
                    }
                    catch
                    {
                        // 解析できない TBL はとりあえずスキップ
                    }

                    if (track is null) continue;

                    // このトラックぶんのリスト行を組み立てて UI スレッドへ渡す。
                    int fallbackChapNo = i + 1;
                    var items = BuildSegmentItemsForTrack(track, fallbackChapNo);

                    if (token.IsCancellationRequested) return;

                    PostToUi(token, () =>
                    {
                        _tracks.Add(track);
                        if (items.Count > 0)
                        {
                            _segmentItems.AddRange(items);
                            lstChapters.BeginUpdate();
                            foreach (var it in items)
                                lstChapters.Items.Add(it);
                            lstChapters.EndUpdate();
                        }
                        statusLabel.Text =
                            $"TBL 解析中... チャプター {_tracks.Count} 件 / セグメント {_segmentItems.Count} 件";
                    });
                }

                sw.Stop();
                long ms = sw.ElapsedMilliseconds;
                if (token.IsCancellationRequested) return;

                PostToUi(token, () =>
                {
                    statusLabel.Text =
                        $"読み込み完了: チャプター {_tracks.Count} 件 / セグメント {_segmentItems.Count} 件 " +
                        $"（TBL解析 {ms} ms、音声インデックスはオンデマンド）";
                    DeviceOnLoadingFinished();
                });
            }, token);
        }

        /// <summary>
        /// バックグラウンドスレッドから UI スレッドへ処理を投げる。
        /// フォーム破棄やキャンセルと競合しても落ちないようガードする。
        /// </summary>
        /// <param name="token">キャンセルトークン</param>
        /// <param name="action">UI スレッドで実行する処理</param>
        private void PostToUi(CancellationToken token, Action action)
        {
            if (token.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    action();
                });
            }
            catch (ObjectDisposedException)
            {
                // フォームが閉じられた直後の競合。無視してよい。
            }
            catch (InvalidOperationException)
            {
                // ハンドル未作成／破棄との競合。無視してよい。
            }
        }

        /// <summary>
        /// 初期化
        /// </summary>
        private void ClearState()
        {
            _rootPath = null;

            // 進行中の TBL 読み込みを止める
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            // 再生エンジン・遷移タイマーを停止し、デバイス状態を初期化
            _engine?.Stop();
            CancelTransitions();
            _repeating = false;
            _selecting = false;
            _curUnit = -1;
            _discLoaded = false;
            _loading = false;
            _choiceChar = ' ';
            _mode = DeviceMode.NoDisc;

            _locator = null;
            _xaRiff?.Dispose();
            _xaRiff = null;

            _tracks.Clear();
            _segmentItems.Clear();
            lstChapters.Items.Clear();

            RefreshDisplay();
        }


        /// <summary>
        /// 選択チャプター再生（リストのダブルクリック等）。
        /// </summary>
        private void PlaySelectedChapter()
        {
            if (_locator is null || _xaRiff is null)
            {
                MessageBox.Show(this, "SOUND.RTF が読み込まれていません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int idx = lstChapters.SelectedIndex;
            if (idx < 0 || idx >= _segmentItems.Count)
            {
                MessageBox.Show(this, "セグメントを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PlayUnitFromList(idx);
        }

        /// <summary>
        /// 1 トラック分の、インデックス／サブインデックス単位のセグメント行を組み立てて返す。
        /// 共有状態には触れないため、バックグラウンドスレッドから呼べる。
        /// </summary>
        /// <param name="track">対象トラック</param>
        /// <param name="fallbackChapNo">ファイル名から番号を取れない場合のチャプター番号</param>
        /// <returns>このトラックに対応するリスト行</returns>
        private static List<SegmentListItem> BuildSegmentItemsForTrack(TrackTable track, int fallbackChapNo)
        {
            var result = new List<SegmentListItem>();

            // ファイル名から [001] の番号を推測（C001.TBL → 1）
            int chapNo = fallbackChapNo;
            var name = track.FileName;
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = Path.GetFileNameWithoutExtension(name).Substring(1);
                if (int.TryParse(numPart, out int n))
                {
                    chapNo = n;
                }
            }

            // INDX ごとにグループ
            var groups = track.Segments
                              .Where(seg => seg.SourceIndex is not null)
                              .GroupBy(seg => seg.SourceIndex!)
                              .OrderBy(g => g.Key.IndexNumber);

            foreach (var g in groups)
            {
                var index = g.Key;

                var subSegs = g.Where(s => s.SourceSubIndex is not null)
                               .OrderBy(s => s.SourceSubIndex!.SubNumber)
                               .ToList();

                var indexSegs = g.Where(s => s.SourceSubIndex is null)
                                 .OrderBy(s => s.StartFrame)
                                 .ToList();

                // サブインデックスが無い INDX → インデックス行としてそのまま出す
                if (subSegs.Count == 0)
                {
                    foreach (var seg in indexSegs)
                    {
                        string line = FormatIndexLine(chapNo, index, seg, track);
                        result.Add(new SegmentListItem
                        {
                            Track = track,
                            Segment = seg,
                            ChapterNo = chapNo,
                            DisplayText = line,
                        });
                    }
                }
                else
                {
                    // サブインデックスがある場合 → サブインデックスごとに1行
                    bool firstSubInIndex = true;
                    foreach (var seg in subSegs)
                    {
                        var sub = seg.SourceSubIndex!;
                        string line = FormatSubIndexLine(chapNo, index, sub, track, firstSubInIndex);
                        firstSubInIndex = false;

                        result.Add(new SegmentListItem
                        {
                            Track = track,
                            Segment = seg,
                            ChapterNo = chapNo,
                            DisplayText = line,
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// インデックス行成形する
        /// </summary>
        /// <param name="chapNo">チャプター番号</param>
        /// <param name="index">インデックス</param>
        /// <param name="seg">セグメント</param>
        /// <param name="track">トラックテーブル</param>
        /// <returns>インデックス行文字列</returns>
        private static string FormatIndexLine(int chapNo, TrackIndex index, Segment seg, TrackTable track)
        {
            string idxText = index.IndexNumber.ToString("00");
            string s = TblParser.FormatFrameAsTimeWithSector(seg.StartFrame, seg.StartByte);
            string e = TblParser.FormatFrameAsTimeWithSector(seg.EndFrame, seg.StartByte);

            string icons = GetControlIcons(index.PlaybackContinuation, index.SegmentKind);
            string iconPart = string.IsNullOrEmpty(icons) ? "" : $" {icons}";

            // チャネルと制御子は一旦、表示しない
            // string idxCtrl = index.ControlWord.ToString("X8");
            // string tail = $"/ ch{track.Header.Channel:00} / {idxCtrl}"; 

            // [001]-(00) 43:28.25 - 44:38.74 ⏬ など
            return $"[{chapNo:000}]-({idxText}) {s} - {e}{iconPart}";
        }

        /// <summary>
        /// サブインデックス行成形
        /// </summary>
        /// <param name="chapNo">チャプター番号</param>
        /// <param name="index">インデックス</param>
        /// <param name="sub">サブインデックス</param>
        /// <param name="track">トラックテーブル</param>
        /// <param name="isFirstInIndex">インデックスの先頭サブインデックスか</param>
        /// <returns></returns>
        private static string FormatSubIndexLine(int chapNo, TrackIndex index, TrackSubIndex sub, TrackTable track, bool isFirstInIndex)
        {
            string idxText = index.IndexNumber.ToString("00");
            string subText = sub.SubNumber.ToString("00");
            string s = TblParser.FormatFrameAsTimeWithSector(sub.StartFrame, sub.StartByte);
            string e = TblParser.FormatFrameAsTimeWithSector(sub.EndFrame, sub.EndByte);

            // サブインデックス自身のフラグを優先
            string icons = GetControlIcons(sub.PlaybackContinuation, sub.SegmentKind);
            string iconPart = string.IsNullOrEmpty(icons) ? "" : $" {icons}";

            // チャネルと制御子は一旦、表示しない
            // string subCtrl = sub.ControlWord.ToString("X8");
            // string tail = $"/ ch{track.Header.Channel:00} / {subCtrl}"; 

            // 全部 80000000 で特に意味ないので省略
            //// インデックス内の先頭サブインデックスの行だけ、インデックス制御子も後ろにつける
            //if (isFirstInIndex)
            //{
            //    string idxCtrl = index.ControlWord.ToString("X8");
            //    tail += $" / {idxCtrl}";
            //}

            // [001]-(00-00) 43:28.25 - 44:38.74 ❓🔽 など
            return $"[{chapNo:000}]-({idxText}-{subText}) {s} - {e}{iconPart}";
        }

        /// <summary>
        /// セグメントの音声をオンデマンドで抽出し、PCM16 モノラルへデコードする。
        /// ファイル I/O・デコードのみで UI には触れないため、バックグラウンドからも呼べる。
        /// </summary>
        /// <param name="item">対象セグメント行</param>
        /// <returns>デコード済み PCM。取得できなければ空配列。</returns>
        private short[] DecodeSegmentPcm(SegmentListItem item)
        {
            var locator = _locator;
            if (locator is null) return Array.Empty<short>();

            int channel = item.Track.Header.Channel;
            byte[] xaBytes = locator.ReadSegmentUserData(channel, item.Segment.StartFrame, item.Segment.EndFrame);
            if (xaBytes.Length == 0) return Array.Empty<short>();

            return XaAdpcmDecoder.DecodeMono(xaBytes, PlaybackSampleRate);
        }

        /// <summary>
        /// 旧 SoundPlayer 方式のタイマー連続再生は廃止（NAudio エンジン側で処理）。
        /// Designer がイベントを参照しているため空ハンドラだけ残す。
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            tmrPlayBack?.Stop();
        }

        /// <summary>
        /// セグメントフラグ取得
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static (PlaybackContinuation playback, SegmentKind kind) GetSegmentFlags(SegmentListItem item)
        {
            if (item.Segment.SourceSubIndex is TrackSubIndex sub)
            {
                return (sub.PlaybackContinuation, sub.SegmentKind);
            }

            if (item.Segment.SourceIndex is TrackIndex idx)
            {
                return (idx.PlaybackContinuation, idx.SegmentKind);
            }

            return (PlaybackContinuation.Stop, SegmentKind.Regular);
        }


        /// <summary>
        /// 次の再生セグメントを取得
        /// </summary>
        /// <param name="current">現在のセグメント位置</param>
        /// <returns>次の再生セグメント位置</returns>
        private int GetNextSegmentIndex(int current)
        {
            if (current < 0 || current >= _segmentItems.Count)
                return -1;

            var item = _segmentItems[current];
            var (playback, kind) = GetSegmentFlags(item);

            switch (playback)
            {
                case PlaybackContinuation.Stop:
                    return -1;

                case PlaybackContinuation.NextSubIndex:
                    return FindNextSubIndex(current);

                case PlaybackContinuation.NextIndex:
                    return FindNextIndex(current);

                default:
                    return -1;
            }
        }

        /// <summary>
        /// 次の再生サブインデックスを探索
        /// </summary>
        /// <param name="current">現在のセグメント位置</param>
        /// <returns>次の再生サブインデックス位置</returns>
        private int FindNextSubIndex(int current)
        {
            var curr = _segmentItems[current];

            // 次の行が同じインデックスに属し、サブ番号が +1 ならそれを選ぶ
            for (int i = current + 1; i < _segmentItems.Count; i++)
            {
                var next = _segmentItems[i];

                // インデックス変わったらサブインデックス終了
                if (next.Segment.SourceIndex?.IndexNumber != curr.Segment.SourceIndex?.IndexNumber)
                    break;

                if (next.Segment.SourceSubIndex != null &&
                    curr.Segment.SourceSubIndex != null &&
                    next.Segment.SourceSubIndex.SubNumber == curr.Segment.SourceSubIndex.SubNumber + 1)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 次の再生インデックスを探索
        /// </summary>
        /// <param name="current">現在のセグメント位置</param>
        /// <returns>次の再生インデックス位置</returns>
        private int FindNextIndex(int current)
        {
            var curr = _segmentItems[current];
            int currentIdx = curr.Segment.SourceIndex?.IndexNumber ?? -1;

            for (int i = current + 1; i < _segmentItems.Count; i++)
            {
                var next = _segmentItems[i];
                int nextIdx = next.Segment.SourceIndex?.IndexNumber ?? -1;

                // インデックス番号が増えたらその最初の行
                if (nextIdx > currentIdx)
                    return i;
            }

            return -1;
        }


        /// <summary>
        /// アイコン生成ヘルパー
        /// </summary>
        /// <param name="playback">連続再生フラグ</param>
        /// <param name="kind">種別フラグ</param>
        /// <returns>アイコン</returns>
        private static string GetControlIcons(PlaybackContinuation playback, SegmentKind kind)
        {
            string icons = "";

            // 種別
            switch (kind)
            {
                case SegmentKind.Question:
                    icons += "❓";
                    break;
                case SegmentKind.CorrectAnswer:
                    icons += "⭕";
                    break;
                case SegmentKind.WrongAnswer:
                    icons += "❌";
                    break;
            }

            // 連続再生フラグ
            switch (playback)
            {
                case PlaybackContinuation.NextSubIndex:
                    icons += "🔽";
                    break;
                case PlaybackContinuation.NextIndex:
                    icons += "⏬";
                    break;
            }

            return icons;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LstChapters_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            int index = lstChapters.IndexFromPoint(e.Location);
            if (index < 0 || index >= lstChapters.Items.Count)
                return;

            lstChapters.SelectedIndex = index;

            if (cmnSegment is not null)
            {
                cmnSegment.Show(lstChapters, e.Location);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SegmentSaveMenuItem_Click(object? sender, EventArgs e)
        {
            SaveSelectedSegmentAsWav();
        }

        /// <summary>
        /// 現在選択中のセグメントを WAV ファイルとして保存する。
        /// </summary>
        private void SaveSelectedSegmentAsWav()
        {
            if (_locator is null || _xaRiff is null)
            {
                MessageBox.Show(this, "SOUND.RTF が読み込まれていません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int idx = lstChapters.SelectedIndex;
            if (idx < 0 || idx >= _segmentItems.Count)
            {
                MessageBox.Show(this, "セグメントを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = _segmentItems[idx];
            var seg = item.Segment;

            if (seg.StartFrame >= seg.EndFrame)
            {
                MessageBox.Show(this, "このセグメントの時間情報が不正です。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 保存先ファイル名を決める
            string defaultFileName;
            if (item.Segment.SourceSubIndex is not null)
            {
                defaultFileName =
                    $"C{item.ChapterNo:000}_IDX{item.Segment.SourceIndex?.IndexNumber:00}_SUB{item.Segment.SourceSubIndex.SubNumber:00}.wav";
            }
            else if (item.Segment.SourceIndex is not null)
            {
                defaultFileName =
                    $"C{item.ChapterNo:000}_IDX{item.Segment.SourceIndex.IndexNumber:00}.wav";
            }
            else
            {
                defaultFileName = $"C{item.ChapterNo:000}_SEG{idx:000}.wav";
            }

            using var sfd = new SaveFileDialog
            {
                Title = "WAV ファイルとして保存",
                Filter = "WAV ファイル (*.wav)|*.wav|すべてのファイル (*.*)|*.*",
                FileName = defaultFileName,
                AddExtension = true,
                DefaultExt = "wav",
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                statusLabel.Text = "音声抽出中...";
                Cursor = Cursors.WaitCursor;

                // 指定範囲をオンデマンドで抽出し PCM16 へデコード
                short[] pcm = DecodeSegmentPcm(item);
                if (pcm.Length == 0)
                {
                    MessageBox.Show(this, "指定範囲の音声が取得できませんでした。", "エラー",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // WAV ファイルとして保存
                using (var fs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write))
                {
                    XaWavWriter.WritePcm16MonoWav(fs, PlaybackSampleRate, pcm);
                }

                statusLabel.Text = $"保存しました: {Path.GetFileName(sfd.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"保存中にエラーが発生しました。\r\n{ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
