using System.Globalization;
using System.Text.Json;

namespace BAAxis;

public enum ActionKind { KeyDown, KeyUp, MouseDown, MouseUp, Move, Wheel, SelectStudent, MouseClick }
public enum EndCheckMode { Remaining, AfterLastAction }

public sealed class AxisEvent
{
    internal AxisEvent Snapshot() => (AxisEvent)MemberwiseClone();
    public string Remaining { get; set; } = "03:00.000";
    public ActionKind Action { get; set; }
    // Keyboard uses Windows scan codes. Mouse uses Left, Right, Middle, X1, X2.
    public string Code { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public bool Extended { get; set; }
    public int Delta { get; set; }
    public int StudentId { get; set; } = 1;
    public bool IsBattleStart { get; set; }
}

public sealed partial class AxisDocument
{
    internal AxisDocument Snapshot()
    {
        var copy=(AxisDocument)MemberwiseClone();
        copy.Events=Events.Select(e=>e.Snapshot()).ToList();
        copy.Students=Students.ToList();
        copy.Skills=Skills.Select(s=>new SkillAction { SelectRemaining=s.SelectRemaining,ReleaseRemaining=s.ReleaseRemaining,StudentId=s.StudentId,X=s.X,Y=s.Y }).ToList();
        return copy;
    }
    public int Version { get; set; } = 4;
    public string Total { get; set; } = "03:00.000";
    public string End { get; set; } = "00:00.000";
    public List<AxisEvent> Events { get; set; } = [];
    public long BattleStartOffsetMs { get; set; }
    public EndCheckMode EndMode { get; set; }
    public long EndWaitMs { get; set; } = 2000;

    public static long Parse(string value)
    {
        bool negative = value.StartsWith('-');
        if (!TimeSpan.TryParseExact(negative ? value[1..] : value, @"mm\:ss\.fff", CultureInfo.InvariantCulture, out var time))
            throw new ArgumentException("时间格式应为 分:秒.毫秒，例如 03:00.000（分钟 00–59）。");
        return (negative ? -1 : 1) * (long)time.TotalMilliseconds;
    }

    public static string Format(long milliseconds) => (milliseconds < 0 ? "-" : "") + TimeSpan.FromMilliseconds(Math.Abs(milliseconds))
        .ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);

    public List<(AxisEvent Event, long At)> Compile()
    {
        if (Version is not (1 or 2 or 3 or 4)) throw new ArgumentException("不支持此动作轴版本。");
        long total = Parse(Total), end = EndMode==EndCheckMode.Remaining?Parse(End):0;
        if (total <= 0 || !Enum.IsDefined(EndMode) || EndWaitMs < 0 || BattleStartOffsetMs < 0)
            throw new ArgumentException("总时间、前置时长或收轴设置无效。");
        if (EndMode == EndCheckMode.Remaining && (end < 0 || end > total))
            throw new ArgumentException("按剩余时间收轴时，应填写 0 到总时长之间的时间。");
        var markers=Events.Where(e=>e.IsBattleStart).ToList();
        if (markers.Count > 1 || (BattleStartOffsetMs > 0 && markers.Count != 1) || markers.Any(e=>Parse(e.Remaining)!=total))
            throw new ArgumentException("请重新标记开战零点，零点行的剩余时间须等于总时长。");
        var ordered = ExpandSelections(Events.Concat(ExpandSkills())).Select(e => (Event: e, At: total - Parse(e.Remaining) + BattleStartOffsetMs)).OrderBy(e => e.At).ToList();
        var held = new HashSet<string>();
        foreach (var item in ordered)
        {
            var e = item.Event;
            if (!Enum.IsDefined(e.Action) || item.At < 0 || (EndMode == EndCheckMode.Remaining && item.At > BattleStartOffsetMs + total - end))
                throw new ArgumentException("动作必须位于起轴与收轴之间。");
            if (e.X < 0 || e.X >= 1920 || e.Y < 0 || e.Y >= 1080)
                throw new ArgumentException("坐标应位于 1920×1080 范围内。");
            if (e.Action is ActionKind.KeyDown or ActionKind.KeyUp)
            {
                if (!ushort.TryParse(e.Code, out var code) || code is 0 or > 255)
                    throw new ArgumentException("键盘 Code 应为扫描码 1–255，可用录制取得。");
            }
            if (e.Action is ActionKind.MouseDown or ActionKind.MouseUp && !Native.Buttons.Contains(e.Code))
                throw new ArgumentException("鼠标 Code 应为 Left、Right、Middle、X1 或 X2。");
            var key = Identity(e);
            if (e.Action is ActionKind.KeyDown or ActionKind.MouseDown)
            {
                if (!held.Add(key)) throw new ArgumentException("同一按键重复按下，请补充松开事件。");
            }
            if (e.Action is ActionKind.KeyUp or ActionKind.MouseUp)
            {
                if (!held.Remove(key)) throw new ArgumentException("松开事件前缺少对应按下事件。");
            }
        }
        if (held.Count != 0) throw new ArgumentException("动作轴存在未松开的键鼠。");
        return ordered;
    }

    public long CheckAt(List<(AxisEvent Event,long At)> events) => EndMode == EndCheckMode.AfterLastAction
        ? (events.Count == 0 ? BattleStartOffsetMs : events.Max(e=>e.At)) + EndWaitMs
        : BattleStartOffsetMs + Parse(Total) - Parse(End);

    public static string Identity(AxisEvent e)
    {
        bool keyboard = e.Action is ActionKind.KeyDown or ActionKind.KeyUp;
        string code = keyboard && ushort.TryParse(e.Code, out var scan) ? scan.ToString(CultureInfo.InvariantCulture) : e.Code;
        return $"{(keyboard ? "K" : "M")}/{code}/{e.Extended}";
    }
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
