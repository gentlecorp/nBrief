using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;

namespace nBriefcase
{
    public partial class Form1 : Form
    {
        private TextBox logBox;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer driveTimer;
        private string[] previousDrives;
        private const string ContainerFileName = "MyBriefcase.nbcase";
        private string foundContainerPath = null;
        private const string TestVhdxPath =@"C:\nBrief\TestBriefCase.vhdx";
        private string foundVhdxPath = null;
        private System.Windows.Forms.Timer syncPollTimer;

        private Dictionary<string, string> lastFileSnapshots =
        new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> pendingFileSnapshots =
        new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase);

        public Form1()
        {
            InitializeComponent();
            syncPollTimer = new System.Windows.Forms.Timer();
            syncPollTimer.Interval = 2000;
            syncPollTimer.Tick += SyncPollTimer_Tick;
            syncPollTimer.Start();

            Text = "Briefcase";
            ClientSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterScreen;

            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Dock = DockStyle.Fill;
            logBox.Font = new Font("Consolas", 10);

            Controls.Add(logBox);

            AddLog("Briefcase 시작");
            AddLog(".NET Framework 4.7.2");
            AddLog("디버그 로그 초기화 완료");

            // 시스템 트레이 메뉴
            trayMenu = new ContextMenuStrip();

            trayMenu.Items.Add("Briefcase 열기", null, (s, e) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            });

            trayMenu.Items.Add("VHDX 연결", null, (s, e) =>
            {
                RunDiskPart(true);
            });

            trayMenu.Items.Add("VHDX 분리", null, (s, e) =>
            {
                RunDiskPart(false);
            });

            trayMenu.Items.Add("로그 기록", null, (s, e) =>
            {
                AddLog("트레이 메뉴에서 로그 기록");
            });

            trayMenu.Items.Add("종료", null, (s, e) =>
            {
                AddLog("프로그램 종료 요청");
                trayIcon.Visible = false;
                trayIcon.Dispose();
                Application.Exit();
            });

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("새 서류가방 만들기...", null,
            CreateBriefcaseMenu_Click);
            ToolStripMenuItem registerFileMenuItem =
            new ToolStripMenuItem("파일 등록...");
            ToolStripMenuItem syncMenuItem =
            new ToolStripMenuItem("동기화 실행");

            syncMenuItem.Click += SyncMenuItem_Click;

            trayMenu.Items.Add(syncMenuItem);

            registerFileMenuItem.Click += RegisterFileMenu_Click;

            trayMenu.Items.Add(registerFileMenuItem);

            // 트레이 아이콘
            trayIcon = new NotifyIcon();
            trayIcon.Text = "Briefcase";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;

            AddLog("시스템 트레이 초기화 완료");
            // 현재 드라이브 목록 저장
            previousDrives = Directory.GetLogicalDrives();

            // 프로그램 시작 시 이미 연결된 이동식 드라이브 검사
            ScanExistingDrives();

            // 2초마다 드라이브 변경 확인
            driveTimer = new System.Windows.Forms.Timer();
            driveTimer.Interval = 2000;
            driveTimer.Tick += DriveTimer_Tick;
            driveTimer.Start();

