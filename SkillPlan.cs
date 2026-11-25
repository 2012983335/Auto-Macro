namespace BAAxis;

public sealed class SkillAction
{
    public string SelectRemaining { get; set; } = "03:00.000";
    public string ReleaseRemaining { get; set; } = "02:59.900";
    public int StudentId { get; set; } = 1;
    public int X { get; set; } = 960;
    public int Y { get; set; } = 540;
}

public sealed partial class AxisDocument
{
    public int StudentCount { get; set; } = 6;
    public List<string> Students { get; set; } = Enumerable.Repeat("", 10).ToList();
    public string OpeningOrder { get; set; } = "1,2,3,4,5";
    public List<SkillAction> Skills { get; set; } = [];

    public List<(SkillAction Skill, int Slot)> CompileSkills()
    {
        if (StudentCount is not (6 or 10)) throw new ArgumentException("队伍人数应为 6 或 10。");
        var tokens = OpeningOrder.Replace('，', ',').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => !int.TryParse(t, out _))) throw new ArgumentException("预选顺序应填写逗号分隔的学生编号。");
        var order = tokens.Select(int.Parse).ToList();
        if (order.Count < StudentCount - 1 || order.Count > StudentCount || order.Distinct().Count() != order.Count || order.Any(i => i < 1 || i > StudentCount))
            throw new ArgumentException($"预选顺序需包含 {StudentCount-1} 或 {StudentCount} 个不重复的学生编号。");
        order.AddRange(Enumerable.Range(1, StudentCount).Except(order).ToArray());
        int handSize = StudentCount == 6 ? 3 : 5;
        var hand = order.Take(handSize).ToArray();
        var queue = new Queue<int>(order.Skip(handSize));
        var result = new List<(SkillAction Skill, int Slot)>();
        long total = Parse(Total), end = EndMode==EndCheckMode.Remaining?Parse(End):0, previousRelease = -1;
        foreach (var skill in Skills.OrderBy(s => total - Parse(s.SelectRemaining)))
        {
            long select = total - Parse(skill.SelectRemaining), release = total - Parse(skill.ReleaseRemaining);
            if (select < 0 || (EndMode == EndCheckMode.Remaining && release > total-end) || release-select < 40)
                throw new ArgumentException("EX 选牌与释放须在轴内，释放应晚于选牌至少 40 毫秒。");
            if (select <= previousRelease) throw new ArgumentException("下一次 EX 选牌须晚于上一次释放。");
            if (skill.StudentId < 1 || skill.StudentId > StudentCount || Students.Count < skill.StudentId || string.IsNullOrWhiteSpace(Students[skill.StudentId-1]))
                throw new ArgumentException($"请先填写学生 {skill.StudentId} 的名字，并确认队伍模式。");
            if (skill.X < 0 || skill.X >= 1920 || skill.Y < 0 || skill.Y >= 1080)
                throw new ArgumentException("EX 目标坐标应位于 1920×1080 范围内。");
            int slot = Array.IndexOf(hand, skill.StudentId);
            if (slot < 0) throw new ArgumentException($"剩余 {skill.SelectRemaining}，{Students[skill.StudentId-1]} 尚未在手牌中。当前学生编号为 {string.Join(",", hand)}。");
            result.Add((skill, slot+1));
            hand[slot] = queue.Dequeue();
            queue.Enqueue(skill.StudentId);
            previousRelease = release;
        }
        return result;
    }

    private IEnumerable<AxisEvent> ExpandSkills()
    {
        // Raw recordings have no semantic EX rows and retain their original behavior.
        if (Skills.Count == 0) yield break;
        long total = Parse(Total);
        foreach (var (skill, slot) in CompileSkills())
        {
            long select = total-Parse(skill.SelectRemaining), release = total-Parse(skill.ReleaseRemaining);
            yield return new() { Remaining = Format(total-select), Action = ActionKind.KeyDown, Code = (slot+1).ToString() };
            yield return new() { Remaining = Format(total-select-20), Action = ActionKind.KeyUp, Code = (slot+1).ToString() };
            yield return new() { Remaining = Format(total-release+20), Action = ActionKind.MouseDown, Code = "Left", X = skill.X, Y = skill.Y };
            yield return new() { Remaining = Format(total-release), Action = ActionKind.MouseUp, Code = "Left", X = skill.X, Y = skill.Y };
        }
    }
}
