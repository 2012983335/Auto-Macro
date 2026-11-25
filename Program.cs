using System.Diagnostics;

namespace BAAxis;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(args.Skip(1).ToArray()); return; }
        if (args.Contains("--ui-self-test")) { MainForm.CheckEditor(); return; }
        if (args.Contains("--large-axis-test")) { MainForm.CheckLargeAxis(); return; }
        Application.Run(new MainForm());
    }
}

internal sealed partial class MainForm : Form
{
    private AxisDocument document = new();
    private readonly TextBox total = new() { Text = "03:00.000", Width = 95 };
    private readonly TextBox end = new() { Text = "00:00.000", Width = 95 };
    private readonly NumericUpDown count = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 75 };
    private readonly DataGridView grid = new AxisGrid { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None };
    private readonly Label status = new BufferedLabel { Text = "就绪" };
    private readonly Label timer = new BufferedLabel { Text = "F6 录制/结束 · F7 开始/暂停/继续 · F8 停止 · F9 重来" };
    private readonly Recorder recorder;
    private readonly Stopwatch recordingClock = new();
    private readonly HashSet<string> recordingHeld = [];
    private readonly Dictionary<string, AxisEvent> recordingDown = [];
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 100 };
    private Runner? runner;
    private Thread? worker;
    private bool recording;
    private long recordingTotal;
    private DateTime recordOverwriteUntil;
    private string? filePath;
    private readonly StatusOverlay overlay = new();
    public MainForm()
    {
        InitializeThemeLayout();
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Remaining", HeaderText = "剩余时间" });
        grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = "Action", HeaderText = "动作", DataSource = Enum.GetValues<ActionKind>().Select(a => new { Value=a, Text=a switch { ActionKind.SelectStudent=>"选学生", ActionKind.MouseClick=>"鼠标点击", ActionKind.KeyDown=>"按键按下", ActionKind.KeyUp=>"按键松开", ActionKind.MouseDown=>"鼠标按下", ActionKind.MouseUp=>"鼠标松开", ActionKind.Move=>"鼠标移动", _=>"滚轮" } }).ToList(), DisplayMember="Text", ValueMember="Value" });
        foreach (var name in new[] { "Code", "X", "Y", "Delta" }) grid.Columns.Add(new DataGridViewTextBoxColumn { Name=name, DataPropertyName = name, HeaderText = name is "X" or "Y" ? "鼠标 "+name : name });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Extended", HeaderText = "扩展键" });
        grid.DataError += (_, e) => { e.ThrowException = false; status.Text = "单元格格式错误，请修正后继续。"; };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName="IsBattleStart",HeaderText="开战零点",ReadOnly=true });
        InitializeVirtualGrid();
        recorder = new Recorder(); recorder.Captured = CaptureEvent;
        recorder.ControlKey = key => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => HandleControlKey(key)); };
        Shown += (_,_) => { overlay.SetMessage("待机 · F6 录制 / 结束 · F7 回放 / 暂停 · F8 停止 · F9 重来"); overlay.Show(); };
        displayTimer.Tick += (_, _) => { timer.Text = "F6 录制/结束 · F7 开始/暂停/继续 · F8 停止 · F9 重来" + (runner == null ? "" : "    剩余 " + runner.Remaining); if (recording) { status.Text = "已录制 " + AxisDocument.Format(recordingClock.ElapsedMilliseconds) + " · F6 结束后标记跳过动画的确认键为开战零点"; overlay.SetMessage(status.Text,true);  } };
        displayTimer.Start(); Bind();
        FormClosing += (_, e) =>
        {
            if(fileBusy) {e.Cancel=true;status.Text="正在读写轴文件，请等待完成后关闭。";return;}
            if (runner != null) { e.Cancel = true; Guard(() => runner.Stop()); status.Text = "停止后可关闭窗口"; return; }
            if (recording) StopRecording();
            recorder.Dispose(); displayTimer.Dispose(); overlay.Close(); overlay.Dispose();

        };
    }
    private void HandleControlKey(int key)
    {
        Guard(() =>
        {
            switch(key)
            {
                case 1: if(recording) StopRecording(); else StartRecording(); break;
                case 2: if(runner!=null) runner.Pause(); else StartPlayback(); break;
                case 3: if(recording) StopRecording(); runner?.Stop(); break;
                case 4: runner?.Restart(); break;
            }
            overlay.SetMessage(status.Text,recording);
        });
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { status.Text = ex.Message; overlay.SetMessage(ex.Message); } }
    private void Idle() { if (runner != null || recording || fileBusy) throw new InvalidOperationException("请先停止录制或回放。"); }
    private void ReadFields()
    {
        document.Total = total.Text;
        document.End = end.Text;
        document.Version=4; ReadEndControls();
    }
    private void Bind()
    {
        total.Text = document.Total; end.Text = document.End;

        grid.CurrentCell=null;
        document.MigrateSkills(); grid.RowCount=document.Events.Count; grid.Invalidate();

        BindEndControls();
        
    }
    private async void Open()
    {
        try
        {
            Idle();
            using var dialog=new OpenFileDialog { Filter="动作轴 (*.json)|*.json" };
            if(dialog.ShowDialog()!=DialogResult.OK) return;
            await LoadAsync(dialog.FileName);
        }
        catch(Exception ex) {status.Text=ex.Message;overlay.SetMessage(ex.Message);}
    }
    private async void Save()
    {
        try {await SaveAsync();}
        catch(Exception ex) {status.Text=ex.Message;overlay.SetMessage(ex.Message);}
    }
    private static void CheckScreen()
    {
        if (Screen.PrimaryScreen?.Bounds != new Rectangle(0, 0, 1920, 1080)) throw new InvalidOperationException("请将主显示器设为 1920×1080，并让游戏全屏运行。");
    }
    private void StartRecording()
    {
        Idle(); CheckScreen(); ReadFields(); recordingTotal = AxisDocument.Parse(document.Total);
        if (recordingTotal <= 0) throw new ArgumentException("总时长应大于零。");
        if ((document.Events.Count > 0 || document.Skills.Count > 0) && DateTime.UtcNow > recordOverwriteUntil)
        {
            recordOverwriteUntil=DateTime.UtcNow.AddSeconds(5);
            status.Text="当前轴已有操作，5 秒内再次按 F6 确认覆盖录制，或先保存当前轴。";
            overlay.SetMessage(status.Text);
            return;
        }
        recordOverwriteUntil=DateTime.MinValue;
        grid.CurrentCell=null; grid.RowCount=0; document.Events.Clear(); document.Skills.Clear(); document.BattleStartOffsetMs=0; recordingHeld.Clear(); recordingDown.Clear(); SetEditorsReadOnly(true); 
        recordingClock.Restart(); recording = true; status.Text="正在录制，F6 结束录制，F8 停止。"; overlay.SetMessage(status.Text,true);
    }
    private void CaptureEvent(AxisEvent e)
    {
        if (!recording) return;
        if (e.Action == ActionKind.Move && !recordingDown.Values.Any(d => d.Action == ActionKind.MouseDown ||
            (d.Action == ActionKind.KeyDown && !d.Extended && d.Code is "44" or "45" or "46"))) return;
        long elapsed = recordingClock.ElapsedMilliseconds;
        
        string identity = AxisDocument.Identity(e);
        if (e.Action is ActionKind.KeyDown or ActionKind.MouseDown)
        { if (!recordingHeld.Add(identity)) return; recordingDown[identity] = e; }
        if (e.Action is ActionKind.KeyUp or ActionKind.MouseUp)
        { if (!recordingHeld.Remove(identity)) return; recordingDown.Remove(identity); }
        e.Remaining = AxisDocument.Format(recordingTotal - elapsed); document.Events.Add(e);
    }
    private void StopRecording()
    {
        recording = false; recordingClock.Stop();
        document.End = AxisDocument.Format(recordingTotal - recordingClock.ElapsedMilliseconds);
        foreach (var down in recordingDown.Values)
            document.Events.Add(new() { Remaining = document.End, Action = down.Action == ActionKind.KeyDown ? ActionKind.KeyUp : ActionKind.MouseUp, Code = down.Code, Extended = down.Extended, X = Cursor.Position.X, Y = Cursor.Position.Y });
        recordingDown.Clear(); recordingHeld.Clear(); SetEditorsReadOnly(false); Bind(); status.Text = $"已录制 {document.Events.Count} 个事件"; overlay.SetMessage(status.Text);
    }
    private void StartPlayback()
    {
        Idle(); CheckScreen(); ReadFields();
        var snapshot = document.Snapshot();
        runner = new(snapshot, (int)count.Value);
        runner.Status = message => { if (!IsDisposed) BeginInvoke(() => {status.Text = message;overlay.SetMessage(message);}); };
        runner.Finished = () => { if (!IsDisposed) BeginInvoke(() => { runner = null; SetEditorsReadOnly(false); }); };
        SetEditorsReadOnly(true); worker = new Thread(runner.Run) { IsBackground = true, Name = "Axis playback" }; worker.Start();
    }
}









