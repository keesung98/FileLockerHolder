using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileLockHolder
{
    public partial class MainWindow : Window
    {
        private const string MesLogRoot = @"D:\APInovationLog\MES\LOG";

        private FileStream? _holdStream;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly DispatcherTimer _timer;
        private double _holdSeconds;
        private bool _infinite;
        private string _lockedPath = "";

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _timer.Tick += Timer_Tick;

            txtPath.Text = GetCurrentMesLogPath();
            SetIdleStatus();
        }

        #region 파일 위치

        private static string GetCurrentMesLogPath()
        {
            DateTime now = DateTime.Now;
            return string.Format(@"{0}\{1}\{2}\{3}\{4}.txt",
                MesLogRoot, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), now.ToString("yyyyMMddHH"));
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "잡아둘 파일 선택",
                Filter = "모든 파일 (*.*)|*.*|텍스트 파일 (*.txt)|*.txt",
                CheckFileExists = true
            };

            string current = txtPath.Text.Trim();
            string? dir = SafeGetDirectory(current);
            if (dir != null && Directory.Exists(dir))
                dlg.InitialDirectory = dir;

            if (dlg.ShowDialog(this) == true)
                txtPath.Text = dlg.FileName;
        }

        private void btnMesPath_Click(object sender, RoutedEventArgs e)
        {
            txtPath.Text = GetCurrentMesLogPath();
            if (!File.Exists(txtPath.Text))
                AddLog("현재 시간대 MES 로그 파일이 아직 없습니다: " + txtPath.Text);
        }

        private void txtPath_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void txtPath_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                txtPath.Text = files[0];
        }

        private void txtPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFileInfo();
        }

        private void UpdateFileInfo()
        {
            if (lblFileInfo == null) return;

            string path = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                lblFileInfo.Text = "";
                return;
            }

            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    lblFileInfo.Text = "파일이 존재하지 않습니다.";
                    lblFileInfo.Foreground = Brushes.IndianRed;
                    return;
                }

                lblFileInfo.Text = string.Format("크기: {0:N0} bytes   수정: {1:yyyy-MM-dd HH:mm:ss}   속성: {2}",
                    fi.Length, fi.LastWriteTime, fi.Attributes);
                lblFileInfo.Foreground = fi.IsReadOnly ? Brushes.DarkOrange : Brushes.Gray;
            }
            catch (Exception ex)
            {
                lblFileInfo.Text = "경로 오류: " + ex.Message;
                lblFileInfo.Foreground = Brushes.IndianRed;
            }
        }

        private static string? SafeGetDirectory(string path)
        {
            try { return string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetDirectoryName(path); }
            catch { return null; }
        }

        #endregion

        #region 잠금 설정

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sec)
            {
                txtSeconds.Text = sec;
                chkInfinite.IsChecked = false;
            }
        }

        private void chkInfinite_Changed(object sender, RoutedEventArgs e)
        {
            txtSeconds.IsEnabled = chkInfinite.IsChecked != true;
        }

        private FileShare GetSelectedShare()
        {
            string tag = (cmbShare.SelectedItem as ComboBoxItem)?.Tag as string ?? "Read";
            return tag switch
            {
                "None" => FileShare.None,
                "ReadWrite" => FileShare.ReadWrite,
                _ => FileShare.Read,
            };
        }

        private static bool TryParseSeconds(string text, out double seconds)
        {
            text = text.Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }

        #endregion

        #region 잠금 / 해제

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            string path = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "파일 위치를 입력하세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _infinite = chkInfinite.IsChecked == true;
            if (!_infinite)
            {
                if (!TryParseSeconds(txtSeconds.Text, out _holdSeconds) || _holdSeconds <= 0)
                {
                    MessageBox.Show(this, "유지 시간은 0보다 큰 숫자(초)로 입력하세요.", "확인",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            FileShare share = GetSelectedShare();

            try
            {
                // 기존 파일만 대상 (없는 파일을 새로 만들지 않음)
                _holdStream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
            }
            catch (Exception ex)
            {
                _holdStream = null;
                SetFailStatus(ex);
                AddLog(string.Format("잠금 실패 - {0}", DescribeException(ex)));
                return;
            }

            _lockedPath = path;
            _stopwatch.Restart();
            _timer.Start();

            SetLockedStatus(share);
            AddLog(string.Format("잠금 시작 - FileAccess.Read / FileShare.{0} / {1} / {2}",
                share, _infinite ? "수동 해제까지" : _holdSeconds.ToString("0.###") + "초", path));
        }

        private void btnRelease_Click(object sender, RoutedEventArgs e)
        {
            Release("사용자 해제");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            double elapsed = _stopwatch.Elapsed.TotalSeconds;

            if (!_infinite && elapsed >= _holdSeconds)
            {
                Release("시간 만료");
                return;
            }

            UpdateElapsed(elapsed);
        }

        private void Release(string reason)
        {
            if (_holdStream == null) return;

            _timer.Stop();
            _stopwatch.Stop();

            try { _holdStream.Dispose(); }
            catch (Exception ex) { AddLog("해제 중 오류 - " + DescribeException(ex)); }
            _holdStream = null;

            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            AddLog(string.Format("잠금 해제 ({0}) - 실제 유지 시간 {1:0.000}초", reason, elapsed));

            SetIdleStatus();
            lblElapsed.Text = string.Format("{0:0.000} 초", elapsed);
            lblStatusDetail.Text = string.Format("마지막 잠금: {0:0.000}초 유지 후 해제 ({1})", elapsed, reason);
            UpdateFileInfo();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Release("프로그램 종료");
        }

        #endregion

        #region 쓰기 테스트

        /// <summary>
        /// MESManager 의 로그 쓰기와 같은 방식으로 열어보기만 하고 바로 닫는다. (내용은 쓰지 않음)
        /// </summary>
        private void btnWriteTest_Click(object sender, RoutedEventArgs e)
        {
            string path = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                }
                AddLog("쓰기 열기 테스트: 성공 (쓰기 가능 상태)");
            }
            catch (Exception ex)
            {
                AddLog("쓰기 열기 테스트: 실패 - " + DescribeException(ex));
            }
        }

        #endregion

        #region 상태 표시

        private void SetIdleStatus()
        {
            ledStatus.Fill = Brushes.Gray;
            lblStatus.Text = "대기 중";
            lblStatus.Foreground = Brushes.Black;
            lblStatusDetail.Text = "파일을 잡고 있지 않습니다.";
            lblRemain.Text = "";
            progress.Value = 0;
            progress.IsIndeterminate = false;
            SetInputEnabled(true);
        }

        private void SetLockedStatus(FileShare share)
        {
            ledStatus.Fill = Brushes.LimeGreen;
            lblStatus.Text = "파일 잡는 중";
            lblStatus.Foreground = Brushes.ForestGreen;
            lblStatusDetail.Text = string.Format("FileShare.{0} - {1}", share,
                share == FileShare.ReadWrite ? "다른 쪽 쓰기 허용 (차단 안 됨)" :
                share == FileShare.None ? "다른 쪽 읽기/쓰기 차단" : "다른 쪽 쓰기 차단");
            progress.IsIndeterminate = _infinite;
            progress.Value = 0;
            SetInputEnabled(false);
            UpdateElapsed(0);
        }

        private void SetFailStatus(Exception ex)
        {
            ledStatus.Fill = Brushes.Red;
            lblStatus.Text = "잠금 실패";
            lblStatus.Foreground = Brushes.Red;
            lblStatusDetail.Text = DescribeException(ex);
            lblElapsed.Text = "0.000 초";
            lblRemain.Text = "";
            progress.Value = 0;
            progress.IsIndeterminate = false;
        }

        private void UpdateElapsed(double elapsed)
        {
            lblElapsed.Text = string.Format("{0:0.000} 초", elapsed);

            if (_infinite)
            {
                lblRemain.Text = "수동 해제까지 유지";
            }
            else
            {
                double remain = Math.Max(0, _holdSeconds - elapsed);
                lblRemain.Text = string.Format("남은 시간 {0:0.000} / {1:0.###} 초", remain, _holdSeconds);
                progress.Value = Math.Min(1.0, elapsed / _holdSeconds);
            }
        }

        private void SetInputEnabled(bool enabled)
        {
            txtPath.IsEnabled = enabled;
            btnBrowse.IsEnabled = enabled;
            btnMesPath.IsEnabled = enabled;
            txtSeconds.IsEnabled = enabled && chkInfinite.IsChecked != true;
            chkInfinite.IsEnabled = enabled;
            cmbShare.IsEnabled = enabled;
            btnStart.IsEnabled = enabled;
            btnRelease.IsEnabled = !enabled;
        }

        private static string DescribeException(Exception ex)
        {
            return string.Format("{0} (HResult=0x{1:X8}): {2}", ex.GetType().Name, ex.HResult, ex.Message);
        }

        #endregion

        #region 로그

        private void AddLog(string message)
        {
            string line = string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, message);
            lstLog.Items.Add(line);
            lstLog.ScrollIntoView(line);
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            lstLog.Items.Clear();
        }

        private void btnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (object item in lstLog.Items)
                sb.AppendLine(item.ToString());

            if (sb.Length > 0)
                Clipboard.SetText(sb.ToString());
        }

        #endregion
    }
}
