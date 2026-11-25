namespace BAAxis;

internal sealed partial class MainForm
{
    internal static void CheckEditor()
    {
        using var form=new MainForm();
        string directory=Path.Combine(Path.GetTempPath(),"BAAxis-recording-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        void Check(bool ok) {if(!ok) throw new Exception("Recording editor acceptance failed");}
        void Pump(Task task) {while(!task.IsCompleted) {Application.DoEvents();Thread.Sleep(1);} task.GetAwaiter().GetResult();}
        IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
        try
        {
            form.Show();Application.DoEvents();
            Check(!Children(form).Any(c=>c.Text is "队伍学生" or "添加键鼠动作" or "添加鼠标点击" or "检查卡序与牌位"));
            Check(form.grid.ReadOnly && !form.grid.AllowUserToAddRows && !form.grid.AllowUserToDeleteRows);
            form.StartRecording();
            for(int i=0;i<10000;i++) form.CaptureEvent(new() {Action=ActionKind.Move,X=i%1920,Y=i%1080});
            Check(form.document.Events.Count==0);
            foreach(string scan in new[]{"44","45","46"})
            {
                form.CaptureEvent(new() {Action=ActionKind.KeyDown,Code=scan});
                form.CaptureEvent(new() {Action=ActionKind.Move,X=700,Y=500});
                form.CaptureEvent(new() {Action=ActionKind.MouseDown,Code="Left",X=700,Y=500});
                form.CaptureEvent(new() {Action=ActionKind.MouseUp,Code="Left",X=700,Y=500});
                form.CaptureEvent(new() {Action=ActionKind.KeyUp,Code=scan});
                form.CaptureEvent(new() {Action=ActionKind.Move,X=999,Y=999});
            }
            form.StopRecording();
            Check(form.grid.RowCount==15 && form.document.Compile().Count==15);
            form.grid.CurrentCell=form.grid.Rows[0].Cells[0];
            Check(!form.grid.BeginEdit(false));
            form.filePath=Path.Combine(directory,"录制操作.json");Pump(form.SaveAsync());
            Pump(form.LoadAsync(form.filePath));
            Check(form.grid.RowCount==15 && form.document.Events[2].X==700 && form.grid.ReadOnly);
            form.document=new AxisDocument {End="02:57.000",Events=[
                new() {Action=ActionKind.KeyDown,Code="1",Remaining="02:59.000"},
                new() {Action=ActionKind.KeyUp,Code="1",Remaining="02:58.960"},
                new() {Action=ActionKind.KeyDown,Code="57",Remaining="02:58.000"},
                new() {Action=ActionKind.KeyUp,Code="57",Remaining="02:57.960"}]};
            form.Bind();form.grid.CurrentCell=form.grid.Rows[2].Cells[0];form.MarkBattleStart();
            Check(form.document.BattleStartOffsetMs==2000 && form.document.Events[2].IsBattleStart);
            form.endMode.SelectedIndex=1;form.endWait.Text="00:00.500";form.ReadFields();
            Check(form.document.CheckAt(form.document.Compile())==2540);
            form.ShiftBattleOrigin(0,null);
            Check(form.document.Events[2].Remaining=="02:58.000");
            form.document=new AxisDocument();form.Bind();
            Check(form.grid.RowCount==0 && form.grid.ReadOnly);
            int paints=0;form.Paint+=(_,_)=>paints++;
            for(int i=0;i<200;i++) {form.status.Text="已录制 "+i;form.status.Update();}
            Check(paints==0);
            using var screenshot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,screenshot.Size));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory,"recording-ui.png"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"editor-test-result.txt"),"PASS: recording-only controls, readonly rows, 10,000 idle moves discarded, Z/X/C movement and clicks retained, save/load, battle origin, two end modes, clear with selected row, status refresh without parent repaint.");
        }
        finally {form.Close();}
    }
}
