using System.Diagnostics;

namespace BAAxis;

internal sealed partial class MainForm
{
    internal static void CheckLargeAxis()
    {
        using var form=new MainForm();
        string directory=Path.Combine(Path.GetTempPath(),"BAAxis-large-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var results=new List<string>();
        void Require(bool condition,string message) {if(!condition) throw new Exception(message);}
        double maxGap=0;
        var heartbeat=Stopwatch.StartNew();
        using var timer=new System.Windows.Forms.Timer {Interval=20};
        timer.Tick+=(_,_)=>{maxGap=Math.Max(maxGap,heartbeat.Elapsed.TotalMilliseconds);heartbeat.Restart();};
        void Pump(Task task)
        {
            while(!task.IsCompleted) {Application.DoEvents();Thread.Sleep(1);}
            task.GetAwaiter().GetResult();Application.DoEvents();
        }
        try
        {
            form.Show();Application.DoEvents();
            form.document=new AxisDocument();form.Bind();form.StartRecording();
            var recordingWatch=Stopwatch.StartNew();
            for(int i=0;i<100000;i++) form.CaptureEvent(new() {Action=ActionKind.Move,X=i%1920,Y=i%1080});
            Require(form.document.Events.Count==0,"Idle mouse movements should not be recorded");
            form.CaptureEvent(new() {Action=ActionKind.MouseDown,Code="Left",X=10,Y=20});
            for(int i=0;i<100000;i++) form.CaptureEvent(new() {Action=ActionKind.Move,X=i%1920,Y=i%1080});
            form.CaptureEvent(new() {Action=ActionKind.MouseUp,Code="Left",X=99999%1920,Y=99999%1080});
            recordingWatch.Restart();form.StopRecording();
            Require(form.document.Events.Count==100002 && form.document.Events[^1].X==99999%1920,"Recording stop lost held-mouse events");
            results.Add($"PASS actual capture/stop entry points: 100,000 idle moves discarded, 100,000 held-mouse moves plus down/up retained, stop+bind {recordingWatch.Elapsed.TotalMilliseconds:F1}ms.");
            foreach(int count in new[]{100000,600000})
            {
                var events=new List<AxisEvent>(count);
                for(int i=0;i<count;i++) events.Add(new() {Action=ActionKind.Move,Remaining=AxisDocument.Format(600000-i),X=i%1920,Y=i%1080});
                form.document=new AxisDocument {Total="10:00.000",Events=events};
                var watch=Stopwatch.StartNew();form.Bind();form.Update();
                double bind=watch.Elapsed.TotalMilliseconds;
                Require(form.grid.VirtualMode && form.grid.RowCount==count,"Virtual row count differs from recording");
                Require(bind<1500,$"Binding {count} rows blocked UI for {bind:F0}ms");
                int middle=count/2;
                form.grid.CurrentCell=form.grid.Rows[middle].Cells[form.grid.Columns["X"]!.Index];
                Require(form.grid.ReadOnly && !form.grid.BeginEdit(false),"Recorded rows must be readonly");
                watch.Restart();
                for(int i=0;i<20;i++) {form.grid.FirstDisplayedScrollingRowIndex=i*(count-20)/20;form.grid.Update();}
                double scroll=watch.Elapsed.TotalMilliseconds;
                form.filePath=Path.Combine(directory,$"axis-{count}.json");
                maxGap=0;heartbeat.Restart();timer.Start();
                watch.Restart();Pump(form.SaveAsync());double save=watch.Elapsed.TotalMilliseconds;
                watch.Restart();Pump(form.LoadAsync(form.filePath));double load=watch.Elapsed.TotalMilliseconds;
                timer.Stop();
                Require(form.document.Events.Count==count,"Save/load dropped events");
                Require(form.document.Events[middle].X==middle%1920 && form.document.Events[^1].Y==(count-1)%1080,"Save/load changed event data");
                Require(maxGap<1000,$"File operation stalled UI heartbeat for {maxGap:F0}ms");
                results.Add($"PASS {count:N0} events: bind {bind:F1}ms, 20 scrolls {scroll:F1}ms, save {save:F1}ms, load+validate {load:F1}ms, max UI heartbeat gap {maxGap:F1}ms, readonly view and full count preserved.");
                form.grid.CurrentCell=form.grid.Rows[1000].Cells[0];
                watch.Restart();form.MarkBattleStart();double mark=watch.Elapsed.TotalMilliseconds;
                Require(form.document.BattleStartOffsetMs==1000 && form.document.Events[1000].IsBattleStart,"Long-axis marker was not applied");
                watch.Restart();form.ShiftBattleOrigin(0,null);double clear=watch.Elapsed.TotalMilliseconds;
                Require(form.document.Events[0].Remaining=="10:00.000" && mark<1500 && clear<1500,"Long-axis origin edit stalled or changed time");
                results.Add($"PASS {count:N0} events: mark battle origin {mark:F1}ms, clear origin {clear:F1}ms, original timestamp restored.");
            }
            results.Add("Synthetic data. No input was sent to the game. Fixtures: "+directory);
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"large-axis-result.txt"),results);
        }
        finally {timer.Stop();form.Close();}
    }
}

