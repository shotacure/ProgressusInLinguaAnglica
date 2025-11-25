using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
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
        private XaSectorIndex? _xaIndex;
        private readonly List<TrackTable> _tracks = new();

        // セグメント表示用
        private readonly List<SegmentListItem> _segmentItems = new();

        // 再生状態管理
        private SoundPlayer? _player;
        private MemoryStream? _currentAudioStream;
        private System.Windows.Forms.Timer? _playbackTimer;
        private int _currentSegmentIndex = -1;

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

            // 再生用タイマー（1セグメント再生終了後に次の行へ進む）
            _playbackTimer = new System.Windows.Forms.Timer();
            _playbackTimer.Interval = 500;
            _playbackTimer.Tick += PlaybackTimer_Tick;
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
                Description = "CDリピーター用ディスクがマウントされているドライブ、またはディスクデータのルートフォルダーを選択してください。",
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
            try
            {
                statusLabel.Text = "解析中...";
                Cursor = Cursors.WaitCursor;

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

                _xaRiff = new XaRiffReader(soundPath);
                _xaIndex = new XaSectorIndex(_xaRiff);
                statusLabel.Text = "SOUND.RTF インデックス作成完了";

                // Cxxx.TBL を列挙（CHAP.TBL は別用途なので除外）
                var tblFiles = Directory.EnumerateFiles(path, "C*.TBL", SearchOption.TopDirectoryOnly)
                                        .Where(f => !string.Equals(Path.GetFileName(f), "CHAP.TBL",
                                                                   StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                        .ToList();

                _tracks.Clear();
                foreach (var tblPath in tblFiles)
                {
                    try
                    {
                        var track = TblParser.Parse(tblPath);
                        if (track is not null)
                        {
                            _tracks.Add(track);
                        }
                    }
                    catch
                    {
                        // 解析できない TBL はとりあえずスキップ
                    }
                }

                RebuildSegmentListItems();



                statusLabel.Text = $"TBL 解析完了: {_tracks.Count} 件";
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// 初期化
        /// </summary>
        private void ClearState()
        {
            _rootPath = null;
            _xaRiff = null;
            _xaIndex = null;
            _tracks.Clear();
            _segmentItems.Clear();
            lstChapters.Items.Clear();

            // 再生関係のクリーンアップ
            _playbackTimer?.Stop();
            _currentSegmentIndex = -1;

            _player?.Stop();
            _player?.Dispose();
            _player = null;

            _currentAudioStream?.Dispose();
            _currentAudioStream = null;
        }


        /// <summary>
        /// 選択チャプター再生
        /// </summary>
        private void PlaySelectedChapter()
        {
            if (_xaIndex is null || _xaRiff is null)
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

            StartPlaybackForSegment(idx);
        }

        /// <summary>
        /// _tracks から、インデックス／サブインデックス単位のセグメント一覧を組み立てて lstChapters に流し込む。
        /// </summary>
        private void RebuildSegmentListItems()
        {
            _segmentItems.Clear();
            lstChapters.Items.Clear();

            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];

                // ファイル名から [001] の番号を推測（C001.TBL → 1）
                int chapNo = i + 1;
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
                            var item = new SegmentListItem
                            {
                                Track = track,
                                Segment = seg,
                                ChapterNo = chapNo,
                                DisplayText = line,
                            };
                            _segmentItems.Add(item);
                            lstChapters.Items.Add(item);
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

                            var item = new SegmentListItem
                            {
                                Track = track,
                                Segment = seg,
                                ChapterNo = chapNo,
                                DisplayText = line,
                            };
                            _segmentItems.Add(item);
                            lstChapters.Items.Add(item);
                        }
                    }
                }
            }

            statusLabel.Text =
                $"TBL 解析完了: チャプター {_tracks.Count} 件 / セグメント {_segmentItems.Count} 件";
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

            // チャネルと制御子は一旦、表示しない
            // string idxCtrl = index.ControlWord.ToString("X8");
            // string tail = $"/ ch{track.Header.Channel:00} / {idxCtrl}"; 

            // [001]-(00) 43:28_25<00> - 44:38_74<00> / ch00 / 01000000
            return $"[{chapNo:000}]-({idxText}) {s} - {e}";
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

            // [001]-(00-00) 43:28_25 - 44:38_74 / ch00 / mode00 / subCtrl[/ idxCtrl]
            return $"[{chapNo:000}]-({idxText}-{subText}) {s} - {e}";
        }

        /// <summary>
        /// 指定したリスト行（セグメント）を再生開始し、必要なら次の行への連続再生もセットする。
        /// </summary>
        /// <param name="listIndex"></param>
        private void StartPlaybackForSegment(int listIndex)
        {
            if (_xaIndex is null || _xaRiff is null)
                return;
            if (listIndex < 0 || listIndex >= _segmentItems.Count)
                return;

            var item = _segmentItems[listIndex];
            var track = item.Track;
            var seg = item.Segment;

            // セグメントの開始～終了フレーム
            int channel = track.Header.Channel;
            int startFrame = seg.StartFrame;
            int endFrame = seg.EndFrame;

            if (startFrame >= endFrame)
            {
                MessageBox.Show(this, "このセグメントの時間情報が不正です。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                statusLabel.Text = "音声抽出中...";
                Cursor = Cursors.WaitCursor;

                // 該当区間のセクタ一覧を取得
                var sectors = _xaIndex.GetSectors(channel, startFrame, endFrame).ToList();
                if (sectors.Count == 0)
                {
                    MessageBox.Show(this, "指定範囲に対応するセクタが見つかりませんでした。", "エラー",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // XA セクタのユーザーデータ（2336バイト）を全部繋げる
                using var msXa = new MemoryStream();
                foreach (var s in sectors)
                {
                    var userData = _xaRiff.ReadUserData(s.FileOffset);
                    msXa.Write(userData, 0, userData.Length);
                }

                byte[] xaBytes = msXa.ToArray();

                // XA ADPCM → PCM16
                const int sampleRate = 18900; // いったん固定値
                short[] pcm = XaAdpcmDecoder.DecodeMono(xaBytes, sampleRate);
                if (pcm.Length == 0)
                {
                    MessageBox.Show(this, "デコード結果が空でした。", "エラー",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 既存再生を停止
                _playbackTimer?.Stop();
                _player?.Stop();
                _player?.Dispose();
                _player = null;
                _currentAudioStream?.Dispose();
                _currentAudioStream = null;

                // メモリ上に WAV を構築して再生
                _currentAudioStream = new MemoryStream();
                XaWavWriter.WritePcm16MonoWav(_currentAudioStream, sampleRate, pcm);
                _currentAudioStream.Position = 0;

                _currentSegmentIndex = listIndex;
                lstChapters.SelectedIndex = listIndex; // 再生中のセグメント行を選択状態にする

                statusLabel.Text = item.DisplayText;
                _player = new SoundPlayer(_currentAudioStream);
                _player.Play(); // 非同期再生

                // このセグメントの制御子がストップマーカーを持つなら、ここで一旦停止（次の自動再生は行わない）
                bool stopAfter = HasStopMarker(item);

                if (!stopAfter)
                {
                    // セグメント長から次セグメントの再生開始タイミングをだいたい計算
                    int lengthMs = Math.Max(100, (int)(pcm.Length * 1000.0 / sampleRate));
                    if (_playbackTimer is not null)
                    {
                        _playbackTimer.Interval = lengthMs + 500;
                        _playbackTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"再生中にエラーが発生しました。\r\n{ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// 再生中のセグメントが終わったらタイマー経由で次の行へ。
        /// </summary>
        /// <param name="sender">イベント送信元オブジェクト</param>
        /// <param name="e">イベントパラメータ</param>
        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            _playbackTimer?.Stop();
            int next = _currentSegmentIndex + 1;
            if (next >= 0 && next < _segmentItems.Count)
            {
                StartPlaybackForSegment(next);
            }
        }

        /// <summary>
        /// インデックス／サブインデックスの制御子のストップマーカー(先頭バイト)が1x以外なら、そこで一旦停止。
        /// </summary>
        /// <param name="item">セグメントリスト要素</param>
        /// <returns>ストップマーカーか (bool 値)</returns>
        private static bool HasStopMarker(SegmentListItem item)
        {
            uint value = 0;
            if (item.Segment.SourceSubIndex is not null)
            {
                value = item.Segment.SourceSubIndex.ControlWord;
            }
            else if (item.Segment.SourceIndex is not null)
            {
                value = item.Segment.SourceIndex.ControlWord;
            }

            return value < 0x10000000U;
        }
    }
}
