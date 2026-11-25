namespace BAAxis;

public sealed partial class AxisDocument
{
    public void MigrateSkills()
    {
        if(Version is not (1 or 2 or 3 or 4)) throw new ArgumentException("不支持此动作轴版本。");
        foreach (var skill in Skills)
        {
            Events.Add(new() { Action=ActionKind.SelectStudent,StudentId=skill.StudentId,Remaining=skill.SelectRemaining });
            Events.Add(new() { Action=ActionKind.MouseDown,Code="Left",Remaining=Format(Parse(skill.ReleaseRemaining)+20),X=skill.X,Y=skill.Y });
            Events.Add(new() { Action=ActionKind.MouseUp,Code="Left",Remaining=skill.ReleaseRemaining,X=skill.X,Y=skill.Y });
        }
        Skills.Clear();
        Version=4;
    }
    private IEnumerable<AxisEvent> ExpandClicks(IEnumerable<AxisEvent> events)
    {
        foreach(var e in events)
        {
            if(e.Action!=ActionKind.MouseClick) { yield return e; continue; }
            yield return new() { Action=ActionKind.MouseDown,Code=e.Code,Remaining=e.Remaining,X=e.X,Y=e.Y,IsBattleStart=e.IsBattleStart };
            yield return new() { Action=ActionKind.MouseUp,Code=e.Code,Remaining=Format(Parse(e.Remaining)-20),X=e.X,Y=e.Y };
        }
    }
    public Dictionary<AxisEvent,int> SelectionSlots() => PlanSelections(ExpandClicks(Events).ToList());
    private Dictionary<AxisEvent,int> PlanSelections(List<AxisEvent> events)
    {
        if(!events.Any(e=>e.Action==ActionKind.SelectStudent)) return [];
        var selected=new List<AxisEvent>();
        var skills=new List<SkillAction>();
        AxisEvent? pending=null;
        foreach(var e in events.OrderByDescending(e=>Parse(e.Remaining)))
        {
            if(e.Action==ActionKind.SelectStudent)
            {
                if(pending!=null) throw new ArgumentException("上一名学生尚未放出技能，请在两次选牌之间添加鼠标点击或左键松开。");
                pending=e;
            }
            if(e.Action==ActionKind.MouseUp && e.Code=="Left" && pending!=null)
            {
                selected.Add(pending);
                skills.Add(new() { StudentId=pending.StudentId,SelectRemaining=pending.Remaining,ReleaseRemaining=e.Remaining,X=e.X,Y=e.Y });
                pending=null;
            }
        }
        if(pending!=null) throw new ArgumentException("选牌后请添加鼠标点击，或拖动结束的左键松开动作。");
        if(selected.Count==0) return [];
        var plan=new AxisDocument { Total=Total,End=End,EndMode=EndMode,StudentCount=StudentCount,Students=Students,OpeningOrder=OpeningOrder,Skills=skills }.CompileSkills();
        return selected.Select((e,i)=>(e,plan[i].Slot)).ToDictionary(x=>x.e,x=>x.Slot);
    }
    private IEnumerable<AxisEvent> ExpandSelections(IEnumerable<AxisEvent> source)
    {
        var events=ExpandClicks(source).ToList();
        var slots=PlanSelections(events);
        foreach(var e in events)
        {
            if(e.Action!=ActionKind.SelectStudent) { yield return e; continue; }
            yield return new() { Action=ActionKind.KeyDown,Code=(slots[e]+1).ToString(),Remaining=e.Remaining,IsBattleStart=e.IsBattleStart };
            yield return new() { Action=ActionKind.KeyUp,Code=(slots[e]+1).ToString(),Remaining=Format(Parse(e.Remaining)-20) };
        }
    }
}
