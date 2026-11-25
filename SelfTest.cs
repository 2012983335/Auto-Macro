namespace BAAxis;

internal static class SelfTest
{
    public static void Run(string[] screenshots)
    {
        void Check(bool condition) { if (!condition) throw new Exception("Self-test failed"); }
        CheckSkills();
        Check(AxisDocument.Parse("02:29.950") == 149950);
        Check(AxisDocument.Format(149950) == "02:29.950");
        var doc = new AxisDocument { Events = [new() { Remaining = "02:59.000", Action = ActionKind.KeyDown, Code = "16" }, new() { Remaining = "02:58.000", Action = ActionKind.KeyUp, Code = "16" }] };
        Check(doc.Compile()[0].At == 1000);
        doc.Events.RemoveAt(1);
        bool rejected = false;
        try { doc.Compile(); } catch (ArgumentException) { rejected = true; }
        Check(rejected);
        using var a = new Bitmap(20,20); using var b = new Bitmap(20,20);
        Check(Runner.Matches(a,b)); using (var g = Graphics.FromImage(b)) g.Clear(Color.White);
        Check(!Runner.Matches(a,b));
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AxisDocument>("{\"Total\":\"03:00.000\",\"RestartRegion\":{},\"RestartImage\":\"legacy\"}")!;
        Check(roundtrip.Total == "03:00.000");
        roundtrip.Compile();
        // Interrupt a held key, verifying that cleanup is emitted and no later event runs.
        var sent = new List<ActionKind>();
        using var pressed = new ManualResetEventSlim();
        var playback = new AxisDocument { Total = "00:10.000", End = "00:00.000", Events = [new() { Remaining = "00:10.000", Action = ActionKind.KeyDown, Code = "16" }, new() { Remaining = "00:01.000", Action = ActionKind.KeyUp, Code = "16" }] };
        var runner = new Runner(playback, 1, e => { lock(sent) sent.Add(e.Action); if (e.Action == ActionKind.KeyDown) pressed.Set(); });
        var thread = new Thread(runner.Run); thread.Start(); Check(pressed.Wait(2000)); runner.Stop(); Check(thread.Join(2000));
        Check(sent.SequenceEqual(new[] { ActionKind.KeyDown, ActionKind.KeyUp }));
        sent.Clear(); pressed.Reset();
        runner = new Runner(playback, 1, e => { lock(sent) sent.Add(e.Action); if (e.Action == ActionKind.KeyDown) pressed.Set(); });
        thread = new Thread(runner.Run); thread.Start(); Check(pressed.Wait(2000)); runner.Pause(); runner.Pause(); runner.Stop(); Check(thread.Join(2000));
        Check(sent.SequenceEqual(new[] { ActionKind.KeyDown, ActionKind.KeyUp }));
        // Exercise the entire two-round restart sequence without sending real inputs.
        using var sample = RestartDetection.CreateSample();
        var looping = new AxisDocument { Total = "00:01.000", End = "00:01.000", BattleStartOffsetMs=10, Events=[new() {Action=ActionKind.Move,Remaining="00:01.010"},new() {Action=ActionKind.Move,Remaining="00:01.000",IsBattleStart=true}] };
        var inputs = new List<string>();
        runner = new Runner(looping, 2, e => inputs.Add($"{e.Action}/{e.Code}"), _ => new Bitmap(sample));
        thread = new Thread(runner.Run); thread.Start(); Check(thread.Join(8000));
        Check(inputs.SequenceEqual(new[] { "Move/", "Move/", "KeyDown/1", "KeyUp/1", "KeyDown/1", "KeyUp/1", "Move/", "Move/" }));
        inputs.Clear();
        runner = new Runner(looping, 2, e => inputs.Add($"{e.Action}/{e.Code}"), _ => new Bitmap(b));
        thread = new Thread(runner.Run); thread.Start(); Check(thread.Join(4000));
        Check(inputs.SequenceEqual(new[] { "Move/", "Move/", "KeyDown/1", "KeyUp/1" }));
        if (screenshots.Length == 4)
        {
            // A crop inside the restart button must distinguish all supplied screens.
            var region = RestartDetection.Region;
            using var crop = RestartDetection.CreateSample();
            for (int i = 0; i < screenshots.Length; i++)
            {
                using var screen = new Bitmap(screenshots[i]);
                Check(screen.Size == new Size(1920, 1080));
                using var candidate = screen.Clone(region, screen.PixelFormat);
                Check(Runner.Matches(crop, candidate) == (i == 1));
            }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: student EX 6/3/5 and 10/5/9 plans, slot replacement, semantic event expansion, legacy document compatibility, time conversion, event ordering, unbalanced key validation, image matching, JSON roundtrip, stop releases held key, interrupted pause requires restart, two-round restart key sequence, missing button stops loop" + (screenshots.Length == 4 ? ", four supplied screenshots distinguished" : ""));
    }

    private static void CheckSkills()
    {
        void Check(bool value) { if(!value) throw new Exception("Student EX self-test failed"); }
        SkillAction Skill(int id,int select) => new() { StudentId=id,SelectRemaining=AxisDocument.Format(1000-select),ReleaseRemaining=AxisDocument.Format(900-select),X=700,Y=600 };
        var normal=new AxisDocument { Total="00:01.000",Students=Enumerable.Range(1,10).Select(i=>$"名字{i}").ToList(),Skills=[Skill(2,0),Skill(4,200),Skill(1,400),Skill(3,600)] };
        Check(normal.CompileSkills().Select(s=>s.Slot).SequenceEqual(new[]{2,2,1,3}));
        normal.Skills.Add(Skill(2,800));
        Check(normal.CompileSkills().Select(s=>s.Slot).SequenceEqual(new[]{2,2,1,3,3}));
        normal.Skills.RemoveAt(4);
        var expanded=normal.Compile();
        Check(expanded.Count==16);
        Check(expanded.Where(e=>e.Event.Action==ActionKind.KeyDown).Select(e=>e.Event.Code).SequenceEqual(new[]{"3","3","2","4"}));
        Check(expanded[3].At==100 && expanded[3].Event.Action==ActionKind.MouseUp && expanded[3].Event.X==700);
        var ten=new AxisDocument { StudentCount=10,OpeningOrder="1,2,3,4,5,6,7,8,9",Total="00:01.000",Students=normal.Students,Skills=[Skill(5,0),Skill(6,200),Skill(1,400)] };
        Check(ten.CompileSkills().Select(s=>s.Slot).SequenceEqual(new[]{5,5,1}));
        Check(ten.Compile().First().Event.Code=="6");
        var restored=System.Text.Json.JsonSerializer.Deserialize<AxisDocument>(System.Text.Json.JsonSerializer.Serialize(ten))!;
        Check(restored.CompileSkills().Select(s=>s.Slot).SequenceEqual(new[]{5,5,1}));
        Check(restored.Students[5]=="名字6");
        Check(normal.CompileSkills().Select(s=>s.Slot).SequenceEqual(new[]{2,2,1,3}));
        normal.Skills=[Skill(6,0)];
        bool rejected=false;
        try { normal.Compile(); } catch(ArgumentException) { rejected=true; }
        Check(rejected);
        normal.Skills=[Skill(1,0),Skill(2,50)];rejected=false;
        try { normal.Compile(); } catch(ArgumentException) { rejected=true; }
        Check(rejected);
        normal.Skills=[];normal.Version=1; Check(normal.Compile().Count==0);
    }
}

