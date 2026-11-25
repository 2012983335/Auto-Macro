using System.Diagnostics;

namespace BAAxis;

internal sealed class Runner
{
    private readonly object gate = new();
    private readonly Dictionary<string, AxisEvent> held = [];
    private readonly Stopwatch clock = new();
    private readonly AxisDocument document;
    private readonly int limit;
    private readonly Action<AxisEvent> sendInput;
    private readonly Func<Rectangle, Bitmap> captureScreen;
    private bool stopped, paused, restart, reopening;
    private bool interrupted;
    private double liveBattleOffset;
    public Action<string>? Status;
    public Action? Finished;
    private readonly List<string> timings = ["round,event,planned_ms,actual_ms,error_ms"];
    public Runner(AxisDocument document, int limit, Action<AxisEvent>? sendInput = null, Func<Rectangle, Bitmap>? captureScreen = null)
    {
        this.document = document; this.limit = limit;
        this.sendInput = sendInput ?? Native.Send;
        this.captureScreen = captureScreen ?? Capture;
        liveBattleOffset=document.BattleStartOffsetMs;
    }
    public string Remaining { get { lock (gate) return clock.Elapsed.TotalMilliseconds < liveBattleOffset ? "前置重开 " + AxisDocument.Format((long)(liveBattleOffset-clock.Elapsed.TotalMilliseconds)) : AxisDocument.Format((long)(AxisDocument.Parse(document.Total)+liveBattleOffset-clock.Elapsed.TotalMilliseconds)); } }
    public void Stop() { lock (gate) { stopped = true; clock.Stop(); Release(); Monitor.PulseAll(gate); } }
    public void Restart()
    {
        lock (gate)
        {
            if ((reopening && !paused) || restart) return;
            restart = true;
            paused = false;
            interrupted = false;
            Release();
            Monitor.PulseAll(gate);
        }
    }
    public void Pause()
    {
        lock (gate)
        {
            if (interrupted) { Status?.Invoke("长按已中断，请按 F9 重来或 F8 停止"); return; }
            paused = !paused;
            if (paused) { clock.Stop(); interrupted = held.Count > 0; Release(); }
            else clock.Start();
            Status?.Invoke(paused ? (interrupted ? "长按已中断，请按 F9 重来" : "已暂停，F7 继续") : "继续执行");
            Monitor.PulseAll(gate);
        }
    }
    private void Release()
    {
        var failures = new List<Exception>();
        foreach (var pair in held.ToArray())
        {
            var e = pair.Value;
            var up = new AxisEvent { Action = e.Action == ActionKind.KeyDown ? ActionKind.KeyUp : ActionKind.MouseUp, Code = e.Code, Extended = e.Extended, X = e.X, Y = e.Y };
            try
            {
                sendInput(up);
                held.Remove(pair.Key);
            }
            catch (Exception ex)
            {
                failures.Add(new IOException($"{pair.Key} 松开发送失败", ex));
            }
        }
        if (failures.Count == 0) return;
        stopped = true;
        clock.Stop();
        Monitor.PulseAll(gate);
        throw new IOException("未能释放 " + string.Join("、", held.Keys), new AggregateException(failures));
    }
    private void Send(AxisEvent e)
    {
        sendInput(e);
        if (e.Action is ActionKind.KeyDown or ActionKind.MouseDown) held[AxisDocument.Identity(e)] = new() { Action = e.Action, Code = e.Code, Extended = e.Extended, X = e.X, Y = e.Y };
        if (e.Action is ActionKind.KeyUp or ActionKind.MouseUp) held.Remove(AxisDocument.Identity(e));
        if (e.Action == ActionKind.Move)
            foreach (var down in held.Values.Where(x => x.Action == ActionKind.MouseDown)) { down.X = e.X; down.Y = e.Y; }
    }
    private void Wait(double target)
    {
        while (true)
        {
            lock (gate)
            {
                if (stopped) throw new OperationCanceledException();
                if (restart) throw new RestartRequested();
                if (paused) { Monitor.Wait(gate, 20); continue; }
                double remaining = target - clock.Elapsed.TotalMilliseconds;
                if (remaining <= 0) return;
                if (remaining > 2) Monitor.Wait(gate, Math.Max(1, (int)Math.Min(remaining - 1, 10)));
            }
            Thread.SpinWait(100);
        }
    }
    private void Delay(int ms) => Wait(clock.Elapsed.TotalMilliseconds + ms);
    private double SendAt(AxisEvent e, double at)
    {
        while (true)
        {
            Wait(at);
            lock (gate)
            {
                if (stopped) throw new OperationCanceledException();
                if (restart) throw new RestartRequested();
                if (paused) continue;
                double actual = clock.Elapsed.TotalMilliseconds;
                Send(e);
                return actual;
            }
        }
    }
    private void Tap(string scan, bool mouse = false, Point position = default)
    {
        SendAt(new() { Action = mouse ? ActionKind.MouseDown : ActionKind.KeyDown, Code = mouse ? "Left" : scan, X = position.X, Y = position.Y }, clock.Elapsed.TotalMilliseconds);
        SendAt(new() { Action = mouse ? ActionKind.MouseUp : ActionKind.KeyUp, Code = mouse ? "Left" : scan, X = position.X, Y = position.Y }, clock.Elapsed.TotalMilliseconds + 40);
    }
    private bool ShouldContinue()
    {
        Status?.Invoke("按 Esc 检查是否进入下一轮");
        Tap("1");
        double deadline=clock.Elapsed.TotalMilliseconds+2000;
        using var sample=RestartDetection.CreateSample();
        while(clock.Elapsed.TotalMilliseconds<deadline)
        {
            Delay(100);
            using var screen=captureScreen(RestartDetection.Region);
            if(!Matches(sample,screen)) continue;
            Status?.Invoke("已识别暂停菜单，关闭菜单后执行下一轮前置重开");
            Tap("1");
            return true;
        }
        Status?.Invoke("未出现暂停菜单，已结束循环");
        return false;
    }
    public void Run()
    {
        var previousPriority=Thread.CurrentThread.Priority;
        bool timerPeriodSet=false;
        try
        {
            timerPeriodSet=Native.timeBeginPeriod(1)==0;
            Thread.CurrentThread.Priority=ThreadPriority.AboveNormal;
            var events=document.Compile();
            if(limit>1 && document.BattleStartOffsetMs<=0)
                throw new ArgumentException("循环前请录下开头重开操作，并标记开战零点。旧轴可单次执行。");
            long checkAt=document.CheckAt(events);
            int round=1;
            while(round<=limit)
            {
                try
                {
                    lock(gate) { if(stopped) throw new OperationCanceledException(); liveBattleOffset=document.BattleStartOffsetMs; clock.Restart(); if(paused) clock.Stop(); }
                    Status?.Invoke($"执行第 {round}/{limit} 轮 · 前置重开及战斗轴");
                    int index=0;
                    double battleShift=0;
                    foreach(var item in events)
                    {
                        double target=item.At+battleShift;
                        double actual=SendAt(item.Event,target);
                        if(item.Event.IsBattleStart)
                        {
                            battleShift=actual-item.At;
                            lock(gate) liveBattleOffset=actual;
                        }
                        timings.Add(FormattableString.Invariant($"{round},{index++},{target:F3},{actual:F3},{actual-target:F3}"));
                    }
                    Status?.Invoke("操作执行完毕，等待收轴判定时间");
                    Wait(document.EndMode==EndCheckMode.AfterLastAction ? clock.Elapsed.TotalMilliseconds+document.EndWaitMs : checkAt+battleShift);
                    lock(gate) Release();
                    if(round==limit) { Status?.Invoke("已达到次数上限"); break; }
                    lock(gate) reopening=true;
                    bool next;
                    try { next=ShouldContinue(); }
                    finally { lock(gate) reopening=false; }
                    if(!next) break;
                    round++;
                }
                catch(RestartRequested)
                {
                    lock(gate) { restart=false; paused=false; interrupted=false; Release(); }
                    if(document.BattleStartOffsetMs<=0) throw new ArgumentException("此轴尚无前置重开片段，请先标记开战零点。");
                    Status?.Invoke("重试本轮，将重新执行前置重开片段");
                }
            }
        }
        catch(OperationCanceledException) { Status?.Invoke("已停止"); }
        catch(Exception ex) { Status?.Invoke(ex.Message); }
        finally
        {
            lock(gate) { try { Release(); } catch(Exception ex) { Status?.Invoke("释放失败，请手动松开键鼠："+ex.Message); } }
            if(timerPeriodSet) Native.timeEndPeriod(1);
            Thread.CurrentThread.Priority=previousPriority;
            try { Directory.CreateDirectory("logs"); File.WriteAllLines(Path.Combine("logs",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".csv"),timings); }
            catch(Exception ex) { Status?.Invoke("日志保存失败："+ex.Message); }
            Finished?.Invoke();
        }
    }
    public static Bitmap Capture(Rectangle region)
    {
        var image = new Bitmap(region.Width, region.Height);
        using var g = Graphics.FromImage(image);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return image;
    }
    public static bool Matches(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        long error = 0, count = 0;
        for (int y = 0; y < a.Height; y += 2)
            for (int x = 0; x < a.Width; x += 2)
            { var p = a.GetPixel(x,y); var q = b.GetPixel(x,y); error += Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B); count += 3; }
        return count > 0 && error / (double)count <= 12;
    }
    private sealed class RestartRequested : Exception;
}