            AddLog("드라이브 감지 시작");
        }

        private void AddLog(string message)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => AddLog(message)));
                }
                catch (InvalidOperationException)
                {
                    // 창이 종료되는 중이면 무시
                }

                return;
            }

            string line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            logBox.AppendText(line + Environment.NewLine);

            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "nBriefcase",
                    "Logs");

                Directory.CreateDirectory(logDirectory);

                string logFile = Path.Combine(
                    logDirectory,
                    $"Briefcase-{DateTime.Now:yyyy-MM-dd}.log");

                File.AppendAllText(
                    logFile,
                    line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "로그 파일 저장 실패: " + ex.Message);
            }
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Name = "Form1";

            ResumeLayout(false);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x0219;
            const int DBT_DEVICEARRIVAL = 0x8000;
            const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
            const int DBT_DEVTYP_VOLUME = 0x00000002;

            if (m.Msg == WM_DEVICECHANGE)
            {
                int eventType = m.WParam.ToInt32();

                if ((eventType == DBT_DEVICEARRIVAL ||
                     eventType == DBT_DEVICEREMOVECOMPLETE) &&
                    m.LParam != IntPtr.Zero)
                {
                    int deviceType = System.Runtime.InteropServices
                        .Marshal.ReadInt32(m.LParam);

                    if (deviceType == DBT_DEVTYP_VOLUME)
                    {
                        if (eventType == DBT_DEVICEARRIVAL)
                            AddLog("저장장치 연결 감지됨");
                        else
                            AddLog("저장장치 제거 감지됨");
                    }
                }
            }

            base.WndProc(ref m);
        }
        private void ScanExistingDrives()
        {
            foreach (string drive in Directory.GetLogicalDrives())
            {
                try
                {
                    DriveInfo info = new DriveInfo(drive);

                    if (info.DriveType == DriveType.Removable &&
                        info.IsReady)
                    {
                        AddLog("기존 이동식 드라이브 발견: " + drive);
                        SearchContainerOnDrive(drive);
                    }
                }
                catch (Exception ex)
                {
                    AddLog("드라이브 확인 실패 (" + drive + "): " + ex.Message);
                }
            }
        }

        private void SearchContainerOnDrive(string drive)
        {
            try
            {
                string[] containerFiles;

                try
                {
                    containerFiles = Directory.GetFiles(
                        drive,
                        "*.nbrief",
                        SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    AddLog("서류가방 검색 실패: " + ex.Message);
                    return;
                }

                if (containerFiles.Length == 0)
                {
                    AddLog("서류가방 설정 파일을 찾을 수 없습니다: " + drive);
                    return;
                }

                if (containerFiles.Length > 1)
                {
                    AddLog("서류가방 설정 파일이 여러 개 있습니다: " + drive);
                    AddLog("현재는 하나의 USB에 서류가방 하나만 두는 방식을 권장합니다.");
                    return;
                }

                string containerPath = containerFiles[0];
                if (!File.Exists(containerPath))
                    return;

                string[] lines = File.ReadAllLines(containerPath);

                string formatVersion = null;
                string containerId = null;
                string diskFile = null;
                string diskFormat = null;

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();

                    if (trimmedLine.Length == 0 ||
                        trimmedLine.StartsWith("#"))
                        continue;

                    int separator = trimmedLine.IndexOf('=');

                    if (separator <= 0)
                        continue;

                    string key = trimmedLine
                        .Substring(0, separator)
                        .Trim();

                    string value = trimmedLine
                        .Substring(separator + 1)
                        .Trim();

                    switch (key)
                    {
                        case "formatVersion":
                            formatVersion = value;
                            break;

                        case "containerId":
                            containerId = value;
                            break;

                        case "diskFile":
                            diskFile = value;
                            break;

                        case "diskFormat":
                            diskFormat = value;
                            break;
                    }
                }

                AddLog("설정 파일 발견: " + containerPath);

                if (formatVersion != "1")
                {
                    AddLog("오류: 지원하지 않는 설정 파일 버전");
                    return;
                }

                Guid parsedId;

                if (!Guid.TryParse(containerId, out parsedId))
                {
                    AddLog("오류: containerId가 올바른 GUID가 아님");
                    return;
                }

                if (string.IsNullOrWhiteSpace(diskFile) ||
                    Path.GetFileName(diskFile) != diskFile ||
                    !string.Equals(
                        Path.GetExtension(diskFile),
                        ".vhdx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddLog("오류: diskFile 설정이 올바르지 않음");
                    return;
                }

                if (!string.Equals(
                    diskFormat,
                    "VHDX",
                    StringComparison.OrdinalIgnoreCase))
                {
                    AddLog("오류: 지원하지 않는 디스크 형식");
                    return;
                }

                string vhdxPath = Path.Combine(
                    Path.GetDirectoryName(containerPath),
                    diskFile);

                if (!File.Exists(vhdxPath))
                {
                    AddLog("오류: VHDX 파일을 찾을 수 없음");
                    AddLog("예상 경로: " + vhdxPath);
                    return;
                }

                foundContainerPath = containerPath;
                foundVhdxPath = vhdxPath;

                AddLog("설정 파일 검증 완료");
                AddLog("컨테이너 ID: " + parsedId);
                AddLog("VHDX 경로: " + foundVhdxPath);
                HideVhdxFile();

                AddLog("브리프케이스 자동 연결을 시작합니다.");
                RunDiskPart(true);
            }
            catch (Exception ex)
            {
                AddLog("설정 파일 검사 실패: " + ex.Message);
            }
        }

        private void DriveTimer_Tick(object sender, EventArgs e)
        {
            string[] currentDrives = Directory.GetLogicalDrives();

            foreach (string drive in currentDrives)
            {
                if (Array.IndexOf(previousDrives, drive) < 0)
                {
                    AddLog("새 드라이브 감지: " + drive);

                    try
                    {
                        DriveInfo info = new DriveInfo(drive);

                        if (info.IsReady)
                        {
                            SearchContainerOnDrive(drive);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog("새 드라이브 확인 실패: " + ex.Message);
                    }
                }
            }

            foreach (string drive in previousDrives)
            {
                if (Array.IndexOf(currentDrives, drive) < 0)
                {
                    AddLog("드라이브 제거 감지: " + drive);
                    trayIcon.BalloonTipTitle = "nBriefcase";
                    trayIcon.BalloonTipText =
                        "USB가 제거되었습니다. 브리프케이스 연결 상태를 확인하세요.";
                    trayIcon.BalloonTipIcon = ToolTipIcon.Warning;
                    trayIcon.ShowBalloonTip(5000);
                }
            }

            previousDrives = currentDrives;
        }

        private void HideVhdxFile()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(foundVhdxPath))
                {
                    AddLog("숨김 처리 실패: 감지된 VHDX가 없음");
                    return;
                }

                if (!File.Exists(foundVhdxPath))
                {
                    AddLog("숨김 처리 실패: 파일을 찾을 수 없음");
                    return;
                }

                FileAttributes attributes =
                    File.GetAttributes(foundVhdxPath);

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    AddLog("숨김 처리 실패: 파일 경로가 폴더임");
                    return;
                }

                File.SetAttributes(
                    foundVhdxPath,
                    attributes | FileAttributes.Hidden);

                AddLog("VHDX 숨김 처리 완료: " + foundVhdxPath);
            }
            catch (Exception ex)
            {
                AddLog("VHDX 숨김 처리 실패: " + ex.Message);
            }
        }

        private bool? GetVhdxAttachedState()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(foundVhdxPath) ||
                    !File.Exists(foundVhdxPath))
                {
                    AddLog("상태 확인 실패: VHDX 파일이 없음");
                    return null;
                }

                string escapedPath = foundVhdxPath.Replace("'", "''");

                string command =
                    "$d = Get-DiskImage -ImagePath '" +
                    escapedPath +
                    "' -ErrorAction Stop; " +
                    "if ($d.Attached) { 'TRUE' } else { 'FALSE' }";

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");

                psi.Arguments =
                    "-NoProfile -NonInteractive -Command \"" +
                    command + "\"";

                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    string error = process.StandardError.ReadToEnd().Trim();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        AddLog("상태 확인 실패: " + error);
                        return null;
                    }

                    if (output.EndsWith("TRUE",
                        StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (output.EndsWith("FALSE",
                        StringComparison.OrdinalIgnoreCase))
                        return false;

                    AddLog("상태 확인 실패: 예상하지 못한 응답");
                    return null;
                }
            }
            catch (Exception ex)
            {
                AddLog("상태 확인 실패: " + ex.Message);
                return null;
            }
        }

        private void RunDiskPart(bool attach)
        {
            string scriptPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(foundVhdxPath))
                {
                    AddLog("오류: 연결할 브리프케이스가 감지되지 않음");
                    return;
                }

                if (!File.Exists(foundVhdxPath))
                {
                    AddLog("오류: VHDX 파일을 찾을 수 없음: " + foundVhdxPath);
                    return;
                }

                string operation = attach ? "연결" : "분리";
                AddLog("VHDX " + operation + " 작업 시작");

                bool? currentState = GetVhdxAttachedState();

                if (!currentState.HasValue)
                {
                    AddLog("작업 취소: VHDX 상태를 확인할 수 없음");
                    return;
                }

                if (attach && currentState.Value)
                {
                    AddLog("VHDX가 이미 연결되어 있음");
                    return;
                }

                if (!attach && !currentState.Value)
                {
                    AddLog("VHDX가 이미 분리되어 있음");
                    return;
                }


                string script = attach
                  ? "select vdisk file=\"" + foundVhdxPath + "\"\r\nattach vdisk\r\nexit\r\n"
                  : "select vdisk file=\"" + foundVhdxPath + "\"\r\ndetach vdisk\r\nexit\r\n";
                AddLog("연결 대상 VHDX 경로: [" + foundVhdxPath + "]");
                AddLog("파일 존재 여부: " + File.Exists(foundVhdxPath));

                scriptPath = Path.Combine(
                    Path.GetTempPath(),
                    "nBriefcase-" + Guid.NewGuid().ToString("N") + ".txt");

                File.WriteAllText(scriptPath, script, Encoding.Default);
                AddLog("DiskPart 스크립트 경로: " + scriptPath);
                AddLog("DiskPart 스크립트 내용:");
                AddLog(File.ReadAllText(scriptPath));

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.System),
                    "diskpart.exe");

                psi.Arguments = "/s \"" + scriptPath + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(output))
                        AddLog("DiskPart 출력: " + output.Trim());

                    if (!string.IsNullOrWhiteSpace(error))
                        AddLog("DiskPart 오류: " + error.Trim());

                    AddLog("DiskPart 종료 코드: " + process.ExitCode);


                    bool? finalState = GetVhdxAttachedState();


                    if (finalState.HasValue &&
                        finalState.Value == attach)
                    {
                        AddLog(attach
                            ? "VHDX 연결 확인 완료"
                            : "VHDX 분리 확인 완료");
                        ShowTrayNotification(
                            "브리프케이스 분리 완료",
                            "서류가방 드라이브가 안전하게 분리되었습니다.",
                            ToolTipIcon.Info);

                        if (attach)
                        {
                            ShowTrayNotification(
                                "브리프케이스 연결 완료",
                                "서류가방 드라이브가 연결되었습니다.",
                                ToolTipIcon.Info);
                        }
                    }
                    else
                    {
                        AddLog("VHDX 상태가 요청한 결과와 다름. 로그를 확인할 것");
                    }


                }
            }
            catch (Exception ex)
            {
                AddLog("VHDX 작업 실패: " + ex.Message);
            }
            finally
            {
                if (scriptPath != null)
                {
                    try
                    {
                        if (File.Exists(scriptPath))
                            File.Delete(scriptPath);
                    }
                    catch (Exception ex)
                    {
                        AddLog("임시 스크립트 정리 실패: " + ex.Message);
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                AddLog("창 숨김 - 트레이에서 계속 실행 중");
                return;
            }

            base.OnFormClosing(e);
        }
        private void OnDeviceArrival(object sender, EventArgs e)
        {
            AddLog("장치 연결 감지됨");
        }

        private void OnDeviceRemoveComplete(object sender, EventArgs e)
        {
            AddLog("장치 제거 감지됨");
        }
        protected override void Dispose(bool disposing)
        {
            if (syncPollTimer != null)
            {
                syncPollTimer.Stop();
                syncPollTimer.Dispose();
                syncPollTimer = null;
            }
            if (disposing)
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }

                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                    trayMenu = null;
                }
            }

            base.Dispose(disposing);
        }

        private void CreateBriefcaseMenu_Click(object sender, EventArgs e)
        {
            using (CreateBriefcaseForm form = new CreateBriefcaseForm())
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return;

                CreateBriefcaseFiles(form.BriefcaseName,
                                     form.DestinationPath,
                                     form.CapacityGB);
            }
        }

        private void SyncMenuItem_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                dialog.Description =
                    "연결된 서류가방 VHDX의 루트 드라이브를 선택하세요.";

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                string briefcaseRoot = dialog.SelectedPath;
                string manifestPath =
                    SyncManager.GetManifestPath(briefcaseRoot);

                if (!File.Exists(manifestPath))
                {
                    MessageBox.Show(
                        "이 위치에 동기화 기록이 없습니다.\r\n"
                        + "서류가방 드라이브의 루트를 선택했는지 확인하세요.",
                        "nBriefcase",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                try
                {
                    SyncManifest manifest =
                        SyncManager.LoadManifest(briefcaseRoot);

                    if (manifest.Records.Count == 0)
                    {
                        MessageBox.Show(
                            "동기화할 파일이 없습니다.",
                            "nBriefcase",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        return;
                    }

                    int successCount = 0;
                    int unchangedCount = 0;
                    int conflictCount = 0;
                    int missingCount = 0;
                    int errorCount = 0;

                    foreach (SyncRecord record in manifest.Records)
                    {
                        try
                        {
                            SyncItemResult result =
                                SyncManager.SynchronizeRecord(
                                    briefcaseRoot,
                                    manifest,
                                    record);

                            AddLog(
                                Path.GetFileName(record.SourcePath)
                                + ": " + result.Message);

                            switch (result.Status)
                            {
                                case SyncStatus.SourceCopiedToBriefcase:
                                case SyncStatus.BriefcaseCopiedToSource:
                                    successCount++;
                                    break;

                                case SyncStatus.NoChange:
                                    unchangedCount++;
                                    break;

                                case SyncStatus.Conflict:
                                    conflictCount++;
                                    break;

                                case SyncStatus.SourceMissing:
                                case SyncStatus.BriefcaseMissing:
                                case SyncStatus.BothMissing:
                                    missingCount++;
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;

                            AddLog(
                                "동기화 오류: "
                                + record.SourcePath
                                + " / " + ex.Message);
                        }
                    }

                    string summary =
                        "동기화 작업이 끝났습니다.\r\n\r\n"
                        + "복사 및 반영: " + successCount + "개\r\n"
                        + "변경 없음: " + unchangedCount + "개\r\n"
                        + "충돌: " + conflictCount + "개\r\n"
                        + "파일 누락: " + missingCount + "개\r\n"
                        + "오류: " + errorCount + "개";

                    AddLog(summary.Replace("\r\n", " | "));

                    MessageBox.Show(
                        summary,
                        "nBriefcase 동기화 결과",
                        MessageBoxButtons.OK,
                        conflictCount > 0 || errorCount > 0
                            ? MessageBoxIcon.Warning
                            : MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AddLog("동기화 작업 실패: " + ex.Message);

                    MessageBox.Show(
                        "동기화를 시작하지 못했습니다.\r\n\r\n"
                        + ex.Message,
                        "nBriefcase",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
        private string FindBriefcaseRoot()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                        continue;

                    if (!string.Equals(
                        drive.VolumeLabel,
                        "nBriefcase",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.Equals(
                        drive.DriveFormat,
                        "NTFS",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    return drive.RootDirectory.FullName;
                }
                catch (IOException)
                {
                    // 검사 중 분리된 드라이브는 건너뜀
                }
                catch (UnauthorizedAccessException)
                {
                    // 접근할 수 없는 드라이브는 건너뜀
                }
            }

            return null;
        }
        private void SyncPollTimer_Tick(object sender, EventArgs e)
        {
            string briefcaseRoot = FindBriefcaseRoot();

            if (string.IsNullOrWhiteSpace(briefcaseRoot))
                return;

            string manifestPath =
                SyncManager.GetManifestPath(briefcaseRoot);

            if (!File.Exists(manifestPath))
                return;

            try
            {
                SyncManifest manifest =
                    SyncManager.LoadManifest(briefcaseRoot);

                foreach (SyncRecord record in manifest.Records)
                {
                    string key = record.SourcePath + "|" + record.RelativePath;

                    string fingerprint =
                        GetRecordFingerprint(briefcaseRoot, record);

                    string previousFingerprint;

                    // 처음 발견한 파일은 기준 상태를 저장하고
                    // 다음 검사 때까지 기다린다.
                    if (!lastFileSnapshots.TryGetValue(
                        key, out previousFingerprint))
                    {
                        lastFileSnapshots[key] = fingerprint;
                        pendingFileSnapshots[key] = fingerprint;
                        continue;
                    }

                    // 파일 상태가 바뀌면 안정화 대기를 다시 시작한다.
                    if (previousFingerprint != fingerprint)
                    {
                        lastFileSnapshots[key] = fingerprint;
                        pendingFileSnapshots[key] = fingerprint;
                        continue;
                    }

                    string pendingFingerprint;

                    // 두 번의 검사에서 상태가 같으면 동기화를 실행한다.
                    if (!pendingFileSnapshots.TryGetValue(
                        key, out pendingFingerprint))
                        continue;

                    if (pendingFingerprint != fingerprint)
                    {
                        pendingFileSnapshots[key] = fingerprint;
                        continue;
                    }

                    try
                    {
                        SyncItemResult result =
                            SyncManager.SynchronizeRecord(
                                briefcaseRoot,
                                manifest,
                                record);

                        if (result.Status != SyncStatus.NoChange)
                        {
                            AddLog(
                                "[자동 동기화] "
                                + Path.GetFileName(record.SourcePath)
                                + ": " + result.Message);
                        }

                        pendingFileSnapshots.Remove(key);

                        // 동기화로 파일 상태가 바뀐 경우를 반영한다.
                        lastFileSnapshots[key] =
                            GetRecordFingerprint(briefcaseRoot, record);
                    }
                    catch (Exception ex)
                    {
                        AddLog(
                            "[자동 동기화 오류] "
                            + Path.GetFileName(record.SourcePath)
                            + ": " + ex.Message);

                        // 오류가 발생하면 다음 검사에서 재시도할 수 있게
                        // 대기 상태를 유지한다.
                        lastFileSnapshots[key] = fingerprint;
                        pendingFileSnapshots[key] = fingerprint;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("[자동 동기화 검사 오류] " + ex.Message);
            }
        }
        private string GetRecordFingerprint(
    string briefcaseRoot,
    SyncRecord record)
        {
            string sourcePath = record.SourcePath;
            string copyPath = Path.Combine(
                briefcaseRoot,
                record.RelativePath);

            return GetFileFingerprint(sourcePath)
                + "|" + GetFileFingerprint(copyPath);
        }

        private string GetFileFingerprint(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return "MISSING";

                FileInfo info = new FileInfo(path);

                return info.Length.ToString()
                    + ":" + info.LastWriteTimeUtc.Ticks.ToString();
            }
            catch (IOException)
            {
                return "UNAVAILABLE";
            }
            catch (UnauthorizedAccessException)
            {
                return "UNAVAILABLE";
            }
        }
        private void CreateBriefcaseFiles(
            string name, string destination, int capacityGB)
        {
            string vhdxPath = Path.Combine(destination, "." + name + ".vhdx");
            string configPath = Path.Combine(destination, name + ".nbrief");
            string tempConfigPath = configPath + ".tmp";

            string diskpartPath = Path.Combine(
                Path.GetTempPath(),
                "nBriefcase-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                if (File.Exists(vhdxPath) ||
                    File.Exists(configPath) ||
                    File.Exists(tempConfigPath))
                {
                    MessageBox.Show(
                        "같은 이름의 파일이 이미 있습니다.\r\n기존 파일은 덮어쓰지 않습니다.",
                        "생성 취소",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DriveInfo drive = new DriveInfo(
                    Path.GetPathRoot(Path.GetFullPath(destination)));

                if (!drive.IsReady)
                    throw new IOException("저장 장치에 접근할 수 없습니다.");

                if (drive.DriveFormat.Equals(
                    "FAT32", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        "FAT32에서는 4GB를 초과하는 단일 파일을 저장할 수 없습니다.\r\n" +
                        "대용량 VHDX를 사용하려면 NTFS 또는 exFAT 등의 " +
                        "대용량 파일 지원 파일시스템을 사용하세요.");
                }

                if (drive.AvailableFreeSpace < 1024L * 1024L * 1024L)
                {
                    throw new IOException(
                        "저장 장치의 여유 공간이 1GB 미만입니다.\r\n" +
                        "공간을 확보한 후 다시 시도하세요.");
                }

                long maximumMB = (long)capacityGB * 1024L;

                AddLog("동적 VHDX 생성 시작: " + vhdxPath);
                AddLog("최대 용량: " + capacityGB + "GB");

                string createCommands =
                    "create vdisk file=\"" + vhdxPath +
                    "\" maximum=" + maximumMB + " type=expandable\r\n" +
                    "exit\r\n";

                File.WriteAllText(
                    diskpartPath, createCommands, Encoding.Default);

                string createOutput;
                int createExitCode = RunDiskPartScriptFile(
                    diskpartPath, out createOutput);

                AddLog("VHDX 생성 DiskPart 종료 코드: " + createExitCode);
                AddLog(createOutput);

                if (createExitCode != 0 || !File.Exists(vhdxPath))
                {
                    throw new IOException(
                        "VHDX 생성에 실패했습니다. 관리자 권한과 저장 공간을 확인하세요.");
                }

                // 새로 만든 VHDX만 연결하고 초기화한다.
                string formatCommands =
                    "select vdisk file=\"" + vhdxPath + "\"\r\n" +
                    "attach vdisk\r\n" +
                    "convert gpt\r\n" +
                    "create partition primary\r\n" +
                    "format fs=ntfs quick label=\"nBriefcase\"\r\n" +
                    "assign\r\n" +
                    "detach vdisk\r\n" +
                    "exit\r\n";

                File.WriteAllText(
                    diskpartPath, formatCommands, Encoding.Default);

                string formatOutput;
                int formatExitCode = RunDiskPartScriptFile(
                    diskpartPath, out formatOutput);

                AddLog("VHDX 초기화 DiskPart 종료 코드: " + formatExitCode);
                AddLog(formatOutput);

                if (formatExitCode != 0)
                {
                    throw new IOException(
                        "VHDX 초기화 명령 실행에 실패했습니다. " +
                        "로그를 확인하고 디스크 상태를 점검하세요.");
                }

                bool? attached = GetVhdxAttachedStateForPath(vhdxPath);

                if (attached != false)
                {
                    throw new IOException(
                        "VHDX가 분리되었는지 확인할 수 없습니다. " +
                        "안전을 위해 설정 파일 생성을 중단합니다.");
                }

                // 디스크 분리가 확인된 후에만 숨김 처리 및 설정 파일 생성
                FileAttributes attributes = File.GetAttributes(vhdxPath);
                File.SetAttributes(
                    vhdxPath, attributes | FileAttributes.Hidden);

                string containerId = Guid.NewGuid().ToString();

                string config =
                    "formatVersion=1\r\n" +
                    "containerId=" + containerId + "\r\n" +
                    "diskFile=." + name + ".vhdx\r\n" +
                    "diskFormat=VHDX\r\n";

                File.WriteAllText(
                    tempConfigPath, config, new UTF8Encoding(false));

                File.Move(tempConfigPath, configPath);

                AddLog("서류가방 생성 완료: " + configPath);
                AddLog("VHDX 파일: " + vhdxPath);

                MessageBox.Show(
                    "서류가방 파일 생성이 완료되었습니다.\r\n\r\n" +
                    configPath + "\r\n" + vhdxPath +
                    "\r\n\r\nVHDX는 동적 할당 방식입니다.",
                    "nBriefcase",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AddLog("서류가방 생성 오류: " + ex.Message);

                MessageBox.Show(
                    "서류가방 생성에 실패했습니다.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\n생성된 VHDX가 있다면 삭제하기 전에 " +
                    "디스크가 연결되어 있는지 먼저 확인하세요.",
                    "생성 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    if (File.Exists(diskpartPath))
                        File.Delete(diskpartPath);
                }
                catch (Exception ex)
                {
                    AddLog("임시 스크립트 정리 실패: " + ex.Message);
                }
            }
        }

        private int RunDiskPartScriptFile(
            string scriptPath, out string output)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.System),
                        "diskpart.exe"),
                    Arguments = "/s \"" + scriptPath + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.Start();

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit();

                output = stdout;

                if (!string.IsNullOrWhiteSpace(stderr))
                    output += Environment.NewLine + stderr;

                return process.ExitCode;
            }
        }
        private void RegisterFileMenu_Click(
    object sender,
    EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "서류가방에 등록할 파일 선택";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                using (FolderBrowserDialog folderDialog =
                    new FolderBrowserDialog())
                {
                    folderDialog.Description =
                        "연결된 서류가방 드라이브의 루트를 선택하세요.";

                    if (folderDialog.ShowDialog() != DialogResult.OK)
                        return;

                    string briefcaseRoot =
                        folderDialog.SelectedPath;

                    try
                    {
                        SyncManifest manifest =
                            SyncManager.LoadManifest(briefcaseRoot);

                        SyncItemResult result =
                            SyncManager.RegisterFile(
                                dialog.FileName,
                                briefcaseRoot,
                                manifest);

                        AddLog(result.Message);

                        MessageBox.Show(
                            result.Message,
                            "nBriefcase",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        AddLog("파일 등록 실패: " + ex.Message);

                        MessageBox.Show(
                            "파일을 등록하지 못했습니다.\r\n\r\n"
                            + ex.Message,
                            "nBriefcase",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }

        private bool? GetVhdxAttachedStateForPath(string path)
        {
            string escapedPath = path.Replace("'", "''");

            string script =
                "$ErrorActionPreference='Stop'; " +
                "$d = Get-DiskImage -ImagePath '" + escapedPath + "'; " +
                "if ($d.Attached) { 'TRUE' } else { 'FALSE' }";

            string powershellPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = powershellPath,
                        Arguments = "-NoProfile -NonInteractive -Command \"" +
                            script.Replace("\"", "\\\"") + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    process.Start();

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                        return null;

                    if (output.EndsWith("TRUE", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (output.EndsWith("FALSE", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private void ShowTrayNotification(
    string title,
    string message,
    ToolTipIcon icon)
        {
            if (trayIcon == null)
                return;

            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(5000);
        }
    }
}
